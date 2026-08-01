using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Storage.Vector.Tests;

public class InMemoryStorageProviderTests
{
    private const string TestContainer = "test-container";
    private const string TestKey = "docs/file.txt";

    [Fact]
    public async Task PutObjectAsync_GetObjectAsync_RoundTrip_Succeeds()
    {
        using var provider = new InMemoryStorageProvider();
        var content = "Hello, Storage.Vector In-Memory!";
        using var data = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var etag = await provider.PutObjectAsync(TestContainer, TestKey, data, "text/plain", CancellationToken.None);

        Assert.NotNull(etag);
        Assert.True(etag.Length > 0);
        Assert.Equal(1, provider.Count);

        using var resultStream = await provider.GetObjectAsync(TestContainer, TestKey, CancellationToken.None);
        using var reader = new StreamReader(resultStream, Encoding.UTF8);
        var readContent = await reader.ReadToEndAsync();

        Assert.Equal(content, readContent);
    }

    [Fact]
    public async Task GetObjectAsync_RangeDownload_Succeeds()
    {
        using var provider = new InMemoryStorageProvider();
        var content = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        using var data = new MemoryStream(Encoding.UTF8.GetBytes(content));

        await provider.PutObjectAsync(TestContainer, TestKey, data, "text/plain", CancellationToken.None);

        // Download range offset 10, length 5 => "ABCDE"
        using var rangeStream = await provider.GetObjectAsync(TestContainer, TestKey, offset: 10, length: 5, CancellationToken.None);
        using var reader = new StreamReader(rangeStream, Encoding.UTF8);
        var slice = await reader.ReadToEndAsync();

        Assert.Equal("ABCDE", slice);
    }

    [Fact]
    public async Task ListObjectsAsync_FiltersByContainerAndPrefix_ReturnsObjects()
    {
        using var provider = new InMemoryStorageProvider();
        using var data1 = new MemoryStream(new byte[] { 1, 2, 3 });
        using var data2 = new MemoryStream(new byte[] { 4, 5 });
        using var data3 = new MemoryStream(new byte[] { 6 });

        await provider.PutObjectAsync(TestContainer, "docs/a.txt", data1, "text/plain", CancellationToken.None);
        await provider.PutObjectAsync(TestContainer, "docs/b.txt", data2, "text/plain", CancellationToken.None);
        await provider.PutObjectAsync(TestContainer, "images/pic.png", data3, "image/png", CancellationToken.None);

        var docs = new List<StorageObject>();
        await foreach (var item in provider.ListObjectsAsync(TestContainer, prefix: "docs/", CancellationToken.None))
        {
            docs.Add(item);
        }

        Assert.Equal(2, docs.Count);
        Assert.Contains(docs, d => d.Key == "docs/a.txt" && d.Size == 3);
        Assert.Contains(docs, d => d.Key == "docs/b.txt" && d.Size == 2);
    }

    [Fact]
    public async Task GetObjectAsync_NotFound_ThrowsStorageException()
    {
        using var provider = new InMemoryStorageProvider();

        var ex = await Assert.ThrowsAsync<StorageException>(() =>
            provider.GetObjectAsync(TestContainer, "non-existent.txt", CancellationToken.None));

        Assert.Equal(StorageErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task DeleteObjectAsync_ExistingAndMissing_IsIdempotent()
    {
        using var provider = new InMemoryStorageProvider();
        using var data = new MemoryStream(new byte[] { 1, 2, 3 });

        await provider.PutObjectAsync(TestContainer, TestKey, data, "application/octet-stream", CancellationToken.None);
        Assert.Equal(1, provider.Count);

        await provider.DeleteObjectAsync(TestContainer, TestKey, CancellationToken.None);
        Assert.Equal(0, provider.Count);

        // Deleting non-existent object should not throw
        await provider.DeleteObjectAsync(TestContainer, TestKey, CancellationToken.None);
    }

    [Fact]
    public async Task EnsureContainerExistsAsync_Succeeds()
    {
        using var provider = new InMemoryStorageProvider();

        await provider.EnsureContainerExistsAsync("new-container", CancellationToken.None);
    }

    [Fact]
    public async Task PresignedUrl_GenerateAndVerify_ValidAndExpired()
    {
        using var provider = new InMemoryStorageProvider();

        var validUrl = await provider.GetPresignedUrlAsync(TestContainer, TestKey, TimeSpan.FromMinutes(10), CancellationToken.None);
        Assert.NotNull(validUrl);

        bool isValid = provider.VerifyPresignedUrl(validUrl.ToString());
        Assert.True(isValid);

        bool isValidAsync = await provider.VerifyPresignedUrlAsync(validUrl.ToString(), CancellationToken.None);
        Assert.True(isValidAsync);

        var expiredUrl = await provider.GetPresignedUrlAsync(TestContainer, TestKey, TimeSpan.FromSeconds(-10), CancellationToken.None);
        bool isExpiredValid = provider.VerifyPresignedUrl(expiredUrl.ToString());
        Assert.False(isExpiredValid);

        // Tampered signature should fail
        var tamperedUrl = validUrl.ToString().Replace("sig=", "sig=tampered");
        Assert.False(provider.VerifyPresignedUrl(tamperedUrl));
    }

    [Fact]
    public void AddInMemoryStorageProvider_RegistersProviderInDI()
    {
        var services = new ServiceCollection();
        services.AddInMemoryStorageProvider(options =>
        {
            options.Container = "my-test-bucket";
        });

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IStorageProvider>();

        Assert.IsType<InMemoryStorageProvider>(provider);
    }

    [Fact]
    public async Task AddStorageProvider_ConfiguredAsInMemory_RegistersCorrectly()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "InMemory",
            ["Storage:Container"] = "mem-bucket"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

        var services = new ServiceCollection();
        services.AddStorageProvider(config);

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IStorageProvider>();

        Assert.IsType<InMemoryStorageProvider>(provider);

        using var data = new MemoryStream(Encoding.UTF8.GetBytes("test payload"));
        var etag = await provider.PutObjectAsync("mem-bucket", "item.txt", data, "text/plain", CancellationToken.None);
        Assert.NotNull(etag);
    }

    [Fact]
    public async Task Clear_RemovesAllEntries()
    {
        using var provider = new InMemoryStorageProvider();
        using var data = new MemoryStream(new byte[] { 1, 2 });

        await provider.PutObjectAsync(TestContainer, "1.txt", data, "text/plain", CancellationToken.None);
        Assert.Equal(1, provider.Count);

        provider.Clear();
        Assert.Equal(0, provider.Count);
    }
}
