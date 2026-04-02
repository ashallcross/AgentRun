using System.Runtime.InteropServices;

namespace AgentRun.Umbraco.Security;

public static class PathSandbox
{
    public static string ValidatePath(string requestedPath, string allowedRoot)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
            throw new ArgumentException("Path must not be null or empty.", nameof(requestedPath));

        var canonicalRoot = Path.GetFullPath(allowedRoot);
        var canonicalPath = Path.GetFullPath(Path.Combine(canonicalRoot, requestedPath));
        var normalisedRoot = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;

        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        // The canonical path must either equal the root (for directory tools) or start with root + separator
        if (!canonicalPath.StartsWith(normalisedRoot, comparison) &&
            !string.Equals(canonicalPath, canonicalRoot.TrimEnd(Path.DirectorySeparatorChar), comparison))
        {
            throw new UnauthorizedAccessException(
                $"Access denied: path '{requestedPath}' is outside the instance folder");
        }

        // Check for symlinks on the target and every ancestor between root and target
        if (PathOrAncestorIsSymlink(canonicalPath, normalisedRoot))
        {
            throw new UnauthorizedAccessException("Access denied: symbolic links are not permitted");
        }

        return canonicalPath;
    }

    public static string GetRelativePath(string fullPath, string allowedRoot)
    {
        return Path.GetRelativePath(allowedRoot, fullPath);
    }

    public static bool IsPathOrAncestorSymlink(string targetPath)
    {
        return IsSymlink(targetPath);
    }

    private static bool PathOrAncestorIsSymlink(string targetPath, string normalisedRoot)
    {
        // Check the target itself if it exists
        if (IsSymlink(targetPath))
            return true;

        // Walk each directory component between root and target
        var current = Path.GetDirectoryName(targetPath);
        var rootTrimmed = normalisedRoot.TrimEnd(Path.DirectorySeparatorChar);

        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        while (current is not null &&
               !string.Equals(current, rootTrimmed, comparison) &&
               current.StartsWith(rootTrimmed, comparison))
        {
            if (IsSymlink(current))
                return true;

            current = Path.GetDirectoryName(current);
        }

        return false;
    }

    private static bool IsSymlink(string path)
    {
        try
        {
            if (!Path.Exists(path))
                return false;

            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }
}
