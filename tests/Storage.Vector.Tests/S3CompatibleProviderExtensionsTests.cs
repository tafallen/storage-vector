using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Storage.Vector.Tests;

public class S3CompatibleProviderExtensionsTests
{
    [Fact]
    public void AddCloudflareR2StorageProvider_RegistersAwsS3StorageProviderWithOptions()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Storage:Container"] = "my-r2-bucket",
            ["Storage:AwsServiceUrl"] = "https://123456789.r2.cloudflarestorage.com",
            ["Storage:AwsAccessKeyId"] = "R2KEY123",
            ["Storage:AwsSecretAccessKey"] = "R2SECRET456",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

        var services = new ServiceCollection();
        services.AddCloudflareR2StorageProvider(config);

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IStorageProvider>();
        var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;

        Assert.IsType<AwsS3StorageProvider>(provider);
        Assert.Equal("my-r2-bucket", options.Container);
        Assert.Equal("auto", options.AwsRegion);
        Assert.False(options.AwsForcePathStyle);
    }

    [Fact]
    public void AddMinIOStorageProvider_RegistersAwsS3StorageProviderWithPathStyle()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Storage:Container"] = "minio-bucket",
            ["Storage:AwsServiceUrl"] = "http://localhost:9000",
            ["Storage:AwsAccessKeyId"] = "minioadmin",
            ["Storage:AwsSecretAccessKey"] = "minioadmin",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

        var services = new ServiceCollection();
        services.AddMinIOStorageProvider(config);

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IStorageProvider>();
        var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;

        Assert.IsType<AwsS3StorageProvider>(provider);
        Assert.Equal("minio-bucket", options.Container);
        Assert.True(options.AwsForcePathStyle);
    }
}
