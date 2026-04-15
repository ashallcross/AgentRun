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
        ArgumentNullException.ThrowIfNull(body);
        if (unmarkedLength < 0 || unmarkedLength > body.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unmarkedLength),
                unmarkedLength,
                $"unmarkedLength must be in [0, {body.Length}].");
        }

        // Hash-derived filename (SHA-256, never SHA-1 or MD5).
        var relPath = $".fetch-cache/{ComputeUrlHash(url)}.html";
        var metaRelPath = $".fetch-cache/{ComputeUrlHash(url)}.meta.json";

        // Defence-in-depth (architect review 2026-04-08): obtain the canonical
        // absolute path via PathSandbox.ValidatePath and write to *that* path —
        // do NOT separately compute a write target via Path.Combine.
        string validatedPath;
        string validatedMetaPath;
        try
        {
            validatedPath = PathSandbox.ValidatePath(relPath, instanceFolderPath);
            validatedMetaPath = PathSandbox.ValidatePath(metaRelPath, instanceFolderPath);
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            throw new ToolExecutionException(
                $"Failed to cache fetch_url response to {relPath}: {ex.Message}");
        }

        long totalWritten;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(validatedPath)!);

            await using var fs = new FileStream(
                validatedPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);

            await fs.WriteAsync(body.AsMemory(0, unmarkedLength), cancellationToken);
            totalWritten = unmarkedLength;

            if (truncated)
            {
                var marker = Encoding.UTF8.GetBytes($"\n\n[Response truncated at {unmarkedLength} bytes]");
                await fs.WriteAsync(marker, cancellationToken);
                totalWritten += marker.Length;
            }

            // Sidecar preserving the real HTTP status + content-type for
            // cache-hit round-trips (Story 10.7a review patch P4). Written
            // after the body so a crash between the two leaves only the body —
            // TryReadHandleAsync falls back to defaults when the sidecar is
            // absent, matching pre-patch behaviour.
            var meta = new FetchCacheMeta(status, contentType);
            var metaJson = JsonSerializer.SerializeToUtf8Bytes(meta, HandleJsonOptions);
            await File.WriteAllBytesAsync(validatedMetaPath, metaJson, cancellationToken);
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
            totalWritten,
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
    public async Task<string?> TryReadHandleAsync(
        string instanceFolderPath,
        string url,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var relPath = $".fetch-cache/{ComputeUrlHash(url)}.html";
        var metaRelPath = $".fetch-cache/{ComputeUrlHash(url)}.meta.json";

        string validatedPath;
        string validatedMetaPath;
        try
        {
            validatedPath = PathSandbox.ValidatePath(relPath, instanceFolderPath);
            validatedMetaPath = PathSandbox.ValidatePath(metaRelPath, instanceFolderPath);
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex,
                "fetch_url cache lookup skipped: path validation failed for {RelPath}", relPath);
            return null;
        }

        if (!File.Exists(validatedPath))
            return null;

        long size;
        bool truncated;
        int status = 200;
        string contentType = "text/html";
        try
        {
            var info = new FileInfo(validatedPath);
            size = info.Length;
            truncated = await FileTailContainsTruncationMarkerAsync(
                validatedPath, size, cancellationToken);

            // Sidecar may be absent for caches written before patch P4 —
            // fall back to (200, text/html) defaults silently.
            if (File.Exists(validatedMetaPath))
            {
                var metaBytes = await File.ReadAllBytesAsync(validatedMetaPath, cancellationToken);
                var meta = JsonSerializer.Deserialize<FetchCacheMeta>(metaBytes, HandleJsonOptions);
                if (meta is not null)
                {
                    status = meta.Status;
                    contentType = meta.ContentType;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogDebug(ex,
                "fetch_url cache lookup skipped: IO error reading {RelPath}", relPath);
            return null;
        }

        var handle = new FetchUrlHandle(
            url,
            status,
            contentType,
            size,
            relPath,
            truncated);

        return JsonSerializer.Serialize(handle, HandleJsonOptions);
    }

    public string BuildEmptyHandle(string url, int status, string contentType)
    {
        var handle = new FetchUrlHandle(
            url,
            status,
            contentType,
            SizeBytes: 0L,
            SavedTo: null,
            Truncated: false);
        return JsonSerializer.Serialize(handle, HandleJsonOptions);
    }

    private static string ComputeUrlHash(string url)
    {
        var bytes = Encoding.UTF8.GetBytes(url);
        var hash  = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<bool> FileTailContainsTruncationMarkerAsync(
        string path, long fileSize, CancellationToken cancellationToken)
    {
        // The marker written by the cache-miss path is
        //   "\n\n[Response truncated at <N> bytes]"
        // where <N> fits in a signed int. A 64-byte tail comfortably covers it.
        const int tailBytes = 64;
        var readLength = (int)Math.Min(tailBytes, fileSize);
        if (readLength <= 0)
            return false;

        await using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        fs.Seek(fileSize - readLength, SeekOrigin.Begin);
        var buffer = new byte[readLength];
        var read = 0;
        while (read < readLength)
        {
            var n = await fs.ReadAsync(buffer.AsMemory(read, readLength - read), cancellationToken);
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

    // Story 10.7a review patch P4 — persisted sidecar so cache-hit round-trips
    // preserve the real HTTP status and Content-Type. Older caches without a
    // sidecar fall back to (200, text/html) silently.
    private sealed record FetchCacheMeta(
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("content_type")] string ContentType);
}
