using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace Storage.Vector.Tests;

public class FluentStorageExtensionsTests
{
    private readonly Mock<IStorageProvider> _mockProvider;

    public FluentStorageExtensionsTests()
    {
        _mockProvider = new Mock<IStorageProvider>();
    }

    [Fact]
    public void Container_CreatesContainerWithCorrectName()
    {
        var container = _mockProvider.Object.Container("test-container");

        Assert.Equal("test-container", container.Name);
    }

    [Fact]
    public void File_CreatesObjectWithCorrectKeyAndContainerName()
    {
        var storageObject = _mockProvider.Object.File("test-container", "test-key.txt");

        Assert.Equal("test-key.txt", storageObject.Key);
        Assert.Equal("test-container", storageObject.Container.Name);
    }

    [Fact]
    public async Task Container_EnsureExistsAsync_DelegatesToProvider()
    {
        var ct = new CancellationToken();
        _mockProvider.Setup(p => p.EnsureContainerExistsAsync("test-container", ct))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var container = _mockProvider.Object.Container("test-container");
        await container.EnsureExistsAsync(ct);

        _mockProvider.Verify();
    }

    [Fact]
    public async Task Object_UploadAsync_DelegatesToProvider()
    {
        using var stream = new MemoryStream();
        var ct = new CancellationToken();
        const string expectedETag = "etag123";

        _mockProvider.Setup(p => p.PutObjectAsync("test-container", "test-key.txt", stream, "text/plain", ct))
            .ReturnsAsync(expectedETag)
            .Verifiable();

        var storageObject = _mockProvider.Object.File("test-container", "test-key.txt");
        var result = await storageObject.UploadAsync(stream, "text/plain", ct);

        Assert.Equal(expectedETag, result);
        _mockProvider.Verify();
    }

    [Fact]
    public async Task Object_DownloadAsync_DelegatesToProvider()
    {
        using var expectedStream = new MemoryStream();
        var ct = new CancellationToken();

        _mockProvider.Setup(p => p.GetObjectAsync("test-container", "test-key.txt", ct))
            .ReturnsAsync(expectedStream)
            .Verifiable();

        var storageObject = _mockProvider.Object.File("test-container", "test-key.txt");
        var result = await storageObject.DownloadAsync(ct);

        Assert.Same(expectedStream, result);
        _mockProvider.Verify();
    }

    [Fact]
    public async Task Object_GetPresignedUrlAsync_DelegatesToProvider()
    {
        var expiry = TimeSpan.FromMinutes(10);
        var expectedUri = new Uri("https://example.com/test-container/test-key.txt");
        var ct = new CancellationToken();

        _mockProvider.Setup(p => p.GetPresignedUrlAsync("test-container", "test-key.txt", expiry, ct))
            .ReturnsAsync(expectedUri)
            .Verifiable();

        var storageObject = _mockProvider.Object.File("test-container", "test-key.txt");
        var result = await storageObject.GetPresignedUrlAsync(expiry, ct);

        Assert.Equal(expectedUri, result);
        _mockProvider.Verify();
    }

    [Fact]
    public async Task Object_DeleteAsync_DelegatesToProvider()
    {
        var ct = new CancellationToken();

        _mockProvider.Setup(p => p.DeleteObjectAsync("test-container", "test-key.txt", ct))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var storageObject = _mockProvider.Object.File("test-container", "test-key.txt");
        await storageObject.DeleteAsync(ct);

        _mockProvider.Verify();
    }

    [Fact]
    public void Constructor_ThrowsOnNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new StorageContainer(null!, "container"));
        Assert.Throws<ArgumentNullException>(() => new StorageContainer(_mockProvider.Object, null!));

        Assert.Throws<ArgumentNullException>(() => new StorageObject(null!, "container", "key"));
        Assert.Throws<ArgumentNullException>(() => new StorageObject(_mockProvider.Object, (string)null!, "key"));
        Assert.Throws<ArgumentNullException>(() => new StorageObject(_mockProvider.Object, "container", null!));
    }
}
