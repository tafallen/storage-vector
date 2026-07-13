namespace Storage.Vector;

// Centralizes the container/key -> filesystem-path resolution used by both
// LocalFileStorageProvider (read/write/delete) and the presigned-download endpoint
// (FAMTree.Api/Storage/LocalFileStorageEndpoints.cs), so the containment check lives in
// exactly one place rather than being re-derived per caller (FAM-TD-101).
public static class LocalFilePathResolver
{
    // Resolves container/key against rootPath and verifies the result is still contained
    // within rootPath. A key containing ".." (or an absolute/rooted segment) could otherwise
    // walk Path.Combine's result outside rootPath entirely -- e.g. a client-supplied upload
    // filename of "../../../../etc/passwd" becoming part of the storage key. Path.GetFullPath
    // normalizes ".." segments before the comparison, so this catches the escape regardless of
    // how many "../" segments or what mix of separators the key contains.
    public static string ResolveContained(string rootPath, string container, string key)
    {
        var root = Path.GetFullPath(rootPath);
        var resolved = Path.GetFullPath(Path.Combine(root, container, key));

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootWithSeparator, StringComparison.Ordinal) && resolved != root)
        {
            throw new StorageException(StorageErrorKind.AccessDenied, $"Key '{key}' resolves outside the storage root.");
        }

        return resolved;
    }
}
