namespace Storage.Vector;

public interface IStorageProvider
{
    Task<string> PutObjectAsync(string container, string key, Stream data, string contentType, CancellationToken ct);

    Task<Uri> GetPresignedUrlAsync(string container, string key, TimeSpan expiry, CancellationToken ct);

    Task<Stream> GetObjectAsync(string container, string key, CancellationToken ct);

    Task DeleteObjectAsync(string container, string key, CancellationToken ct);

    Task EnsureContainerExistsAsync(string container, CancellationToken ct);
}
