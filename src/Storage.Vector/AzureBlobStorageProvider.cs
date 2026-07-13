using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;

namespace Storage.Vector;

/// <summary>
/// A storage provider implementation that uses Azure Blob Storage.
/// </summary>
public class AzureBlobStorageProvider : IStorageProvider
{
    /// <summary>
    /// Caps how long a presigned SAS URL can grant unauthenticated access to a blob (currently 1 hour).
    /// </summary>
    public static readonly TimeSpan MaxPresignedUrlExpiry = TimeSpan.FromHours(1);

    private readonly BlobServiceClient _service;
    private readonly StorageOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureBlobStorageProvider"/> class.
    /// </summary>
    /// <param name="service">The Azure Blob service client.</param>
    /// <param name="options">The storage configuration options.</param>
    public AzureBlobStorageProvider(BlobServiceClient service, IOptions<StorageOptions> options)
    {
        _service = service;
        _options = options.Value;
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
    public Task<Uri> GetPresignedUrlAsync(string container, string key, TimeSpan expiry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var cappedExpiry = expiry > MaxPresignedUrlExpiry ? MaxPresignedUrlExpiry : expiry;

        try
        {
            var blobClient = _service.GetBlobContainerClient(container).GetBlobClient(key);
            var uri = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(cappedExpiry));
            return Task.FromResult(RewriteToPublicEndpoint(uri));
        }
        catch (RequestFailedException ex)
        {
            throw StorageException.FromAzureException(ex);
        }
    }

    private Uri RewriteToPublicEndpoint(Uri uri)
    {
        if (string.IsNullOrWhiteSpace(_options.PublicBlobEndpoint))
        {
            return uri;
        }

        var publicEndpoint = new Uri(_options.PublicBlobEndpoint);
        var builder = new UriBuilder(uri)
        {
            Scheme = publicEndpoint.Scheme,
            Host = publicEndpoint.Host,
            Port = publicEndpoint.Port,
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
}
