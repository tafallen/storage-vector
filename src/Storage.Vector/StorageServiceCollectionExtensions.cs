using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Storage.Vector;

public static class StorageServiceCollectionExtensions
{
    /// <summary>DI key the secondary IStorageProvider is registered under (see AddSecondaryStorageProvider).</summary>
    public const string SecondaryProviderKey = "secondary";

    public static IServiceCollection AddAzureBlobStorageProvider(this IServiceCollection services, IConfiguration configuration)
    {
        // ValidateOnStart fails the app at boot with a clear error if
        // Storage:ConnectionString is missing or malformed, instead of
        // surfacing as a confusing error on the first real storage operation
        // a user triggers in production.
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

        // Stateless — depends only on a singleton — so a singleton lifetime
        // avoids an unnecessary allocation per request.
        services.AddSingleton<IStorageProvider, AzureBlobStorageProvider>();

        return services;
    }

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

    public static bool UsesLocalFileProvider(IConfiguration configuration) =>
        UsesLocalFileProvider(configuration, StorageOptions.SectionName);

    public static bool UsesLocalFileProvider(IConfiguration configuration, string sectionName) =>
        string.Equals(configuration[$"{sectionName}:Provider"], "LocalFile", StringComparison.OrdinalIgnoreCase);

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
    /// (SecondaryStorageOptions). Callers gate this behind Storage:SyncEnabled -- see
    /// Program.cs -- since most deployments don't run media sync (FAM-NF-11) at all.
    /// </summary>
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

        // Constructor-shape adapter (option (c) from the FAM-NF-11 Task 2 plan): both
        // AzureBlobStorageProvider and LocalFileStorageProvider take IOptions<StorageOptions>
        // specifically, so a SecondaryStorageOptions instance is mapped into a
        // StorageOptions-shaped IOptions<T> at registration time rather than changing either
        // provider's constructor or introducing a shared options interface.
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

    // Option (c) from the FAM-NF-11 Task 2 plan's constructor-shape note: builds a
    // StorageOptions-shaped IOptions<T> from a SecondaryStorageOptions instance so the existing
    // provider constructors (which take IOptions<StorageOptions>) can be reused unmodified.
    // Public (not internal) so FAMTree.Api's Program.cs can reuse it for the secondary health
    // check registration too, rather than duplicating this mapping a second time -- FAMTree.Api
    // and Storage.Vector are separate assemblies after FAM-NF-20, so internal is no longer
    // visible across that boundary.
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
