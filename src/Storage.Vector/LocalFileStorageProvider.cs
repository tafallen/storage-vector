using Microsoft.Extensions.Options;

namespace Storage.Vector;

/// <summary>
/// A storage provider implementation that uses the local filesystem.
/// </summary>
public class LocalFileStorageProvider : IStorageProvider
{
    /// <summary>
    /// Caps how long a presigned URL can grant unauthenticated access (currently 1 hour).
    /// </summary>
    public static readonly TimeSpan MaxPresignedUrlExpiry = TimeSpan.FromHours(1);

    private readonly StorageOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalFileStorageProvider"/> class.
    /// </summary>
    /// <param name="options">The storage configuration options.</param>
    public LocalFileStorageProvider(IOptions<StorageOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<string> PutObjectAsync(string container, string key, Stream data, string contentType, CancellationToken ct)
    {
        var path = ResolvePath(container, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            // Use FileOptions.Asynchronous and useAsync: true for non-blocking handle creation and writes.
            await using var dest = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: _options.BufferSize,
                useAsync: true);
            await data.CopyToAsync(dest, _options.BufferSize, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException(StorageErrorKind.Unavailable, $"Could not write object '{container}/{key}'.", ex);
        }

        var info = new FileInfo(path);
        return $"{info.LastWriteTimeUtc.Ticks:x}-{info.Length:x}";
    }

    /// <inheritdoc />
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

        var route = _options.LocalFileDownloadRoute.Trim('/');
        var url = $"{_options.PublicBaseUrl!.TrimEnd('/')}/{route}/{container}/{key}?expires={expiresAt}&sig={signature}";
        return Task.FromResult(new Uri(url));
    }

    /// <inheritdoc />
    public Task<Stream> GetObjectAsync(string container, string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var path = ResolvePath(container, key);

        try
        {
            // Attempt to open the FileStream directly with useAsync: true to avoid TOCTOU race conditions.
            Stream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: _options.BufferSize,
                useAsync: true);
            return Task.FromResult(stream);
        }
        catch (FileNotFoundException ex)
        {
            throw new StorageException(StorageErrorKind.NotFound, $"No object at '{container}/{key}'.", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new StorageException(StorageErrorKind.NotFound, $"No container or object at '{container}/{key}'.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException(StorageErrorKind.Unavailable, $"Could not read object '{container}/{key}'.", ex);
        }
    }

    /// <inheritdoc />
    public Task DeleteObjectAsync(string container, string key, CancellationToken ct)
    {
        var path = ResolvePath(container, key);

        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // Container directory never existed — nothing to delete.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException(StorageErrorKind.Unavailable, $"Could not delete object '{container}/{key}'.", ex);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public bool VerifyPresignedUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var query = uri.Query.TrimStart('?').Split('&');
            string? expiresStr = null;
            string? sig = null;

            foreach (var part in query)
            {
                var kv = part.Split('=');
                if (kv.Length == 2)
                {
                    if (kv[0] == "expires") expiresStr = Uri.UnescapeDataString(kv[1]);
                    else if (kv[0] == "sig") sig = Uri.UnescapeDataString(kv[1]);
                }
            }

            if (string.IsNullOrEmpty(expiresStr) || string.IsNullOrEmpty(sig))
            {
                return false;
            }

            if (!long.TryParse(expiresStr, out var expiresAt))
            {
                return false;
            }

            if (DateTimeOffset.FromUnixTimeSeconds(expiresAt) <= DateTimeOffset.UtcNow)
            {
                return false;
            }

            // Extract container and key from the path
            var route = _options.LocalFileDownloadRoute.Trim('/');
            var path = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/');
            
            if (path.StartsWith(route, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(route.Length).Trim('/');
            }

            var firstSlash = path.IndexOf('/');
            if (firstSlash <= 0)
            {
                return false;
            }

            var container = path.Substring(0, firstSlash);
            var key = path.Substring(firstSlash + 1);

            return LocalFileUrlSigner.Verify(_options.SigningKey!, container, key, expiresAt, sig);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string ResolvePath(string container, string key) =>
        LocalFilePathResolver.ResolveContained(_options.RootPath!, container, key);
}
