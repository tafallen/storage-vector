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
public class AwsS3StorageProvider : IStorageProvider
{
    /// <summary>
    /// Caps how long a presigned URL can grant unauthenticated access to an S3 object (currently 1 hour).
    /// </summary>
    public static readonly TimeSpan MaxPresignedUrlExpiry = TimeSpan.FromHours(1);

    private readonly IAmazonS3 _s3;
    private readonly StorageOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsS3StorageProvider" /> class.
    /// </summary>
    /// <param name="s3">The Amazon S3 client.</param>
    /// <param name="options">The storage configuration options.</param>
    public AwsS3StorageProvider(IAmazonS3 s3, IOptions<StorageOptions> options)
    {
        _s3 = s3;
        _options = options.Value;
    }

    // The configured Container property is the S3 bucket name.
    private string BucketName => _options.Container;

    // Object key in single-bucket mode: container acts as a key prefix.
    private static string ObjectKey(string container, string key) => $"{container}/{key}";

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
        // AWS SigV4 presigned URLs embed expiry as two query parameters:
        //   X-Amz-Date    — the signing moment  (format: yyyyMMddTHHmmssZ)
        //   X-Amz-Expires — validity window in seconds from that moment
        try
        {
            var uri = new Uri(url);
            var query = uri.Query.TrimStart('?');

            string? amzDate = null;
            string? amzExpires = null;

            foreach (var segment in query.Split('&'))
            {
                var eq = segment.IndexOf('=');
                if (eq <= 0) continue;

                var k = segment.Substring(0, eq);
                var v = Uri.UnescapeDataString(segment.Substring(eq + 1));

                if (k.Equals("X-Amz-Date", StringComparison.OrdinalIgnoreCase))
                    amzDate = v;
                else if (k.Equals("X-Amz-Expires", StringComparison.OrdinalIgnoreCase))
                    amzExpires = v;
            }

            if (amzDate is null || amzExpires is null) return false;

            if (!DateTimeOffset.TryParseExact(
                    amzDate,
                    "yyyyMMddTHHmmssZ",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var issuedAt))
                return false;

            if (!long.TryParse(amzExpires, out var expiresInSeconds)) return false;

            return issuedAt.AddSeconds(expiresInSeconds) > DateTimeOffset.UtcNow;
        }
        catch
        {
            return false;
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
}
