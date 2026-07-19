using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Storage.Vector;

/// <summary>
/// Defines a fluent, scoped context for an object/file within a container.
/// </summary>
public interface IStorageObject
{
    /// <summary>
    /// Gets the unique file key/name.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Gets the container containing this object.
    /// </summary>
    IStorageContainer Container { get; }

    /// <summary>
    /// Uploads or updates the object.
    /// </summary>
    /// <param name="data">The source data stream.</param>
    /// <param name="contentType">The MIME content type.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A version/hash (e.g. ETag) of the written object.</returns>
    Task<string> UploadAsync(Stream data, string contentType, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a download stream for the object.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A stream containing the object data.</returns>
    Task<Stream> DownloadAsync(CancellationToken ct = default);

    /// <summary>
    /// Generates a short-lived presigned download URL for the object.
    /// </summary>
    /// <param name="expiry">How long the signed URL should remain valid.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A signed URI.</returns>
    Task<Uri> GetPresignedUrlAsync(TimeSpan expiry, CancellationToken ct = default);

    /// <summary>
    /// Deletes the object.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    Task DeleteAsync(CancellationToken ct = default);
}
