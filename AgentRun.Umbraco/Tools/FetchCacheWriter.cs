using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentRun.Umbraco.Tools;

// Story 10.7a Track A: extracted from FetchUrlTool. Owns the `.fetch-cache/`
// path sandbox + file I/O. FetchUrlTool stays the transport + validation
// owner; this writer is the only class in Tools/ that touches the cache
// directory on disk.
//
// Defence-in-depth invariant (architect review 2026-04-08, Story 9.10): every
// write path MUST go through PathSandbox.ValidatePath and use the canonical
// path it returns — do NOT separately compute a target via Path.Combine.
public class FetchCacheWriter : IFetchCacheWriter
{
    private static readonly JsonSerializerOptions HandleJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly ILogger<FetchCacheWriter> _logger;

    public FetchCacheWriter(ILogger<FetchCacheWriter>? logger = null)
    {
        _logger = logger ?? NullLogger<FetchCacheWriter>.Instance;
    }

    public async Task<string> WriteHandleAsync(
        string instanceFolderPath,
        string url,
        int status,
        string contentType,
        byte[] body,
        int unmarkedLength,
        bool truncated,
        CancellationToken cancellationToken)
    {
        byte[] bytesToWrite;
        if (truncated)
        {
            var marker = Encoding.UTF8.GetBytes($"\n\n[Response truncated at {unmarkedLength} bytes]");
            bytesToWrite = new byte[unmarkedLength + marker.Length];
            Buffer.BlockCopy(body, 0, bytesToWrite, 0, unmarkedLength);
            Buffer.BlockCopy(marker, 0, bytesToWrite, unmarkedLength, marker.Length);
        }
        else
        {
            bytesToWrite = new byte[unmarkedLength];
            Buffer.BlockCopy(body, 0, bytesToWrite, 0, unmarkedLength);
        }

        // Hash-derived filename (SHA-256, never SHA-1 or MD5).
        var relPath = $".fetch-cache/{ComputeUrlHash(url)}.html";

        // Defence-in-depth (architect review 2026-04-08): obtain the canonical
        // absolute path via PathSandbox.ValidatePath and write to *that* path —
        // do NOT separately compute a write target via Path.Combine.
        string validatedPath;
        try
        {
            validatedPath = PathSandbox.ValidatePath(relPath, instanceFolderPath);
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            throw new ToolExecutionException(
                $"Failed to cache fetch_url response to {relPath}: {ex.Message}");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(validatedPath)!);
            await File.WriteAllBytesAsync(validatedPath, bytesToWrite, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ToolExecutionException(
                $"Failed to cache fetch_url response to {relPath}: {ex.Message}");
        }

        var handle = new FetchUrlHandle(
            url,
            status,
            contentType,
            bytesToWrite.LongLength,
            relPath,
            truncated);

        return JsonSerializer.Serialize(handle, HandleJsonOptions);
    }

    // Story 10.6 Task 0.5 — probe the instance's .fetch-cache/ for a prior
    // response to this URL and build a handle indistinguishable from the
    // cache-miss case (status, content_type, size_bytes, saved_to, truncated
    // all populated from the on-disk file). Returns null on any miss,
    // malformed cache path, or IO failure — callers fall through to the
    // normal HTTP path.
    public Task<string?> TryReadHandleAsync(
        string instanceFolderPath,
        string url,
        CancellationToken cancellationToken)
    {
        var relPath = $".fetch-cache/{ComputeUrlHash(url)}.html";

        string validatedPath;
        try
        {
            validatedPath = PathSandbox.ValidatePath(relPath, instanceFolderPath);
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex,
                "fetch_url cache lookup skipped: path validation failed for {RelPath}", relPath);
            return Task.FromResult<string?>(null);
        }

        if (!File.Exists(validatedPath))
            return Task.FromResult<string?>(null);

        long size;
        bool truncated;
        try
        {
            var info = new FileInfo(validatedPath);
            size = info.Length;
            truncated = FileTailContainsTruncationMarker(validatedPath, size);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex,
                "fetch_url cache lookup skipped: IO error reading {RelPath}", relPath);
            return Task.FromResult<string?>(null);
        }

        // Content-Type is not persisted with the cached body. Default to
        // text/html (the overwhelming majority of fetch_url usage); documented
        // in ExecuteAsync as a known limitation.
        var handle = new FetchUrlHandle(
            url,
            200,
            "text/html",
            size,
            relPath,
            truncated);

        return Task.FromResult<string?>(JsonSerializer.Serialize(handle, HandleJsonOptions));
    }

    private static string ComputeUrlHash(string url)
    {
        var bytes = Encoding.UTF8.GetBytes(url);
        var hash  = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FileTailContainsTruncationMarker(string path, long fileSize)
    {
        // The marker written by the cache-miss path is
        //   "\n\n[Response truncated at <N> bytes]"
        // where <N> fits in a signed int. A 64-byte tail comfortably covers it.
        const int tailBytes = 64;
        var readLength = (int)Math.Min(tailBytes, fileSize);
        if (readLength <= 0)
            return false;

        using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        fs.Seek(fileSize - readLength, SeekOrigin.Begin);
        var buffer = new byte[readLength];
        var read = 0;
        while (read < readLength)
        {
            var n = fs.Read(buffer, read, readLength - read);
            if (n == 0) break;
            read += n;
        }
        var tail = Encoding.UTF8.GetString(buffer, 0, read);
        return tail.Contains("[Response truncated at ", StringComparison.Ordinal);
    }

    private sealed record FetchUrlHandle(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("content_type")] string ContentType,
        [property: JsonPropertyName("size_bytes")] long SizeBytes,
        [property: JsonPropertyName("saved_to")] string? SavedTo,
        [property: JsonPropertyName("truncated")] bool Truncated);
}
