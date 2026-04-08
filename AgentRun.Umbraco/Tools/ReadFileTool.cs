using System.Text;
using System.Text.Json;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Security;

namespace AgentRun.Umbraco.Tools;

public class ReadFileTool : IWorkflowTool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "Relative path to the file within the instance folder" }
            },
            "required": ["path"]
        }
        """).RootElement;

    private readonly IToolLimitResolver _limitResolver;

    public string Name => "read_file";

    public string Description => "Reads the contents of a file within the instance folder";

    public JsonElement? ParameterSchema => Schema;

    public ReadFileTool(IToolLimitResolver limitResolver)
    {
        _limitResolver = limitResolver;
    }

    public async Task<object> ExecuteAsync(
        IDictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var path = ExtractStringArgument(arguments, "path");
        var canonicalPath = ValidatePathSandboxed(path, context.InstanceFolderPath);

        if (!File.Exists(canonicalPath))
        {
            var relativePath = PathSandbox.GetRelativePath(canonicalPath, context.InstanceFolderPath);
            throw new ToolExecutionException($"File not found: '{relativePath}'");
        }

        // Story 9.9: tool tuning values must come through the resolver chain.
        // Missing Step/Workflow on the execution context is an engine wiring bug.
        // Throw a typed AgentRunException subtype so LlmErrorClassifier does not
        // silently rewrite it as a generic provider failure.
        if (context.Step is null || context.Workflow is null)
        {
            throw new ToolContextMissingException(
                "ReadFileTool requires ToolExecutionContext.Step and .Workflow to be set by the executor. " +
                "This is an engine wiring bug, not a workflow configuration issue.");
        }

        var limit = _limitResolver.ResolveReadFileMaxResponseBytes(context.Step, context.Workflow);

        // Story 9.9 (post-D1 review): single bounded-read code path. Allocate
        // `byte[limit]` once and read on a single FileStream handle. There is no
        // separate FileInfo.Length stat — the only "file size" we observe comes
        // from the same handle we read on, eliminating the TOCTOU window where a
        // small file could grow past the limit between stat and read and be
        // returned via an unbounded File.ReadAllTextAsync.
        var buffer = new byte[limit];
        int read = 0;
        long totalBytes;
        bool truncated;
        try
        {
            await using var stream = File.OpenRead(canonicalPath);
            totalBytes = stream.Length;

            int bytesRead;
            while (read < limit &&
                   (bytesRead = await stream.ReadAsync(buffer.AsMemory(read, limit - read), cancellationToken)) > 0)
            {
                read += bytesRead;
            }

            // Detect truncation independently of the stat: if we filled the
            // buffer AND the stream still has bytes, the file is over the limit.
            // Peek a single byte into a scratch buffer; this is robust against
            // any racing growth that occurred during the read loop.
            truncated = false;
            if (read == limit)
            {
                var scratch = new byte[1];
                var peek = await stream.ReadAsync(scratch.AsMemory(0, 1), cancellationToken);
                if (peek > 0)
                {
                    truncated = true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Edge Case #2 / #4: surface as ToolExecutionException — never
            // return a partial truncated string on a mid-read failure.
            throw new ToolExecutionException($"Failed to read file: {ex.Message}");
        }

        // Decode bytes actually read. UTF-8 codepoint split at the truncation
        // boundary becomes a U+FFFD replacement character (documented behaviour;
        // marker says "{limit} bytes" so the byte-level boundary is the contract).
        var decoded = Encoding.UTF8.GetString(buffer, 0, read);

        if (!truncated)
        {
            // Either the file was at or under the limit (typical small artifact
            // case) or it shrank between open and read (Edge Case #11). Return
            // only the bytes actually read, no marker.
            return decoded;
        }

        // If the file grew during the read so much that the open-time stream.Length
        // is no longer above the limit, defensively report at least limit + 1 in
        // the marker so the marker text is internally consistent with truncation.
        var reportedTotal = Math.Max(totalBytes, (long)limit + 1);

        var marker =
            $"[Response truncated at {limit} bytes — full file is {reportedTotal} bytes. " +
            $"Use a structured extraction tool (e.g. fetch_url with extract: \"structured\" once Story 9.1b ships) " +
            $"or override read_file.max_response_bytes in your workflow configuration to read the rest.]";

        return decoded + marker;
    }

    private static string ValidatePathSandboxed(string path, string instanceFolderPath)
    {
        try
        {
            return PathSandbox.ValidatePath(path, instanceFolderPath);
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            throw new ToolExecutionException(ex.Message);
        }
    }

    private static string ExtractStringArgument(IDictionary<string, object?> arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
            throw new ToolExecutionException($"Missing required argument: '{name}'");

        var stringValue = value switch
        {
            string s => s,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } je => je.GetString()!,
            _ => throw new ToolExecutionException($"Argument '{name}' must be a string")
        };

        if (string.IsNullOrWhiteSpace(stringValue))
            throw new ToolExecutionException($"Missing required argument: '{name}'");

        return stringValue;
    }
}
