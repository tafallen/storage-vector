using System.Net;
using System.Net.Sockets;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Storage.Vector.Tests;

public class AwsS3StorageProviderTests
{
    // ─── VerifyPresignedUrl ───────────────────────────────────────────────────

    [Fact]
    public void VerifyPresignedUrl_ValidUrl_ReturnsTrue()
    {
        var provider = BuildProvider();

        // Build a URL whose X-Amz-Date is now and X-Amz-Expires is 3600 seconds.
        var amzDate = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("yyyyMMddTHHmmssZ");
        var url = $"https://my-bucket.s3.eu-west-2.amazonaws.com/photos/img.jpg" +
                  $"?X-Amz-Algorithm=AWS4-HMAC-SHA256" +
                  $"&X-Amz-Date={amzDate}" +
                  $"&X-Amz-Expires=3600" +
                  $"&X-Amz-Signature=fakesig";

        Assert.True(provider.VerifyPresignedUrl(url));
    }

    [Fact]
    public void VerifyPresignedUrl_ExpiredUrl_ReturnsFalse()
    {
        var provider = BuildProvider();

        // Issued 2 hours ago, valid for only 1 hour → expired.
        var amzDate = DateTimeOffset.UtcNow.AddHours(-2).ToString("yyyyMMddTHHmmssZ");
        var url = $"https://my-bucket.s3.eu-west-2.amazonaws.com/photos/img.jpg" +
                  $"?X-Amz-Date={amzDate}" +
                  $"&X-Amz-Expires=3600" +
                  $"&X-Amz-Signature=fakesig";

        Assert.False(provider.VerifyPresignedUrl(url));
    }

    [Fact]
    public void VerifyPresignedUrl_MissingDateParam_ReturnsFalse()
    {
        var provider = BuildProvider();
        var url = "https://my-bucket.s3.eu-west-2.amazonaws.com/photos/img.jpg?X-Amz-Expires=3600";
        Assert.False(provider.VerifyPresignedUrl(url));
    }

    [Fact]
    public void VerifyPresignedUrl_MissingExpiresParam_ReturnsFalse()
    {
        var provider = BuildProvider();
        var amzDate = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("yyyyMMddTHHmmssZ");
        var url = $"https://my-bucket.s3.eu-west-2.amazonaws.com/photos/img.jpg?X-Amz-Date={amzDate}";
        Assert.False(provider.VerifyPresignedUrl(url));
    }

    [Fact]
    public void VerifyPresignedUrl_MalformedUrl_ReturnsFalse()
    {
        var provider = BuildProvider();
        Assert.False(provider.VerifyPresignedUrl("not-a-url"));
    }

    [Fact]
    public void VerifyPresignedUrl_EmptyString_ReturnsFalse()
    {
        var provider = BuildProvider();
        Assert.False(provider.VerifyPresignedUrl(string.Empty));
    }

    // ─── StorageException mapping ─────────────────────────────────────────────

