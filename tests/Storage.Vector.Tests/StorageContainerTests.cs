using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Storage.Vector.Tests;

public class StorageContainerTests
{
    [Fact]
    public void StorageContainer_ConstructorValidation_ThrowsArgumentNullException()
    {
        using var provider = new InMemoryStorageProvider();

        Assert.Throws<ArgumentNullException>(() => new StorageContainer(null!, "name"));
        Assert.Throws<ArgumentNullException>(() => new StorageContainer(provider, null!));
    }

    [Fact]
    public async Task StorageContainer_FileAndObjectOperations_Succeed()
    {
        using var provider = new InMemoryStorageProvider();
        var container = new StorageContainer(provider, "test-container");
        Assert.Equal("test-container", container.Name);

        await container.EnsureExistsAsync(CancellationToken.None);

        var obj = container.File("docs/test.txt");
        Assert.Equal("docs/test.txt", obj.Key);
        Assert.Equal("test-container", obj.Container.Name);

        // Upload via StorageObject struct helper method
        using var data = new MemoryStream(Encoding.UTF8.GetBytes("hello struct container"));
        var etag = await obj.UploadAsync(data, "text/plain", CancellationToken.None);
        Assert.NotNull(etag);

        // Download via StorageObject struct helper method
        using var downloadedStream = await obj.DownloadAsync(CancellationToken.None);
        using var reader = new StreamReader(downloadedStream, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("hello struct container", content);

        // GetPresignedUrl via StorageObject struct helper method
        var url = await obj.GetPresignedUrlAsync(TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(url);

        // Delete via StorageObject struct helper method
        await obj.DeleteAsync(CancellationToken.None);
        Assert.Equal(0, provider.Count);

        // Explicit interface cast call
        IStorageContainer iconainer = container;
        IStorageObject iobj = iconainer.File("docs/test.txt");
        Assert.Equal("docs/test.txt", iobj.Key);
    }

    [Fact]
    public void StorageObject_ConstructorValidation_ThrowsArgumentNullException()
    {
        using var provider = new InMemoryStorageProvider();
        var container = new StorageContainer(provider, "bucket");

        Assert.Throws<ArgumentNullException>(() => new StorageObject(null!, container, "key"));
        Assert.Throws<ArgumentNullException>(() => new StorageObject(provider, container, null!));
        Assert.Throws<ArgumentNullException>(() => new StorageObject(null!, "bucket", "key"));
    }
}
