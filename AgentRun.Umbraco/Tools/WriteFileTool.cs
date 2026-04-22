using System.Text.Json;
using AgentRun.Umbraco.Security;
using Microsoft.Extensions.Logging;

namespace AgentRun.Umbraco.Tools;

public class WriteFileTool : IWorkflowTool
{
    // Aggregate per-file byte cap in append mode. Overwrite mode is naturally
    // bounded by each call's `content` size; append mode has no such bound
    // because the agent can call append repeatedly on the same file across
    // many tool cycles. Without a cap, a runaway LLM tool-loop or a very
    // large content-audit run can produce multi-MB artifacts that the next
    // read_file call then has to ingest. 10 MB is generous for realistic
    // streaming-append use cases (per-node scan-results for 500+ nodes).
    public const long MaxAppendFileBytes = 10L * 1024 * 1024;

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "Relative path to the file within the instance folder" },
                "content": { "type": "string", "description": "The content to write to the file" },
                "append": { "type": "boolean", "description": "If true, append content to the existing file (creating it if absent). If false or omitted, overwrite the file atomically. Use append for streaming per-item outputs (e.g. one section per tool-call cycle) so each write happens while the source data is fresh in context." }
            },
            "required": ["path", "content"]
        }
        """).RootElement;

    private readonly ILogger<WriteFileTool>? _logger;

    public WriteFileTool()
    {
        _logger = null;
    }

    public WriteFileTool(ILogger<WriteFileTool> logger)
    {
        _logger = logger;
    }

    public string Name => "write_file";

    public string Description =>
        "Writes content to a file within the instance folder. " +
        "Overwrites atomically by default; set append=true to append to the existing file " +
        "(useful for streaming per-item outputs across many tool-call cycles).";

    public JsonElement? ParameterSchema => Schema;

    public async Task<object> ExecuteAsync(
        IDictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var path = ExtractStringArgument(arguments, "path");
        var content = ExtractStringArgument(arguments, "content", allowEmpty: true);
        var append = ExtractOptionalBoolArgument(arguments, "append") ?? false;
        var canonicalPath = ValidatePathSandboxed(path, context.InstanceFolderPath);

        Directory.CreateDirectory(Path.GetDirectoryName(canonicalPath)!);

        if (append)
        {
            // Aggregate size cap — guard against runaway append loops or very
            // large streaming writes that would produce unreadable artifacts.
            var existingSize = File.Exists(canonicalPath) ? new FileInfo(canonicalPath).Length : 0L;
            var incomingSize = System.Text.Encoding.UTF8.GetByteCount(content);
            if (existingSize + incomingSize > MaxAppendFileBytes)
            {
                throw new ToolExecutionException(
                    $"Append would grow '{path}' beyond {MaxAppendFileBytes:N0} bytes " +
                    $"(existing: {existingSize:N0}, incoming: {incomingSize:N0}). " +
                    $"Start a new file or increase MaxAppendFileBytes.");
            }

            // Typo guard — append is typically used to stream into a file that
            // was created earlier by an overwrite. An agent typo in `path` at
            // the append stage silently creates a parallel malformed file and
            // the agent never notices. Emit a single Warning when append
            // creates a missing file so operators can spot typos in logs.
            var fileExisted = File.Exists(canonicalPath);

            // TOCTOU re-validation — between ValidatePath (which rejects
            // symlinks) and the File.AppendAllTextAsync open, an adversary
            // with write access to the instance folder could swap the file
            // for a symlink. Re-check symlink posture on the canonical path
            // and every ancestor immediately before open.
            if (PathSandbox.IsPathOrAncestorSymlink(canonicalPath))
            {
                throw new ToolExecutionException(
                    "Access denied: symbolic links are not permitted");
            }

            // Append mode — not atomic; the file may be partially written if
            // cancellation fires mid-write. Acceptable for streaming use cases
            // where the agent writes per-item sections across many cycles and
            // wants each append to happen while the source data is fresh in
            // context. Creates the file if it doesn't exist yet.
            await File.AppendAllTextAsync(canonicalPath, content, cancellationToken);

            if (!fileExisted && _logger is not null)
            {
                _logger.LogWarning(
                    "write_file(append=true) created missing file '{RelativePath}' — verify this is not a path typo",
                    PathSandbox.GetRelativePath(canonicalPath, context.InstanceFolderPath));
            }
        }
        else
        {
            // Overwrite mode — atomic via tmp + rename. A partial write never
            // corrupts the target; the reader always sees either the prior
            // content or the complete new content.
            var tmpPath = canonicalPath + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tmpPath, content, cancellationToken);
                File.Move(tmpPath, canonicalPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); }
                    catch { /* best effort cleanup */ }
                }
            }
        }

        var relativePath = PathSandbox.GetRelativePath(canonicalPath, context.InstanceFolderPath);
        return append
            ? $"File appended: '{relativePath}'"
            : $"File written: '{relativePath}'";
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

    private static string ExtractStringArgument(IDictionary<string, object?> arguments, string name, bool allowEmpty = false)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
            throw new ToolExecutionException($"Missing required argument: '{name}'");

        var stringValue = value switch
        {
            string s => s,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } je => je.GetString()!,
            _ => throw new ToolExecutionException($"Argument '{name}' must be a string")
        };

        if (!allowEmpty && string.IsNullOrWhiteSpace(stringValue))
            throw new ToolExecutionException($"Missing required argument: '{name}'");

        return stringValue;
    }

    private static bool? ExtractOptionalBoolArgument(IDictionary<string, object?> arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
            return null;

        return value switch
        {
            bool b => b,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            _ => throw new ToolExecutionException($"Argument '{name}' must be a boolean")
        };
    }
}
