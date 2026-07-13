using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Storage.Vector;

/// <summary>
/// Service collection extension methods to register storage providers.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>DI key the secondary IStorageProvider is registered under (see AddSecondaryStorageProvider).</summary>
    public const string SecondaryProviderKey = "secondary";

    /// <summary>
    /// Registers the Azure Blob Storage provider and its configurations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAzureBlobStorageProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(o => IsValidConnectionString(o.ConnectionString), "Storage:ConnectionString is missing or malformed.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Container), "Storage:Container is missing.")
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            return new BlobServiceClient(options.ConnectionString);
        });

        services.AddSingleton<IStorageProvider, AzureBlobStorageProvider>();

        return services;
    }

    /// <summary>
    /// Registers the Local Filesystem storage provider and its configurations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLocalFileStorageProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Container), "Storage:Container is missing.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "Storage:RootPath is required when Storage:Provider is LocalFile.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey), "Storage:SigningKey is required when Storage:Provider is LocalFile.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.PublicBaseUrl), "Storage:PublicBaseUrl is required when Storage:Provider is LocalFile.")
            .ValidateOnStart();

        services.AddSingleton<IStorageProvider, LocalFileStorageProvider>();

        return services;
    }

    /// <summary>
    /// Helper to check if the configured provider under the default "Storage" section is LocalFile.
    /// </summary>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>True if Provider is LocalFile, false otherwise.</returns>
    public static bool UsesLocalFileProvider(IConfiguration configuration) =>
        UsesLocalFileProvider(configuration, StorageOptions.SectionName);

    /// <summary>
    /// Helper to check if the configured provider under a specific section name is LocalFile.
    /// </summary>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="sectionName">The section name containing the Provider key.</param>
    /// <returns>True if Provider is LocalFile, false otherwise.</returns>
    public static bool UsesLocalFileProvider(IConfiguration configuration, string sectionName) =>
        string.Equals(configuration[$"{sectionName}:Provider"], "LocalFile", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Dynamically registers the primary storage provider based on configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddStorageProvider(this IServiceCollection services, IConfiguration configuration)
    {
        if (UsesLocalFileProvider(configuration))
        {
            services.AddLocalFileStorageProvider(configuration);
        }
        else
        {
            services.AddAzureBlobStorageProvider(configuration);
        }

        return services;
    }

    /// <summary>
    /// Registers a second, independently-configured IStorageProvider under the keyed DI
    /// slot "secondary" (see SecondaryProviderKey), bound from "Storage:Secondary:*"
    /// (SecondaryStorageOptions).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSecondaryStorageProvider(this IServiceCollection services, IConfiguration configuration)
    {
        if (UsesLocalFileProvider(configuration, SecondaryStorageOptions.SectionName))
        {
            services.AddLocalFileSecondaryStorageProvider(configuration);
        }
        else
        {
            services.AddAzureBlobSecondaryStorageProvider(configuration);
        }

        return services;
    }

    private static IServiceCollection AddAzureBlobSecondaryStorageProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SecondaryStorageOptions>()
            .Bind(configuration.GetSection(SecondaryStorageOptions.SectionName))
            .Validate(o => IsValidConnectionString(o.ConnectionString), "Storage:Secondary:ConnectionString is missing or malformed.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Container), "Storage:Secondary:Container is missing.")
            .ValidateOnStart();

        services.AddKeyedSingleton<IStorageProvider, AzureBlobStorageProvider>(SecondaryProviderKey, (sp, _) =>
        {
            var secondaryOptions = sp.GetRequiredService<IOptions<SecondaryStorageOptions>>().Value;
            var blobServiceClient = new BlobServiceClient(secondaryOptions.ConnectionString);
            return new AzureBlobStorageProvider(blobServiceClient, ToPrimaryShapedOptions(secondaryOptions));
        });

        return services;
    }

    private static IServiceCollection AddLocalFileSecondaryStorageProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SecondaryStorageOptions>()
            .Bind(configuration.GetSection(SecondaryStorageOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Container), "Storage:Secondary:Container is missing.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "Storage:Secondary:RootPath is required when Storage:Secondary:Provider is LocalFile.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey), "Storage:Secondary:SigningKey is required when Storage:Secondary:Provider is LocalFile.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.PublicBaseUrl), "Storage:Secondary:PublicBaseUrl is required when Storage:Secondary:Provider is LocalFile.")
            .ValidateOnStart();

        services.AddKeyedSingleton<IStorageProvider, LocalFileStorageProvider>(SecondaryProviderKey, (sp, _) =>
        {
            var secondaryOptions = sp.GetRequiredService<IOptions<SecondaryStorageOptions>>().Value;
            return new LocalFileStorageProvider(ToPrimaryShapedOptions(secondaryOptions));
        });

        return services;
    }

    /// <summary>
    /// Builds a StorageOptions-shaped IOptions of StorageOptions from a SecondaryStorageOptions instance.
    /// This allows reusing the existing provider constructors without modifications.
    /// </summary>
    /// <param name="secondary">The secondary storage options instance.</param>
    /// <returns>A mapped IOptions wrapped StorageOptions instance.</returns>
    public static IOptions<StorageOptions> ToPrimaryShapedOptions(SecondaryStorageOptions secondary) =>
        Options.Create(new StorageOptions
        {
            ConnectionString = secondary.ConnectionString,
            Container = secondary.Container,
            PublicBlobEndpoint = secondary.PublicBlobEndpoint,
            Provider = secondary.Provider,
            RootPath = secondary.RootPath,
            SigningKey = secondary.SigningKey,
            PublicBaseUrl = secondary.PublicBaseUrl,
            LocalFileDownloadRoute = secondary.LocalFileDownloadRoute,
            BufferSize = secondary.BufferSize,
        });

    private static bool IsValidConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            _ = new BlobServiceClient(connectionString);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
