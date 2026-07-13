using Microsoft.Extensions.Options;

namespace Storage.Vector;

public class LocalFileStorageProvider : IStorageProvider
{
    // Mirrors AzureBlobStorageProvider.MaxPresignedUrlExpiry — caps how long a presigned
    // URL can grant unauthenticated access, regardless of what a caller requests.
    public static readonly TimeSpan MaxPresignedUrlExpiry = TimeSpan.FromHours(1);

    private readonly StorageOptions _options;

    public LocalFileStorageProvider(IOptions<StorageOptions> options)
    {
        _options = options.Value;
    }

    public async Task<string> PutObjectAsync(string container, string key, Stream data, string contentType, CancellationToken ct)
    {
        var path = ResolvePath(container, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            await using var dest = File.Create(path);
            await data.CopyToAsync(dest, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException(StorageErrorKind.Unavailable, $"Could not write object '{container}/{key}'.", ex);
        }

        var info = new FileInfo(path);
        return $"{info.LastWriteTimeUtc.Ticks:x}-{info.Length:x}";
    }

    public Task<Uri> GetPresignedUrlAsync(string container, string key, TimeSpan expiry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var path = ResolvePath(container, key);
        if (!File.Exists(path))
        {
            throw new StorageException(StorageErrorKind.NotFound, $"No object at '{container}/{key}'.");
        }

        var cappedExpiry = expiry > MaxPresignedUrlExpiry ? MaxPresignedUrlExpiry : expiry;
        var expiresAt = DateTimeOffset.UtcNow.Add(cappedExpiry).ToUnixTimeSeconds();
        var signature = LocalFileUrlSigner.Compute(_options.SigningKey!, container, key, expiresAt);

        var url = $"{_options.PublicBaseUrl!.TrimEnd('/')}/api/v1/media/local-file/{container}/{key}?expires={expiresAt}&sig={signature}";
        return Task.FromResult(new Uri(url));
    }

    public Task<Stream> GetObjectAsync(string container, string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var path = ResolvePath(container, key);
        if (!File.Exists(path))
        {
            throw new StorageException(StorageErrorKind.NotFound, $"No object at '{container}/{key}'.");
        }

        try
        {
            Stream stream = File.OpenRead(path);
            return Task.FromResult(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException(StorageErrorKind.Unavailable, $"Could not read object '{container}/{key}'.", ex);
        }
    }

    public Task DeleteObjectAsync(string container, string key, CancellationToken ct)
    {
        var path = ResolvePath(container, key);

        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // Container directory never existed — nothing to delete, matching Azure's
            // DeleteIfExistsAsync idempotent-no-op-if-missing semantics.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException(StorageErrorKind.Unavailable, $"Could not delete object '{container}/{key}'.", ex);
        }

        return Task.CompletedTask;
    }

    public Task EnsureContainerExistsAsync(string container, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(_options.RootPath!, container));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException(StorageErrorKind.Unavailable, $"Could not create container '{container}'.", ex);
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(string container, string key) =>
        LocalFilePathResolver.ResolveContained(_options.RootPath!, container, key);
}
