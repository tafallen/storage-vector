namespace Storage.Vector;

/// <summary>
/// Defines a fluent, scoped context for a storage container.
/// </summary>
public interface IStorageContainer
{
    /// <summary>
    /// Gets the name of the container.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Resolves an object context within this container.
    /// </summary>
    /// <param name="key">The unique file key/name.</param>
    /// <returns>An object context.</returns>
    IStorageObject File(string key);

    /// <summary>
    /// Ensures that the container exists, creating it if it does not.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    Task EnsureExistsAsync(CancellationToken ct = default);
}
