using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Moq;

namespace Storage.Vector.Tests;

internal static class FakeBlobServiceClient
{
    // Lets WebApplicationFactory<Program>-based tests boot the full host —
    // including StorageBootstrapHostedService, which runs automatically at
    // host startup — without requiring a live Azurite instance.
    public static BlobServiceClient Create()
    {
        var mockService = new Mock<BlobServiceClient>();
        AllowContainerBootstrap(mockService);
        return mockService.Object;
    }

    // Configures GetBlobContainerClient(...).CreateIfNotExistsAsync(...) to succeed
    // on an existing BlobServiceClient mock, independent of whatever that mock's
    // other setups (e.g. GetPropertiesAsync for the health check) already do.
    public static void AllowContainerBootstrap(Mock<BlobServiceClient> mockService)
    {
        var fakeContainerInfo = BlobsModelFactory.BlobContainerInfo(new ETag("\"fake\""), DateTimeOffset.UtcNow);
        var fakeContainerResponse = Mock.Of<Response<BlobContainerInfo>>(r => r.Value == fakeContainerInfo);

        var mockContainer = new Mock<BlobContainerClient>();
        mockContainer
            .Setup(c => c.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobContainerEncryptionScopeOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeContainerResponse);

        mockService.Setup(s => s.GetBlobContainerClient(It.IsAny<string>())).Returns(mockContainer.Object);
    }
}

