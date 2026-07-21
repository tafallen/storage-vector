using System;
using System.IO;

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
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return ResolveContainedFast(root, rootWithSeparator, container, key);
    }

    /// <summary>
    /// Resolves container/key using pre-normalized root paths to avoid expensive root path I/O resolution calls.
    /// </summary>
    public static string ResolveContainedFast(string normalizedRootPath, string rootPathWithSeparator, string container, string key)
    {
        if (string.IsNullOrWhiteSpace(container))
        {
            throw new ArgumentException("Container name cannot be null, empty, or whitespace.", nameof(container));
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key name cannot be null, empty, or whitespace.", nameof(key));
        }

        var resolved = Path.GetFullPath(Path.Combine(normalizedRootPath, container, key));

        if (!resolved.StartsWith(rootPathWithSeparator, StringComparison.Ordinal) && resolved != normalizedRootPath)
        {
            throw new StorageException(StorageErrorKind.AccessDenied, $"Key '{key}' resolves outside the storage root.");
        }

        return resolved;
    }
}
