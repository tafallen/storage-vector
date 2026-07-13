using System.Net;
using Azure.Storage.Blobs;
using Storage.Vector;
using Microsoft.Extensions.Options;
using Xunit;

namespace Storage.Vector.Tests;

[Collection("Neo4jIntegration")]
public class AzureBlobStorageIntegrationTests
{
    private const string Container = "famtree-media-integration-test";
    private const string Key = "integration-test/sample.png";

    private static bool IsEnabled => Environment.GetEnvironmentVariable("STORAGE_INTEGRATION") == "true";

    [SkippableFact]
    public async Task PutObjectAsync_GetPresignedUrlAsync_DeleteObjectAsync_RoundTripAgainstAzurite()
    {
        Skip.IfNot(IsEnabled, "Set STORAGE_INTEGRATION=true with `docker compose up -d storage storage-init` running to exercise this test against real Azurite.");

        var connectionString = Environment.GetEnvironmentVariable("Storage__ConnectionString") ?? "UseDevelopmentStorage=true";
        var blobServiceClient = new BlobServiceClient(connectionString);
        var provider = new AzureBlobStorageProvider(blobServiceClient, Options.Create(new StorageOptions()));

        await provider.EnsureContainerExistsAsync(Container, CancellationToken.None);
        try
        {
            var originalBytes = new byte[1024];
            new Random(42).NextBytes(originalBytes);
            using (var uploadStream = new MemoryStream(originalBytes))
            {
                await provider.PutObjectAsync(Container, Key, uploadStream, "image/png", CancellationToken.None);
            }

            var downloadUrl = await provider.GetPresignedUrlAsync(Container, Key, TimeSpan.FromMinutes(5), CancellationToken.None);

            using var httpClient = new HttpClient();
            var downloadedBytes = await httpClient.GetByteArrayAsync(downloadUrl);
            Assert.Equal(originalBytes, downloadedBytes);

            await using (var objectStream = await provider.GetObjectAsync(Container, Key, CancellationToken.None))
            using (var ms = new MemoryStream())
            {
                await objectStream.CopyToAsync(ms);
                Assert.Equal(originalBytes, ms.ToArray());
            }

            await provider.DeleteObjectAsync(Container, Key, CancellationToken.None);

            var postDeleteUrl = await provider.GetPresignedUrlAsync(Container, Key, TimeSpan.FromMinutes(5), CancellationToken.None);
            var postDeleteResponse = await httpClient.GetAsync(postDeleteUrl);
            Assert.Equal(HttpStatusCode.NotFound, postDeleteResponse.StatusCode);
        }
        finally
        {
            await blobServiceClient.GetBlobContainerClient(Container).DeleteIfExistsAsync();
        }
    }
}

