using Storage.Vector;
using Microsoft.Extensions.Options;
using Xunit;

namespace Storage.Vector.Tests;

public class LocalFileStorageProviderTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "famtree-local-storage-tests-" + Guid.NewGuid());

    private LocalFileStorageProvider CreateProvider(string signingKey = "test-signing-key", string publicBaseUrl = "http://localhost:8080")
    {
        var options = Options.Create(new StorageOptions
        {
            RootPath = _rootPath,
            SigningKey = signingKey,
            PublicBaseUrl = publicBaseUrl,
        });
        return new LocalFileStorageProvider(options);
    }

    [Fact]
    public async Task PutObjectAsync_WritesFileUnderRootPath()
    {
        var provider = CreateProvider();
        using var data = new MemoryStream(new byte[] { 1, 2, 3 });

        await provider.PutObjectAsync("famtree-media", "events/E001/cert.jpg", data, "image/jpeg", CancellationToken.None);

        var written = File.ReadAllBytes(Path.Combine(_rootPath, "famtree-media", "events/E001/cert.jpg"));
        Assert.Equal(new byte[] { 1, 2, 3 }, written);
    }

    [Fact]
    public async Task GetObjectAsync_RangeDownload_ReturnsCorrectSlice()
    {
        var provider = CreateProvider();
        var payload = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        using var data = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));

        await provider.PutObjectAsync("range-container", "file.txt", data, "text/plain", CancellationToken.None);

        using var rangeStream = await provider.GetObjectAsync("range-container", "file.txt", offset: 10, length: 5, CancellationToken.None);
        using var reader = new StreamReader(rangeStream, System.Text.Encoding.UTF8);
        var slice = await reader.ReadToEndAsync();

        Assert.Equal("ABCDE", slice);
    }

    [Fact]
    public async Task ListObjectsAsync_EnumeratesContainerFiles()
    {
        var provider = CreateProvider();
        using var data1 = new MemoryStream(new byte[] { 1, 2 });
        using var data2 = new MemoryStream(new byte[] { 3, 4, 5 });

        await provider.PutObjectAsync("list-container", "docs/a.txt", data1, "text/plain", CancellationToken.None);
        await provider.PutObjectAsync("list-container", "docs/b.txt", data2, "text/plain", CancellationToken.None);

        var list = new List<StorageObject>();
        await foreach (var item in provider.ListObjectsAsync("list-container", "docs/", CancellationToken.None))
        {
            list.Add(item);
        }

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task PutObjectAsync_ReturnsNonEmptyString()
    {
        var provider = CreateProvider();
        using var data = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await provider.PutObjectAsync("famtree-media", "events/E001/cert.jpg", data, "image/jpeg", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public async Task GetPresignedUrlAsync_ObjectMissing_ThrowsStorageExceptionWithNotFoundKind()
    {
        var provider = CreateProvider();

        var ex = await Assert.ThrowsAsync<StorageException>(
            () => provider.GetPresignedUrlAsync("famtree-media", "events/E001/cert.jpg", TimeSpan.FromMinutes(15), CancellationToken.None));

        Assert.Equal(StorageErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_ObjectExists_ReturnsUrlWithValidSignature()
    {
        var provider = CreateProvider(signingKey: "shared-key");
        using var data = new MemoryStream(new byte[] { 1, 2, 3 });
        await provider.PutObjectAsync("famtree-media", "events/E001/cert.jpg", data, "image/jpeg", CancellationToken.None);

        var url = await provider.GetPresignedUrlAsync("famtree-media", "events/E001/cert.jpg", TimeSpan.FromMinutes(15), CancellationToken.None);

        Assert.StartsWith("http://localhost:8080/api/v1/media/local-file/famtree-media/events/E001/cert.jpg?expires=", url.ToString());
        var queryParams = url.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('='))
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));
        var expires = long.Parse(queryParams["expires"]!);
        Assert.True(LocalFileUrlSigner.Verify(System.Text.Encoding.UTF8.GetBytes("shared-key"), "famtree-media", "events/E001/cert.jpg", expires, queryParams["sig"]!));
    }

    [Fact]
    public async Task GetPresignedUrlAsync_ExpiryExceedsMax_ClampsToMaxPresignedUrlExpiry()
    {
        var provider = CreateProvider(signingKey: "shared-key");
        using var data = new MemoryStream(new byte[] { 1, 2, 3 });
        await provider.PutObjectAsync("famtree-media", "events/E001/cert.jpg", data, "image/jpeg", CancellationToken.None);
        var before = DateTimeOffset.UtcNow;

        var url = await provider.GetPresignedUrlAsync("famtree-media", "events/E001/cert.jpg", TimeSpan.FromDays(7), CancellationToken.None);

        var queryParams = url.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('='))
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));
        var expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(queryParams["expires"]!));
        Assert.True(expires <= before.Add(LocalFileStorageProvider.MaxPresignedUrlExpiry).AddSeconds(5));
    }

    [Fact]
    public async Task DeleteObjectAsync_RemovesFile()
    {
        var provider = CreateProvider();
        using var data = new MemoryStream(new byte[] { 1, 2, 3 });
        await provider.PutObjectAsync("famtree-media", "events/E001/cert.jpg", data, "image/jpeg", CancellationToken.None);

        await provider.DeleteObjectAsync("famtree-media", "events/E001/cert.jpg", CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_rootPath, "famtree-media", "events/E001/cert.jpg")));
    }

    [Fact]
    public async Task DeleteObjectAsync_ObjectAlreadyMissing_DoesNotThrow()
    {
        var provider = CreateProvider();

        await provider.DeleteObjectAsync("famtree-media", "events/E001/cert.jpg", CancellationToken.None);
    }

    [Fact]
    public async Task PutObjectAsync_DeeplyNestedRootPath_WritesFileCorrectly()
    {
        // Simulates a realistic mounted-network-share subdirectory structure
        // (e.g. /mnt/nas/famtree/media), not just a flat temp directory.
        var deepRootPath = Path.Combine(_rootPath, "mnt", "nas-simulated", "famtree", "media-root");
        var options = Options.Create(new StorageOptions
        {
            RootPath = deepRootPath,
            SigningKey = "test-signing-key",
            PublicBaseUrl = "http://localhost:8080",
        });
        var provider = new LocalFileStorageProvider(options);
        using var data = new MemoryStream(new byte[] { 4, 5, 6 });

        await provider.PutObjectAsync("famtree-media", "events/E002/deep.jpg", data, "image/jpeg", CancellationToken.None);

        var written = File.ReadAllBytes(Path.Combine(deepRootPath, "famtree-media", "events/E002/deep.jpg"));
        Assert.Equal(new byte[] { 4, 5, 6 }, written);
    }

    [Fact]
    public async Task GetObjectAsync_ObjectExists_ReturnsExactBytesWritten()
    {
        var provider = CreateProvider();
        var originalBytes = new byte[] { 1, 2, 3, 4, 5 };
        using (var data = new MemoryStream(originalBytes))
        {
            await provider.PutObjectAsync("famtree-media", "events/E001/cert.jpg", data, "image/jpeg", CancellationToken.None);
        }

        await using var stream = await provider.GetObjectAsync("famtree-media", "events/E001/cert.jpg", CancellationToken.None);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        Assert.Equal(originalBytes, ms.ToArray());
    }

    [Fact]
    public async Task GetObjectAsync_ObjectMissing_ThrowsStorageExceptionWithNotFoundKind()
    {
        var provider = CreateProvider();

        var ex = await Assert.ThrowsAsync<StorageException>(
            () => provider.GetObjectAsync("famtree-media", "does-not-exist.jpg", CancellationToken.None));

        Assert.Equal(StorageErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task EnsureContainerExistsAsync_CreatesContainerDirectory()
    {
        var provider = CreateProvider();

        await provider.EnsureContainerExistsAsync("famtree-media", CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(_rootPath, "famtree-media")));
    }

    [Fact]
    public async Task PutObjectAsync_EmptyContainer_ThrowsArgumentException()
    {
        var provider = CreateProvider();
        using var data = new MemoryStream(new byte[] { 1 });

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.PutObjectAsync("", "file.jpg", data, "image/jpeg", CancellationToken.None));
    }

    [Fact]
    public async Task PutObjectAsync_EmptyKey_ThrowsArgumentException()
    {
        var provider = CreateProvider();
        using var data = new MemoryStream(new byte[] { 1 });

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.PutObjectAsync("container", " ", data, "image/jpeg", CancellationToken.None));
    }

    [Fact]
    public async Task VerifyPresignedUrl_ValidUrl_ReturnsTrue()
    {
        var provider = CreateProvider(signingKey: "test-signing-key");
        using var data = new MemoryStream(new byte[] { 1, 2, 3 });
        await provider.PutObjectAsync("famtree-media", "events/E001/cert.jpg", data, "image/jpeg", CancellationToken.None);

        var url = await provider.GetPresignedUrlAsync("famtree-media", "events/E001/cert.jpg", TimeSpan.FromMinutes(15), CancellationToken.None);

        var isValid = provider.VerifyPresignedUrl(url.ToString());

        Assert.True(isValid);
    }

    [Fact]
    public async Task VerifyPresignedUrl_ExpiredUrl_ReturnsFalse()
    {
        var provider = CreateProvider(signingKey: "test-signing-key");
        using var data = new MemoryStream(new byte[] { 1, 2, 3 });
        await provider.PutObjectAsync("famtree-media", "events/E001/cert.jpg", data, "image/jpeg", CancellationToken.None);

        // Compute an expired URL directly (expires 10 seconds ago)
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeSeconds();
        var signature = LocalFileUrlSigner.Compute(System.Text.Encoding.UTF8.GetBytes("test-signing-key"), "famtree-media", "events/E001/cert.jpg", expiresAt);
        var url = $"http://localhost:8080/api/v1/media/local-file/famtree-media/events/E001/cert.jpg?expires={expiresAt}&sig={signature}";

        var isValid = provider.VerifyPresignedUrl(url);

        Assert.False(isValid);
    }

    [Fact]
    public async Task VerifyPresignedUrl_TamperedUrl_ReturnsFalse()
    {
        var provider = CreateProvider(signingKey: "test-signing-key");
        using var data = new MemoryStream(new byte[] { 1, 2, 3 });
        await provider.PutObjectAsync("famtree-media", "events/E001/cert.jpg", data, "image/jpeg", CancellationToken.None);

        var url = await provider.GetPresignedUrlAsync("famtree-media", "events/E001/cert.jpg", TimeSpan.FromMinutes(15), CancellationToken.None);
        var tamperedUrl = url.ToString().Replace("E001", "E002"); // Tamper key

        var isValid = provider.VerifyPresignedUrl(tamperedUrl);

        Assert.False(isValid);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}

