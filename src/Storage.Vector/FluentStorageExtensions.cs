namespace Storage.Vector;

/// <summary>
/// Extension methods enabling fluent contextual scoping for storage providers.
/// </summary>
public static class FluentStorageExtensions
{
    /// <summary>
    /// Creates a fluent container context.
    /// </summary>
    /// <param name="provider">The storage provider.</param>
    /// <param name="containerName">The target container/directory name.</param>
    /// <returns>A fluent container context.</returns>
    public static StorageContainer Container(this IStorageProvider provider, string containerName) =>
        new(provider, containerName);

    /// <summary>
    /// Creates a fluent object/file context.
    /// </summary>
    /// <param name="provider">The storage provider.</param>
    /// <param name="containerName">The target container/directory name.</param>
    /// <param name="key">The unique file key/name.</param>
    /// <returns>A fluent object context.</returns>
    public static StorageObject File(this IStorageProvider provider, string containerName, string key) =>
        new(provider, containerName, key);
}
