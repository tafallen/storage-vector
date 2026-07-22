using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Storage.Vector;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Storage.Vector.Tests;

public class AzureBlobStorageProviderTests
{
    [Fact]
    public async Task PutObjectAsync_CallsUpload_ReturnsETag()
    {
        var fakeInfo = BlobsModelFactory.BlobContentInfo(new ETag("\"abc123\""), DateTimeOffset.UtcNow, null, null, null, null, 0);
        var fakeResponse = Mock.Of<Response<BlobContentInfo>>(r => r.Value == fakeInfo);

        var mockService = new Mock<BlobServiceClient>();
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();

        mockService.Setup(s => s.GetBlobContainerClient("famtree-media")).Returns(mockContainer.Object);
        mockContainer.Setup(c => c.GetBlobClient("events/E001/cert.jpg")).Returns(mockBlob.Object);
        mockBlob
            .Setup(b => b.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobHttpHeaders>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobRequestConditions>(), It.IsAny<IProgress<long>>(), It.IsAny<AccessTier?>(), It.IsAny<StorageTransferOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeResponse);

        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));
        using var data = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await provider.PutObjectAsync("famtree-media", "events/E001/cert.jpg", data, "image/jpeg", CancellationToken.None);

        Assert.Equal("\"abc123\"", result);
        mockBlob.Verify(b => b.UploadAsync(
            It.Is<Stream>(s => s == data),
            It.Is<BlobHttpHeaders>(h => h.ContentType == "image/jpeg"),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<BlobRequestConditions>(),
            It.IsAny<IProgress<long>>(),
            It.IsAny<AccessTier?>(),
            It.IsAny<StorageTransferOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_ReturnsUriFromSdk()
    {
        var mockService = new Mock<BlobServiceClient>();
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();
        mockBlob.Setup(b => b.CanGenerateSasUri).Returns(true);

        mockService.Setup(s => s.GetBlobContainerClient("famtree-media")).Returns(mockContainer.Object);
        mockContainer.Setup(c => c.GetBlobClient("events/E001/cert.jpg")).Returns(mockBlob.Object);
        mockBlob
            .Setup(b => b.GenerateSasUri(It.IsAny<BlobSasPermissions>(), It.IsAny<DateTimeOffset>()))
            .Returns(new Uri("https://storage.local/famtree-media/events/E001/cert.jpg?sig=xyz"));

        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        var result = await provider.GetPresignedUrlAsync("famtree-media", "events/E001/cert.jpg", TimeSpan.FromMinutes(15), CancellationToken.None);

        Assert.Equal(new Uri("https://storage.local/famtree-media/events/E001/cert.jpg?sig=xyz"), result);
        mockBlob.Verify(b => b.GenerateSasUri(BlobSasPermissions.Read, It.IsAny<DateTimeOffset>()), Times.Once);
    }

    [Fact]
    public async Task DeleteObjectAsync_CallsDeleteIfExists()
    {
        var fakeResponse = Mock.Of<Response<bool>>(r => r.Value == true);

        var mockService = new Mock<BlobServiceClient>();
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();

        mockService.Setup(s => s.GetBlobContainerClient("famtree-media")).Returns(mockContainer.Object);
        mockContainer.Setup(c => c.GetBlobClient("events/E001/cert.jpg")).Returns(mockBlob.Object);
        mockBlob
            .Setup(b => b.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeResponse);

        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        await provider.DeleteObjectAsync("famtree-media", "events/E001/cert.jpg", CancellationToken.None);

        mockBlob.Verify(b => b.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task EnsureContainerExistsAsync_CallsCreateIfNotExists()
    {
        var fakeContainerInfo = BlobsModelFactory.BlobContainerInfo(new ETag("\"def456\""), DateTimeOffset.UtcNow);
        var fakeResponse = Mock.Of<Response<BlobContainerInfo>>(r => r.Value == fakeContainerInfo);

        var mockService = new Mock<BlobServiceClient>();
        var mockContainer = new Mock<BlobContainerClient>();

        mockService.Setup(s => s.GetBlobContainerClient("famtree-media")).Returns(mockContainer.Object);
        mockContainer
            .Setup(c => c.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobContainerEncryptionScopeOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeResponse);

        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        await provider.EnsureContainerExistsAsync("famtree-media", CancellationToken.None);

        mockContainer.Verify(c => c.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobContainerEncryptionScopeOptions>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_ExpiryExceedsMax_ClampsToMaxPresignedUrlExpiry()
    {
        var mockService = new Mock<BlobServiceClient>();
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();
        mockBlob.Setup(b => b.CanGenerateSasUri).Returns(true);
        DateTimeOffset capturedExpiry = default;

        mockService.Setup(s => s.GetBlobContainerClient("famtree-media")).Returns(mockContainer.Object);
        mockContainer.Setup(c => c.GetBlobClient("events/E001/cert.jpg")).Returns(mockBlob.Object);
        mockBlob
            .Setup(b => b.GenerateSasUri(It.IsAny<BlobSasPermissions>(), It.IsAny<DateTimeOffset>()))
            .Callback<BlobSasPermissions, DateTimeOffset>((_, expiresOn) => capturedExpiry = expiresOn)
            .Returns(new Uri("https://storage.local/famtree-media/events/E001/cert.jpg?sig=xyz"));

        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));
        var before = DateTimeOffset.UtcNow;

        await provider.GetPresignedUrlAsync("famtree-media", "events/E001/cert.jpg", TimeSpan.FromDays(7), CancellationToken.None);

        Assert.True(capturedExpiry <= before.Add(AzureBlobStorageProvider.MaxPresignedUrlExpiry).AddSeconds(5));
    }

    [Fact]
    public async Task PutObjectAsync_SdkThrowsNotFound_ThrowsStorageExceptionWithNotFoundKind()
    {
        var mockService = new Mock<BlobServiceClient>();
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();

        mockService.Setup(s => s.GetBlobContainerClient("famtree-media")).Returns(mockContainer.Object);
        mockContainer.Setup(c => c.GetBlobClient("events/E001/cert.jpg")).Returns(mockBlob.Object);
        mockBlob
            .Setup(b => b.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobHttpHeaders>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobRequestConditions>(), It.IsAny<IProgress<long>>(), It.IsAny<AccessTier?>(), It.IsAny<StorageTransferOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "container not found"));

        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));
        using var data = new MemoryStream(new byte[] { 1, 2, 3 });

        var ex = await Assert.ThrowsAsync<StorageException>(
            () => provider.PutObjectAsync("famtree-media", "events/E001/cert.jpg", data, "image/jpeg", CancellationToken.None));

        Assert.Equal(StorageErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task DeleteObjectAsync_SdkThrowsServerError_ThrowsStorageExceptionWithUnavailableKind()
    {
        var mockService = new Mock<BlobServiceClient>();
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();

        mockService.Setup(s => s.GetBlobContainerClient("famtree-media")).Returns(mockContainer.Object);
        mockContainer.Setup(c => c.GetBlobClient("events/E001/cert.jpg")).Returns(mockBlob.Object);
        mockBlob
            .Setup(b => b.DeleteIfExistsAsync(It.IsAny<DeleteSnapshotsOption>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(503, "service unavailable"));

        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        var ex = await Assert.ThrowsAsync<StorageException>(
            () => provider.DeleteObjectAsync("famtree-media", "events/E001/cert.jpg", CancellationToken.None));

        Assert.Equal(StorageErrorKind.Unavailable, ex.Kind);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_PublicBlobEndpointConfigured_RewritesSchemeHostAndPortOnly()
    {
        var mockService = new Mock<BlobServiceClient>();
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();
        mockBlob.Setup(b => b.CanGenerateSasUri).Returns(true);

        mockService.Setup(s => s.GetBlobContainerClient("famtree-media")).Returns(mockContainer.Object);
        mockContainer.Setup(c => c.GetBlobClient("events/E001/cert.jpg")).Returns(mockBlob.Object);
        mockBlob
            .Setup(b => b.GenerateSasUri(It.IsAny<BlobSasPermissions>(), It.IsAny<DateTimeOffset>()))
            .Returns(new Uri("http://storage:10000/devstoreaccount1/famtree-media/events/E001/cert.jpg?sig=xyz&se=2026-01-01"));

        var options = Options.Create(new StorageOptions { PublicBlobEndpoint = "http://localhost:10000/devstoreaccount1" });
        var provider = new AzureBlobStorageProvider(mockService.Object, options);

        var result = await provider.GetPresignedUrlAsync("famtree-media", "events/E001/cert.jpg", TimeSpan.FromMinutes(15), CancellationToken.None);

        Assert.Equal("http", result.Scheme);
        Assert.Equal("localhost", result.Host);
        Assert.Equal(10000, result.Port);
        Assert.Equal("/devstoreaccount1/famtree-media/events/E001/cert.jpg", result.AbsolutePath);
        Assert.Equal("?sig=xyz&se=2026-01-01", result.Query);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_PublicBlobEndpointNotConfigured_ReturnsSdkUriUnchanged()
    {
        var mockService = new Mock<BlobServiceClient>();
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();
        mockBlob.Setup(b => b.CanGenerateSasUri).Returns(true);

        var sdkUri = new Uri("http://storage:10000/devstoreaccount1/famtree-media/events/E001/cert.jpg?sig=xyz&se=2026-01-01");

        mockService.Setup(s => s.GetBlobContainerClient("famtree-media")).Returns(mockContainer.Object);
        mockContainer.Setup(c => c.GetBlobClient("events/E001/cert.jpg")).Returns(mockBlob.Object);
        mockBlob
            .Setup(b => b.GenerateSasUri(It.IsAny<BlobSasPermissions>(), It.IsAny<DateTimeOffset>()))
            .Returns(sdkUri);

        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        var result = await provider.GetPresignedUrlAsync("famtree-media", "events/E001/cert.jpg", TimeSpan.FromMinutes(15), CancellationToken.None);

        Assert.Equal(sdkUri, result);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_TokenCredentialAuth_GeneratesUserDelegationSasUri()
    {
        var mockService = new Mock<BlobServiceClient>();
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();
        
        mockService.Setup(s => s.AccountName).Returns("devstoreaccount1");
        mockService.Setup(s => s.GetBlobContainerClient("famtree-media")).Returns(mockContainer.Object);
        mockContainer.Setup(c => c.GetBlobClient("events/E001/cert.jpg")).Returns(mockBlob.Object);
        mockBlob.Setup(b => b.CanGenerateSasUri).Returns(false);
        mockBlob.Setup(b => b.Uri).Returns(new Uri("http://storage:10000/devstoreaccount1/famtree-media/events/E001/cert.jpg"));

        var fakeKey = BlobsModelFactory.UserDelegationKey("signed-oid", "signed-tid", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "signed-service", "signed-version", "dmFsdWU=");
        var fakeResponse = Mock.Of<Response<UserDelegationKey>>(r => r.Value == fakeKey);

        mockService
            .Setup(s => s.GetUserDelegationKeyAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeResponse);

        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        var result = await provider.GetPresignedUrlAsync("famtree-media", "events/E001/cert.jpg", TimeSpan.FromMinutes(15), CancellationToken.None);

        Assert.Contains("sig=", result.Query);
        Assert.Contains("se=", result.Query);
        mockService.Verify(s => s.GetUserDelegationKeyAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetObjectAsync_ObjectExists_ReturnsExactBytesWritten()
    {
        var originalBytes = new byte[] { 1, 2, 3, 4, 5 };
        var fakeResult = BlobsModelFactory.BlobDownloadStreamingResult(content: new MemoryStream(originalBytes));
        var fakeResponse = Mock.Of<Response<BlobDownloadStreamingResult>>(r => r.Value == fakeResult);

        var mockService = new Mock<BlobServiceClient>();
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();

        mockService.Setup(s => s.GetBlobContainerClient("famtree-media")).Returns(mockContainer.Object);
        mockContainer.Setup(c => c.GetBlobClient("events/E001/cert.jpg")).Returns(mockBlob.Object);
        mockBlob
            .Setup(b => b.DownloadStreamingAsync(It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeResponse);

        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        await using var stream = await provider.GetObjectAsync("famtree-media", "events/E001/cert.jpg", CancellationToken.None);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        Assert.Equal(originalBytes, ms.ToArray());
    }

    [Fact]
    public async Task GetObjectAsync_ObjectMissing_ThrowsStorageExceptionWithNotFoundKind()
    {
        var mockService = new Mock<BlobServiceClient>();
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();

        mockService.Setup(s => s.GetBlobContainerClient("famtree-media")).Returns(mockContainer.Object);
        mockContainer.Setup(c => c.GetBlobClient("does-not-exist.jpg")).Returns(mockBlob.Object);
        mockBlob
            .Setup(b => b.DownloadStreamingAsync(It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "blob not found"));

        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        var ex = await Assert.ThrowsAsync<StorageException>(
            () => provider.GetObjectAsync("famtree-media", "does-not-exist.jpg", CancellationToken.None));

        Assert.Equal(StorageErrorKind.NotFound, ex.Kind);
    }

    // ─── VerifyPresignedUrl Tests ──────────────────────────────────────────────

    [Fact]
    public void VerifyPresignedUrl_ValidSasUrl_ReturnsTrue()
    {
        var mockService = new Mock<BlobServiceClient>();
        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        var expiry = DateTimeOffset.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var validSig = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var url = $"https://account.blob.core.windows.net/container/file.txt?sp=r&st=2026-01-01T00:00:00Z&se={Uri.EscapeDataString(expiry)}&sv=2020-08-04&sr=b&sig={Uri.EscapeDataString(validSig)}";

        Assert.True(provider.VerifyPresignedUrl(url));
    }

    [Fact]
    public void VerifyPresignedUrl_ExpiredSasUrl_ReturnsFalse()
    {
        var mockService = new Mock<BlobServiceClient>();
        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        var expiry = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var validSig = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        var url = $"https://account.blob.core.windows.net/container/file.txt?sp=r&se={Uri.EscapeDataString(expiry)}&sv=2020-08-04&sig={Uri.EscapeDataString(validSig)}";

        Assert.False(provider.VerifyPresignedUrl(url));
    }

    [Fact]
    public void VerifyPresignedUrl_MissingSig_ReturnsFalse()
    {
        var mockService = new Mock<BlobServiceClient>();
        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        var expiry = DateTimeOffset.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var url = $"https://account.blob.core.windows.net/container/file.txt?sp=r&se={Uri.EscapeDataString(expiry)}&sv=2020-08-04";

        Assert.False(provider.VerifyPresignedUrl(url));
    }

    [Fact]
    public void VerifyPresignedUrl_InvalidBase64Sig_ReturnsFalse()
    {
        var mockService = new Mock<BlobServiceClient>();
        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        var expiry = DateTimeOffset.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var url = $"https://account.blob.core.windows.net/container/file.txt?sp=r&se={Uri.EscapeDataString(expiry)}&sv=2020-08-04&sig=not-valid-base64!!!";

        Assert.False(provider.VerifyPresignedUrl(url));
    }

    [Fact]
    public void VerifyPresignedUrl_MissingExpiry_ReturnsFalse()
    {
        var mockService = new Mock<BlobServiceClient>();
        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        var validSig = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        var url = $"https://account.blob.core.windows.net/container/file.txt?sp=r&sv=2020-08-04&sig={Uri.EscapeDataString(validSig)}";

        Assert.False(provider.VerifyPresignedUrl(url));
    }

    [Fact]
    public void VerifyPresignedUrl_MissingPermissions_ReturnsFalse()
    {
        var mockService = new Mock<BlobServiceClient>();
        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        var expiry = DateTimeOffset.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var validSig = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        var url = $"https://account.blob.core.windows.net/container/file.txt?se={Uri.EscapeDataString(expiry)}&sv=2020-08-04&sig={Uri.EscapeDataString(validSig)}";

        Assert.False(provider.VerifyPresignedUrl(url));
    }

    [Fact]
    public void VerifyPresignedUrl_MissingServiceVersion_ReturnsFalse()
    {
        var mockService = new Mock<BlobServiceClient>();
        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        var expiry = DateTimeOffset.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var validSig = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        var url = $"https://account.blob.core.windows.net/container/file.txt?sp=r&se={Uri.EscapeDataString(expiry)}&sig={Uri.EscapeDataString(validSig)}";

        Assert.False(provider.VerifyPresignedUrl(url));
    }

    [Fact]
    public void VerifyPresignedUrl_MalformedUrl_ReturnsFalse()
    {
        var mockService = new Mock<BlobServiceClient>();
        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        Assert.False(provider.VerifyPresignedUrl("not-a-valid-url"));
    }

    [Fact]
    public void VerifyPresignedUrl_EmptyString_ReturnsFalse()
    {
        var mockService = new Mock<BlobServiceClient>();
        var provider = new AzureBlobStorageProvider(mockService.Object, Options.Create(new StorageOptions()));

        Assert.False(provider.VerifyPresignedUrl(string.Empty));
    }
}


