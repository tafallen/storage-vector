using Storage.Vector;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Storage.Vector.Tests;

public class StorageServiceCollectionExtensionsTests
{
    private static ServiceProvider BuildProvider(IDictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var services = new ServiceCollection();
        services.AddAzureBlobStorageProvider(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Container_Missing_ThrowsOptionsValidationException()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Storage:ConnectionString"] = "UseDevelopmentStorage=true",
        });

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<StorageOptions>>().Value);

        Assert.Contains("Storage:Container is missing.", ex.Message);
    }

    [Fact]
    public void Container_Present_DoesNotThrow()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Storage:ConnectionString"] = "UseDevelopmentStorage=true",
            ["Storage:Container"] = "famtree-media",
        });

        var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;

        Assert.Equal("famtree-media", options.Container);
    }

    private static ServiceProvider BuildLocalFileProvider(IDictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var services = new ServiceCollection();
        services.AddLocalFileStorageProvider(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddLocalFileStorageProvider_RootPathMissing_ThrowsOptionsValidationException()
    {
        using var provider = BuildLocalFileProvider(new Dictionary<string, string?>
        {
            ["Storage:Container"] = "famtree-media",
            ["Storage:SigningKey"] = "key",
            ["Storage:PublicBaseUrl"] = "http://localhost:8080",
        });

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<StorageOptions>>().Value);

        Assert.Contains("Storage:RootPath is required when Storage:Provider is LocalFile.", ex.Message);
    }

    [Fact]
    public void AddLocalFileStorageProvider_AllFieldsPresent_DoesNotThrow()
    {
        using var provider = BuildLocalFileProvider(new Dictionary<string, string?>
        {
            ["Storage:Container"] = "famtree-media",
            ["Storage:RootPath"] = "/data/media",
            ["Storage:SigningKey"] = "key",
            ["Storage:PublicBaseUrl"] = "http://localhost:8080",
        });

        var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;

        Assert.Equal("/data/media", options.RootPath);
    }

    // Mirrors Program.cs's own conditional wiring: AddSecondaryStorageProvider is only ever
    // called when Storage:SyncEnabled is true.
    private static ServiceProvider BuildProviderWithOptionalSecondary(IDictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var services = new ServiceCollection();
        services.AddStorageProvider(configuration);
        if (configuration.GetValue<bool>("Storage:SyncEnabled"))
        {
            services.AddSecondaryStorageProvider(configuration);
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddSecondaryStorageProvider_SyncDisabled_DoesNotRegisterSecondaryKey()
    {
        using var provider = BuildProviderWithOptionalSecondary(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "LocalFile",
            ["Storage:Container"] = "famtree-media",
            ["Storage:RootPath"] = "/data/media",
            ["Storage:SigningKey"] = "key",
            ["Storage:PublicBaseUrl"] = "http://localhost:8080",
        });

        Assert.Null(provider.GetKeyedService<IStorageProvider>(StorageServiceCollectionExtensions.SecondaryProviderKey));
    }

    [Fact]
    public void AddSecondaryStorageProvider_SyncEnabledWithLocalFileSecondary_RegistersKeyedLocalFileProvider()
    {
        using var provider = BuildProviderWithOptionalSecondary(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "LocalFile",
            ["Storage:Container"] = "famtree-media",
            ["Storage:RootPath"] = "/data/media",
            ["Storage:SigningKey"] = "key",
            ["Storage:PublicBaseUrl"] = "http://localhost:8080",
            ["Storage:SyncEnabled"] = "true",
            ["Storage:Secondary:Provider"] = "LocalFile",
            ["Storage:Secondary:Container"] = "famtree-media-secondary",
            ["Storage:Secondary:RootPath"] = "/data/media-secondary",
            ["Storage:Secondary:SigningKey"] = "secondary-key",
            ["Storage:Secondary:PublicBaseUrl"] = "http://localhost:8081",
        });

        var primary = provider.GetRequiredService<IStorageProvider>();
        var secondary = provider.GetRequiredKeyedService<IStorageProvider>(StorageServiceCollectionExtensions.SecondaryProviderKey);

        Assert.IsType<LocalFileStorageProvider>(secondary);
        Assert.NotSame(primary, secondary);
    }

    [Fact]
    public void AddStorageProvider_ProviderIsS3_RegistersAwsS3StorageProvider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "S3",
            ["Storage:Container"] = "my-bucket",
            ["Storage:AwsRegion"] = "eu-west-2",
        }).Build();

        var services = new ServiceCollection();
        services.AddStorageProvider(configuration);
        using var provider = services.BuildServiceProvider();

        var storageProvider = provider.GetRequiredService<IStorageProvider>();
        Assert.IsType<AwsS3StorageProvider>(storageProvider);
    }

    [Fact]
    public void AddStorageProvider_UnrecognizedProvider_ThrowsInvalidOperationException()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "UnrecognizedEngine",
        }).Build();

        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddStorageProvider(configuration));
        Assert.Contains("Invalid storage provider 'UnrecognizedEngine'", ex.Message);
    }

    [Fact]
    public void AddStorageProvider_SyncEnabledIsTrue_AutomaticallyRegistersSecondaryKey()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "LocalFile",
            ["Storage:Container"] = "famtree-media",
            ["Storage:RootPath"] = "/data/media",
            ["Storage:SigningKey"] = "key",
            ["Storage:PublicBaseUrl"] = "http://localhost:8080",
            ["Storage:SyncEnabled"] = "true",
            ["Storage:Secondary:Provider"] = "LocalFile",
            ["Storage:Secondary:Container"] = "secondary-media",
            ["Storage:Secondary:RootPath"] = "/data/secondary",
            ["Storage:Secondary:SigningKey"] = "key2",
            ["Storage:Secondary:PublicBaseUrl"] = "http://localhost:8081",
        }).Build();

        var services = new ServiceCollection();
        services.AddStorageProvider(configuration);
        using var provider = services.BuildServiceProvider();

        var secondary = provider.GetKeyedService<IStorageProvider>(StorageServiceCollectionExtensions.SecondaryProviderKey);
        Assert.NotNull(secondary);
        Assert.IsType<LocalFileStorageProvider>(secondary);
    }

    [Fact]
    public void AddSecondaryStorageProvider_ProviderIsS3_RegistersKeyedAwsS3StorageProvider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Secondary:Provider"] = "S3",
            ["Storage:Secondary:Container"] = "secondary-bucket",
            ["Storage:Secondary:AwsRegion"] = "us-east-1",
        }).Build();

        var services = new ServiceCollection();
        services.AddSecondaryStorageProvider(configuration);
        using var provider = services.BuildServiceProvider();

        var secondary = provider.GetRequiredKeyedService<IStorageProvider>(StorageServiceCollectionExtensions.SecondaryProviderKey);
        Assert.IsType<AwsS3StorageProvider>(secondary);
    }

    [Fact]
    public void UsesAwsS3Provider_ReturnsTrueForS3CaseInsensitive()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "s3",
        }).Build();

        Assert.True(StorageServiceCollectionExtensions.UsesAwsS3Provider(config));
    }
}

