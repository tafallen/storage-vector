using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;

namespace Storage.Vector;

/// <summary>
/// A storage provider implementation that uses Azure Blob Storage.
/// </summary>
public class AzureBlobStorageProvider : IStorageProvider, IDisposable
{
    /// <summary>
    /// Caps how long a presigned SAS URL can grant unauthenticated access to a blob (currently 1 hour).
    /// </summary>
    public static readonly TimeSpan MaxPresignedUrlExpiry = TimeSpan.FromHours(1);

    private readonly BlobServiceClient _service;
    private readonly StorageOptions _options;
    private readonly Uri? _publicEndpointUri;
    private readonly System.Threading.SemaphoreSlim _keySemaphore = new(1, 1);
    private volatile UserDelegationKey? _cachedUserDelegationKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureBlobStorageProvider"/> class.
    /// </summary>
    /// <param name="service">The Azure Blob service client.</param>
    /// <param name="options">The storage configuration options.</param>
    public AzureBlobStorageProvider(BlobServiceClient service, IOptions<StorageOptions> options)
    {
        _service = service;
        _options = options.Value;
        _publicEndpointUri = string.IsNullOrWhiteSpace(_options.PublicBlobEndpoint) ? null : new Uri(_options.PublicBlobEndpoint);
    }

    /// <summary>
    /// Disposes managed resources used by the <see cref="AzureBlobStorageProvider"/>.
    /// </summary>
    public void Dispose()
    {
        _keySemaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<UserDelegationKey> GetUserDelegationKeyCachedAsync(DateTimeOffset now, CancellationToken ct)
    {
        var key = _cachedUserDelegationKey;
        if (key != null && key.SignedExpiresOn > now.AddMinutes(30))
        {
            return key;
        }

        await _keySemaphore.WaitAsync(ct);
        try
        {
            key = _cachedUserDelegationKey;
            if (key != null && key.SignedExpiresOn > now.AddMinutes(30))
            {
                return key;
            }

            var keyExpiresOn = now.AddDays(1);
            var response = await _service.GetUserDelegationKeyAsync(now.AddMinutes(-5), keyExpiresOn, cancellationToken: ct);
            _cachedUserDelegationKey = response.Value;
            return response.Value;
        }
        finally
        {
            _keySemaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string> PutObjectAsync(string container, string key, Stream data, string contentType, CancellationToken ct)
    {
        try
        {
            var blobClient = _service.GetBlobContainerClient(container).GetBlobClient(key);
            var response = await blobClient.UploadAsync(data, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);
            return response.Value.ETag.ToString();
        }
        catch (RequestFailedException ex)
        {
            throw StorageException.FromAzureException(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Uri> GetPresignedUrlAsync(string container, string key, TimeSpan expiry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var cappedExpiry = expiry > MaxPresignedUrlExpiry ? MaxPresignedUrlExpiry : expiry;

        try
        {
            var blobClient = _service.GetBlobContainerClient(container).GetBlobClient(key);
            Uri uri;

            if (blobClient.CanGenerateSasUri)
            {
                // Simple shared-key signature
                uri = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(cappedExpiry));
            }
            else
            {
                // Entra ID TokenCredential flow: generate a User Delegation SAS using sliding cache
                var now = DateTimeOffset.UtcNow;
                var startsOn = now.AddMinutes(-5); // Buffer for clock skew
                var expiresOn = now.Add(cappedExpiry);

                var userDelegationKey = await GetUserDelegationKeyCachedAsync(now, ct);

                var sasBuilder = new BlobSasBuilder(BlobSasPermissions.Read, expiresOn)
                {
                    BlobContainerName = container,
                    BlobName = key,
                    Resource = "b",
                    StartsOn = startsOn
                };

                var blobUriBuilder = new BlobUriBuilder(blobClient.Uri)
                {
                    Sas = sasBuilder.ToSasQueryParameters(userDelegationKey, _service.AccountName)
                };

                uri = blobUriBuilder.ToUri();
            }

            return RewriteToPublicEndpoint(uri);
        }
        catch (RequestFailedException ex)
        {
            throw StorageException.FromAzureException(ex);
        }
    }

    private Uri RewriteToPublicEndpoint(Uri uri)
    {
        if (_publicEndpointUri == null)
        {
            return uri;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = _publicEndpointUri.Scheme,
            Host = _publicEndpointUri.Host,
            Port = _publicEndpointUri.Port,
        };

        return builder.Uri;
    }

    /// <inheritdoc />
    public async Task<Stream> GetObjectAsync(string container, string key, CancellationToken ct)
    {
        try
        {
            var blobClient = _service.GetBlobContainerClient(container).GetBlobClient(key);
            var response = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
            return response.Value.Content;
        }
        catch (RequestFailedException ex)
        {
            throw StorageException.FromAzureException(ex);
        }
    }

    /// <inheritdoc />
    public async Task DeleteObjectAsync(string container, string key, CancellationToken ct)
    {
        try
        {
            var blobClient = _service.GetBlobContainerClient(container).GetBlobClient(key);
            await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex)
        {
            throw StorageException.FromAzureException(ex);
        }
    }

    /// <inheritdoc />
    public async Task EnsureContainerExistsAsync(string container, CancellationToken ct)
    {
        try
        {
            var containerClient = _service.GetBlobContainerClient(container);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex)
        {
            throw StorageException.FromAzureException(ex);
        }
    }

    /// <inheritdoc />
    public bool VerifyPresignedUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            var uri = new Uri(url);
            var querySpan = uri.Query.AsSpan();
            if (querySpan.StartsWith("?"))
            {
                querySpan = querySpan.Slice(1);
            }

            ReadOnlySpan<char> sig = default;
            ReadOnlySpan<char> se = default;
            ReadOnlySpan<char> sp = default;
            ReadOnlySpan<char> sv = default;

            while (!querySpan.IsEmpty)
            {
                int ampIndex = querySpan.IndexOf('&');
                ReadOnlySpan<char> parameter = ampIndex == -1 ? querySpan : querySpan.Slice(0, ampIndex);
                querySpan = ampIndex == -1 ? default : querySpan.Slice(ampIndex + 1);

                int eqIndex = parameter.IndexOf('=');
                if (eqIndex != -1)
                {
                    var keySpan = parameter.Slice(0, eqIndex);
                    var valueSpan = parameter.Slice(eqIndex + 1);

                    if (keySpan.Equals("sig", StringComparison.Ordinal))
                    {
                        sig = valueSpan;
                    }
                    else if (keySpan.Equals("se", StringComparison.Ordinal))
                    {
                        se = valueSpan;
                    }
                    else if (keySpan.Equals("sp", StringComparison.Ordinal))
                    {
                        sp = valueSpan;
                    }
                    else if (keySpan.Equals("sv", StringComparison.Ordinal))
                    {
                        sv = valueSpan;
                    }
                }
            }

            if (sig.IsEmpty || se.IsEmpty || sp.IsEmpty || sv.IsEmpty)
            {
                return false;
            }

            var decodedSig = Uri.UnescapeDataString(sig.ToString());
            if (string.IsNullOrWhiteSpace(decodedSig))
            {
                return false;
            }

            // Validate that the signature is valid Base64
            Span<byte> buffer = stackalloc byte[512];
            if (!Convert.TryFromBase64String(decodedSig, buffer, out _))
            {
                try
                {
                    _ = Convert.FromBase64String(decodedSig);
                }
                catch
                {
                    return false;
                }
            }

            var decodedSe = Uri.UnescapeDataString(se.ToString());
            if (!DateTimeOffset.TryParse(decodedSe, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var expiry))
            {
                return false;
            }

            return expiry > DateTimeOffset.UtcNow;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
