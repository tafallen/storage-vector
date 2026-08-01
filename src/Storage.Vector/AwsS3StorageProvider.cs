using System.Globalization;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Storage.Vector;

/// <summary>
/// A storage provider implementation that uses AWS S3 (or any S3-compatible service such as LocalStack or MinIO).
/// </summary>
/// <remarks>
/// <para>
/// A single S3 bucket (configured via <see cref="StorageOptionsBase.Container" />) is used for all operations.
/// The <c>container</c> parameter passed to each method acts as a key-prefix namespace:
/// the S3 object key is <c>{container}/{key}</c>.
/// </para>
/// <para>
/// Presigned URLs are signed locally by the SDK (no network call) using SigV4 and are capped at
/// <see cref="MaxPresignedUrlExpiry" /> (1 hour) to match the behaviour of the other providers.
/// </para>
/// </remarks>
public class AwsS3StorageProvider : IStorageProvider, IDisposable
{
    /// <summary>
    /// Caps how long a presigned URL can grant unauthenticated access to an S3 object (currently 1 hour).
    /// </summary>
    public static readonly TimeSpan MaxPresignedUrlExpiry = TimeSpan.FromHours(1);

    private readonly IAmazonS3 _s3;
    private readonly StorageOptions _options;
    private readonly bool _disposeClient;
    private readonly Uri? _publicEndpointUri;

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsS3StorageProvider" /> class.
    /// </summary>
    /// <param name="s3">The Amazon S3 client.</param>
    /// <param name="options">The storage configuration options.</param>
    public AwsS3StorageProvider(IAmazonS3 s3, IOptions<StorageOptions> options)
        : this(s3, options, disposeClient: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsS3StorageProvider" /> class with client disposal configuration.
    /// </summary>
    /// <param name="s3">The Amazon S3 client.</param>
    /// <param name="options">The storage configuration options.</param>
    /// <param name="disposeClient">Whether to dispose the S3 client when this provider is disposed.</param>
    public AwsS3StorageProvider(IAmazonS3 s3, IOptions<StorageOptions> options, bool disposeClient)
    {
        _s3 = s3;
        _options = options.Value;
        _disposeClient = disposeClient;
        _publicEndpointUri = string.IsNullOrWhiteSpace(_options.PublicBlobEndpoint) ? null : new Uri(_options.PublicBlobEndpoint);
    }

    /// <summary>
    /// Disposes managed resources used by the <see cref="AwsS3StorageProvider" />.
    /// </summary>
    public void Dispose()
    {
        if (_disposeClient)
        {
            _s3.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    // The configured Container property is the S3 bucket name.
    private string BucketName => _options.Container;

    // Object key in single-bucket mode: container acts as a key prefix.
    private static string ObjectKey(string container, string key) => string.Concat(container, "/", key);

    /// <inheritdoc />
    public async Task<string> PutObjectAsync(string container, string key, Stream data, string contentType, CancellationToken ct)
    {
        try
        {
            var request = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = ObjectKey(container, key),
                InputStream = data,
                ContentType = contentType,
                AutoCloseStream = false,
            };

            var response = await _s3.PutObjectAsync(request, ct);
            return response.ETag;
        }
        catch (AmazonS3Exception ex)
        {
            throw StorageException.FromAwsException(ex);
        }
    }

    /// <inheritdoc />
    public Task<Uri> GetPresignedUrlAsync(string container, string key, TimeSpan expiry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var cappedExpiry = expiry > MaxPresignedUrlExpiry ? MaxPresignedUrlExpiry : expiry;

        try
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = BucketName,
                Key = ObjectKey(container, key),
                Expires = DateTime.UtcNow.Add(cappedExpiry),
                Verb = HttpVerb.GET,
                Protocol = Protocol.HTTPS,
            };

            // GetPreSignedURL is a pure local computation — no network call is made.
            var url = _s3.GetPreSignedURL(request);
            var uri = new Uri(url);
            return Task.FromResult(RewriteToPublicEndpoint(uri));
        }
        catch (AmazonS3Exception ex)
        {
            throw StorageException.FromAwsException(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Stream> GetObjectAsync(string container, string key, CancellationToken ct)
    {
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = BucketName,
                Key = ObjectKey(container, key),
            };

            var response = await _s3.GetObjectAsync(request, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex)
        {
            throw StorageException.FromAwsException(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Stream> GetObjectAsync(string container, string key, long offset, long? length = null, CancellationToken ct = default)
    {
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = BucketName,
                Key = ObjectKey(container, key),
            };

            if (length.HasValue)
            {
                request.ByteRange = new Amazon.S3.Model.ByteRange(offset, offset + length.Value - 1);
            }
            else if (offset > 0)
            {
                request.ByteRange = new Amazon.S3.Model.ByteRange(offset, long.MaxValue);
            }

            var response = await _s3.GetObjectAsync(request, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex)
        {
            throw StorageException.FromAwsException(ex);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StorageObject> ListObjectsAsync(string container, string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var s3Prefix = string.IsNullOrEmpty(prefix) ? $"{container}/" : $"{container}/{prefix}";
        var request = new ListObjectsV2Request
        {
            BucketName = BucketName,
            Prefix = s3Prefix,
        };

        var paginator = _s3.Paginators.ListObjectsV2(request);
        var containerPrefixLength = container.Length + 1;

        await foreach (var response in paginator.Responses.WithCancellation(ct))
        {
            if (response.S3Objects == null) continue;

            foreach (var s3Obj in response.S3Objects)
            {
                ct.ThrowIfCancellationRequested();
                var key = s3Obj.Key.Length > containerPrefixLength ? s3Obj.Key.Substring(containerPrefixLength) : s3Obj.Key;
                yield return new StorageObject(this, container, key, s3Obj.Size ?? 0, s3Obj.LastModified);
            }
        }
    }

    /// <inheritdoc />
    public async Task DeleteObjectAsync(string container, string key, CancellationToken ct)
    {
        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = BucketName,
                Key = ObjectKey(container, key),
            };

            await _s3.DeleteObjectAsync(request, ct);
        }
        catch (AmazonS3Exception ex) when ((int)ex.StatusCode == 404)
        {
            // Idempotent — S3 does not consistently return 404 on missing-object deletes,
            // but swallow it explicitly to match the contract and other provider behaviour.
        }
        catch (AmazonS3Exception ex)
        {
            throw StorageException.FromAwsException(ex);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// In single-bucket mode, the method operates on the configured S3 bucket (<see cref="StorageOptionsBase.Container"/>),
    /// creating the bucket if it does not already exist. The <paramref name="container"/> parameter acts as a key prefix for objects.
    /// </remarks>
    public async Task EnsureContainerExistsAsync(string container, CancellationToken ct)
    {
        // In single-bucket mode the `container` arg is a key prefix — not a real S3 bucket.
        // We create the configured S3 bucket if it does not yet exist.
        try
        {
            await _s3.PutBucketAsync(new PutBucketRequest
            {
                BucketName = BucketName,
                UseClientRegion = true,
            }, ct);
        }
        catch (AmazonS3Exception ex) when (
            ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
            // Already exists — operation is idempotent.
        }
        catch (AmazonS3Exception ex)
        {
            throw StorageException.FromAwsException(ex);
        }
    }

    /// <inheritdoc />
    public bool VerifyPresignedUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        // AWS SigV4 presigned URLs embed expiry as two query parameters:
        //   X-Amz-Date    — the signing moment  (format: yyyyMMddTHHmmssZ)
        //   X-Amz-Expires — validity window in seconds from that moment
        try
        {
            var uri = new Uri(url);
            var querySpan = uri.Query.AsSpan();
            if (querySpan.StartsWith("?"))
            {
                querySpan = querySpan.Slice(1);
            }

            ReadOnlySpan<char> amzDate = default;
            ReadOnlySpan<char> amzExpires = default;

            while (!querySpan.IsEmpty)
            {
                int ampIndex = querySpan.IndexOf('&');
                ReadOnlySpan<char> parameter = ampIndex == -1 ? querySpan : querySpan.Slice(0, ampIndex);
                querySpan = ampIndex == -1 ? default : querySpan.Slice(ampIndex + 1);

                int eqIndex = parameter.IndexOf('=');
                if (eqIndex > 0)
                {
                    var keySpan = parameter.Slice(0, eqIndex);
                    var valueSpan = parameter.Slice(eqIndex + 1);

                    if (keySpan.Equals("X-Amz-Date", StringComparison.OrdinalIgnoreCase))
                    {
                        amzDate = valueSpan;
                    }
                    else if (keySpan.Equals("X-Amz-Expires", StringComparison.OrdinalIgnoreCase))
                    {
                        amzExpires = valueSpan;
                    }
                }
            }

            if (amzDate.IsEmpty || amzExpires.IsEmpty)
            {
                return false;
            }

            if (!DateTimeOffset.TryParseExact(
                    amzDate,
                    "yyyyMMddTHHmmssZ",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var issuedAt))
            {
                return false;
            }

            if (!long.TryParse(amzExpires, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresInSeconds))
            {
                return false;
            }

            return issuedAt.AddSeconds(expiresInSeconds) > DateTimeOffset.UtcNow;
        }
        catch
        {
            return false;
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
}