    [Fact]
    public void FromAwsException_404_ReturnsNotFound()
    {
        var ex = new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound };
        var result = StorageException.FromAwsException(ex);
        Assert.Equal(StorageErrorKind.NotFound, result.Kind);
    }

    [Fact]
    public void FromAwsException_403_ReturnsAccessDenied()
    {
        var ex = new AmazonS3Exception("Forbidden") { StatusCode = HttpStatusCode.Forbidden };
        var result = StorageException.FromAwsException(ex);
        Assert.Equal(StorageErrorKind.AccessDenied, result.Kind);
    }

    [Fact]
    public void FromAwsException_401_ReturnsAccessDenied()
    {
        var ex = new AmazonS3Exception("Unauthorized") { StatusCode = HttpStatusCode.Unauthorized };
        var result = StorageException.FromAwsException(ex);
        Assert.Equal(StorageErrorKind.AccessDenied, result.Kind);
    }

    [Fact]
    public void FromAwsException_500_ReturnsUnavailable()
    {
        var ex = new AmazonS3Exception("Internal Server Error") { StatusCode = HttpStatusCode.InternalServerError };
        var result = StorageException.FromAwsException(ex);
        Assert.Equal(StorageErrorKind.Unavailable, result.Kind);
    }

    [Fact]
    public void FromAwsException_UnknownStatus_ReturnsUnknown()
    {
        var ex = new AmazonS3Exception("Bad Request") { StatusCode = HttpStatusCode.BadRequest };
        var result = StorageException.FromAwsException(ex);
        Assert.Equal(StorageErrorKind.Unknown, result.Kind);
    }

    // ─── Provider method error propagation (mock IAmazonS3) ─────────────────

    [Fact]
    public async Task PutObjectAsync_WhenS3Throws_ThrowsStorageException()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new AmazonS3Exception("Error") { StatusCode = HttpStatusCode.Forbidden });

        var provider = BuildProvider(s3.Object);

        var ex = await Assert.ThrowsAsync<StorageException>(() =>
            provider.PutObjectAsync("mycontainer", "mykey", Stream.Null, "text/plain", CancellationToken.None));

        Assert.Equal(StorageErrorKind.AccessDenied, ex.Kind);
    }

    [Fact]
    public async Task GetObjectAsync_WhenS3Throws404_ThrowsStorageExceptionNotFound()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound });

        var provider = BuildProvider(s3.Object);

        var ex = await Assert.ThrowsAsync<StorageException>(() =>
            provider.GetObjectAsync("mycontainer", "mykey", CancellationToken.None));

        Assert.Equal(StorageErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task DeleteObjectAsync_WhenS3Throws404_DoesNotThrow()
    {
        // DeleteObjectAsync should be idempotent — 404 must be swallowed.
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound });

        var provider = BuildProvider(s3.Object);

        // Should not throw
        await provider.DeleteObjectAsync("mycontainer", "mykey", CancellationToken.None);
    }

    [Fact]
    public async Task DeleteObjectAsync_WhenS3Throws500_ThrowsStorageException()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new AmazonS3Exception("Server Error") { StatusCode = HttpStatusCode.InternalServerError });

        var provider = BuildProvider(s3.Object);

        var ex = await Assert.ThrowsAsync<StorageException>(() =>
            provider.DeleteObjectAsync("mycontainer", "mykey", CancellationToken.None));

        Assert.Equal(StorageErrorKind.Unavailable, ex.Kind);
    }

    [Fact]
    public async Task EnsureContainerExistsAsync_BucketAlreadyOwnedByYou_DoesNotThrow()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.PutBucketAsync(It.IsAny<PutBucketRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new AmazonS3Exception("BucketAlreadyOwnedByYou") { ErrorCode = "BucketAlreadyOwnedByYou" });

        var provider = BuildProvider(s3.Object);

        // Should not throw — already-exists errors are idempotent.
        await provider.EnsureContainerExistsAsync("mycontainer", CancellationToken.None);
    }

    // ─── Object key namespacing ───────────────────────────────────────────────

    [Fact]
    public async Task PutObjectAsync_UsesContainerAsKeyPrefix()
    {
        PutObjectRequest? captured = null;
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
          .Callback<PutObjectRequest, CancellationToken>((r, _) => captured = r)
          .ReturnsAsync(new PutObjectResponse { ETag = "\"abc123\"" });

        var provider = BuildProvider(s3.Object);
        await provider.PutObjectAsync("photos", "summer/img.jpg", Stream.Null, "image/jpeg", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("my-test-bucket", captured!.BucketName);
        Assert.Equal("photos/summer/img.jpg", captured.Key);
    }

    [Fact]
    public async Task GetObjectAsync_Success_ReturnsResponseStream()
    {
        var expectedBytes = new byte[] { 10, 20, 30 };
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GetObjectResponse { ResponseStream = new MemoryStream(expectedBytes) });

        var provider = BuildProvider(s3.Object);
        await using var stream = await provider.GetObjectAsync("photos", "sample.png", CancellationToken.None);

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        Assert.Equal(expectedBytes, ms.ToArray());
    }

    [Fact]
    public async Task GetPresignedUrlAsync_PublicBlobEndpointConfigured_RewritesHostAndScheme()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
          .Returns("https://s3.eu-west-2.amazonaws.com/my-test-bucket/photos/img.jpg?X-Amz-Expires=3600");

        var options = Options.Create(new StorageOptions
        {
            Container = "my-test-bucket",
            AwsRegion = "eu-west-2",
            PublicBlobEndpoint = "https://cdn.example.com:8443",
        });

        var provider = new AwsS3StorageProvider(s3.Object, options);
        var url = await provider.GetPresignedUrlAsync("photos", "img.jpg", TimeSpan.FromMinutes(15), CancellationToken.None);

        Assert.Equal("https", url.Scheme);
        Assert.Equal("cdn.example.com", url.Host);
        Assert.Equal(8443, url.Port);
        Assert.Equal("/my-test-bucket/photos/img.jpg", url.AbsolutePath);
    }

    [Fact]
    public async Task EnsureContainerExistsAsync_WhenS3ThrowsGeneralException_ThrowsStorageException()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.PutBucketAsync(It.IsAny<PutBucketRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new AmazonS3Exception("Access Denied") { StatusCode = HttpStatusCode.Forbidden });

        var provider = BuildProvider(s3.Object);
        var ex = await Assert.ThrowsAsync<StorageException>(() =>
            provider.EnsureContainerExistsAsync("photos", CancellationToken.None));

        Assert.Equal(StorageErrorKind.AccessDenied, ex.Kind);
    }

    [Fact]
    public async Task VerifyPresignedUrlAsync_ValidUrl_ReturnsTrue()
    {
        IStorageProvider provider = BuildProvider();
        var amzDate = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("yyyyMMddTHHmmssZ");
        var url = $"https://my-bucket.s3.eu-west-2.amazonaws.com/photos/img.jpg?X-Amz-Date={amzDate}&X-Amz-Expires=3600";

        var result = await provider.VerifyPresignedUrlAsync(url);
        Assert.True(result);
    }

    // ─── LocalStack integration (skipped unless S3_INTEGRATION=true) ─────────

    private const string IntegrationSkipReason =
        "Set S3_INTEGRATION=true with LocalStack running (`docker run -p 4566:4566 localstack/localstack`) to exercise this test.";

    private static bool IsIntegrationEnabled =>
        Environment.GetEnvironmentVariable("S3_INTEGRATION") == "true";

    [SkippableFact]
    public async Task PutObjectAsync_GetPresignedUrlAsync_DeleteObjectAsync_RoundTripAgainstLocalStack()
    {
        Skip.IfNot(IsIntegrationEnabled, IntegrationSkipReason);

        var serviceUrl = Environment.GetEnvironmentVariable("S3_SERVICE_URL") ?? "http://localhost:4566";
        var region = Environment.GetEnvironmentVariable("S3_REGION") ?? "us-east-1";
        const string bucket = "storage-vector-integration-test";
        const string container = "photos";
        const string key = "test/sample.bin";

        var config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = region,
        };
        var credentials = new BasicAWSCredentials("test", "test");
        using var s3Client = new AmazonS3Client(credentials, config);

        var options = Options.Create(new StorageOptions
        {
            Container = bucket,
            AwsRegion = region,
            AwsServiceUrl = serviceUrl,
            AwsForcePathStyle = true,
            AwsAccessKeyId = "test",
            AwsSecretAccessKey = "test",
        });

        var provider = new AwsS3StorageProvider(s3Client, options);

        await provider.EnsureContainerExistsAsync(container, CancellationToken.None);

        try
        {
            var originalBytes = new byte[1024];
            new Random(42).NextBytes(originalBytes);

            using (var uploadStream = new MemoryStream(originalBytes))
            {
                var etag = await provider.PutObjectAsync(container, key, uploadStream, "application/octet-stream", CancellationToken.None);
                Assert.False(string.IsNullOrWhiteSpace(etag));
            }

            await using (var objectStream = await provider.GetObjectAsync(container, key, CancellationToken.None))
            using (var ms = new MemoryStream())
            {
                await objectStream.CopyToAsync(ms);
                Assert.Equal(originalBytes, ms.ToArray());
            }

            await provider.DeleteObjectAsync(container, key, CancellationToken.None);

            // Second delete must not throw (idempotent).
            await provider.DeleteObjectAsync(container, key, CancellationToken.None);
        }
        finally
        {
            // Cleanup: delete all objects in the bucket then the bucket itself.
            var listResp = await s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket });
            foreach (var obj in listResp.S3Objects)
            {
                await s3Client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = obj.Key });
            }

            await s3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucket });
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static AwsS3StorageProvider BuildProvider(IAmazonS3? s3 = null)
    {
        var mock = s3 ?? new Mock<IAmazonS3>().Object;
        var options = Options.Create(new StorageOptions
        {
            Container = "my-test-bucket",
            AwsRegion = "eu-west-2",
        });
        return new AwsS3StorageProvider(mock, options);
    }
}
