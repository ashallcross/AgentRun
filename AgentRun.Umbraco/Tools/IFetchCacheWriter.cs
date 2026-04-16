using System.Threading;
using System.Threading.Tasks;

namespace AgentRun.Umbraco.Tools;

public interface IFetchCacheWriter
{
    // Writes the body (with optional truncation marker) into the instance's
    // .fetch-cache/ directory and returns the serialized JSON handle string.
    // Throws ToolExecutionException on path-sandbox violation or IO failure.
    Task<string> WriteHandleAsync(
        string instanceFolderPath,
        string url,
        int status,
        string contentType,
        byte[] body,
        int unmarkedLength,
        bool truncated,
        CancellationToken cancellationToken);

    // Probes the instance's .fetch-cache/ for a prior response to this URL and
    // returns a handle indistinguishable from the cache-miss case. Returns null
    // on miss, malformed cache path, or IO failure — callers fall through to
    // the normal HTTP path.
    Task<string?> TryReadHandleAsync(
        string instanceFolderPath,
        string url,
        CancellationToken cancellationToken);

    // Returns the serialized JSON handle for a zero-body response — same wire
    // shape as WriteHandleAsync's return value but with size_bytes=0 and
    // saved_to=null. Callers (FetchUrlTool's empty-body branch) use this to
    // avoid a no-op disk write while keeping the JSON contract pinned to one
    // implementation.
    string BuildEmptyHandle(string url, int status, string contentType);
}
