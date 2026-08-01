namespace Storage.Vector;

/// <summary>
/// Defines a unified storage provider interface for object-like storage (local filesystem, Azure Blob, etc.).
/// </summary>
public interface IStorageProvider
{
    /// <summary>
    /// Uploads or updates an object in the specified container.
    /// </summary>
    /// <param name="container">The target container/directory name.</param>
    /// <param name="key">The unique file key/name.</param>
    /// <param name="data">The source data stream.</param>
    /// <param name="contentType">The MIME content type of the object.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A string identifier representing the version/hash (e.g. ETag) of the written object.</returns>
    Task<string> PutObjectAsync(string container, string key, Stream data, string contentType, CancellationToken ct);

    /// <summary>
    /// Generates a short-lived presigned download URL for the object.
    /// </summary>
    /// <param name="container">The target container/directory name.</param>
    /// <param name="key">The unique file key/name.</param>
    /// <param name="expiry">How long the signed URL should remain valid.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A signed URI allowing unauthenticated access for the duration of the expiry.</returns>
    Task<Uri> GetPresignedUrlAsync(string container, string key, TimeSpan expiry, CancellationToken ct);

    /// <summary>
    /// Retrieves a download stream for the specified object.
    /// </summary>
    /// <param name="container">The target container/directory name.</param>
    /// <param name="key">The unique file key/name.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A stream containing the object data. Callers are responsible for disposing of the stream.</returns>
    Task<Stream> GetObjectAsync(string container, string key, CancellationToken ct);

    /// <summary>
    /// Retrieves a partial range download stream for the specified object starting at <paramref name="offset"/>.
    /// </summary>
    /// <param name="container">The target container/directory name.</param>
    /// <param name="key">The unique file key/name.</param>
    /// <param name="offset">The starting byte offset (0-based).</param>
    /// <param name="length">The optional number of bytes to retrieve. If null, reads to the end of the object.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A stream containing the requested byte range. Callers are responsible for disposing of the stream.</returns>
    Task<Stream> GetObjectAsync(string container, string key, long offset, long? length = null, CancellationToken ct = default)
    {
        return GetObjectRangeFallbackAsync(this, container, key, offset, length, ct);

        static async Task<Stream> GetObjectRangeFallbackAsync(IStorageProvider provider, string container, string key, long offset, long? length, CancellationToken ct)
        {
            var stream = await provider.GetObjectAsync(container, key, ct).ConfigureAwait(false);
            if (offset > 0 && stream.CanSeek)
            {
                stream.Seek(offset, SeekOrigin.Begin);
            }
            return stream;
        }
    }

    /// <summary>
    /// Lists objects in the specified container with streaming pagination.
    /// </summary>
    /// <param name="container">The target container/directory name.</param>
    /// <param name="prefix">An optional key prefix filter.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>An async enumerable sequence of <see cref="StorageObject"/> metadata.</returns>
    IAsyncEnumerable<StorageObject> ListObjectsAsync(string container, string? prefix = null, CancellationToken ct = default);

    /// <summary>
    /// Deletes the object. This operation is idempotent and will succeed even if the file is not found.
    /// </summary>
    /// <param name="container">The target container/directory name.</param>
    /// <param name="key">The unique file key/name.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    Task DeleteObjectAsync(string container, string key, CancellationToken ct);

    /// <summary>
    /// Ensures that the specified container (or local directory) exists, creating it if it does not.
    /// </summary>
    /// <param name="container">The target container/directory name.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    Task EnsureContainerExistsAsync(string container, CancellationToken ct);

    /// <summary>
    /// Verifies if a given presigned URL is valid (not expired and has a valid signature).
    /// </summary>
    /// <param name="url">The presigned URL to verify.</param>
    /// <returns>True if the URL is valid, false if expired, tampered with, or invalid.</returns>
    bool VerifyPresignedUrl(string url);

    /// <summary>
    /// Asynchronously verifies if a given presigned URL is valid (not expired and has a valid signature).
    /// Provides a non-blocking alternative for providers requiring remote revocation checks.
    /// </summary>
    /// <param name="url">The presigned URL to verify.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A Task resolving to true if the URL is valid, false if expired, tampered with, or invalid.</returns>
    Task<bool> VerifyPresignedUrlAsync(string url, CancellationToken ct = default) =>
        Task.FromResult(VerifyPresignedUrl(url));
}
