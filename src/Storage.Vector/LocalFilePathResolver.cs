namespace Storage.Vector;

/// <summary>
/// Resolves container and key identifiers to filesystem paths and enforces directory containment.
/// </summary>
public static class LocalFilePathResolver
{
    /// <summary>
    /// Resolves container/key against rootPath and verifies the result is still contained within rootPath.
    /// This prevents path traversal directory breakout attacks.
    /// </summary>
    /// <param name="rootPath">The base directory of the storage system.</param>
    /// <param name="container">The container folder name.</param>
    /// <param name="key">The object key (filename or subpath).</param>
    /// <returns>The fully resolved absolute path, if secure.</returns>
    /// <exception cref="StorageException">Thrown if the path escapes the root path boundary.</exception>
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
