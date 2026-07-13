namespace Storage.Vector;

/// <summary>
/// Configuration options for the secondary (backup/mirror) storage provider.
/// Mirrors StorageOptions's shape exactly (Provider/ConnectionString/Container/
/// PublicBlobEndpoint/RootPath/SigningKey/PublicBaseUrl), minus SyncEnabled -- that flag lives
/// only on the primary options class since it gates whether the secondary is used at all. Bound
/// from "Storage:Secondary:*" (e.g. Storage__Secondary__Provider as an env var, matching this
/// codebase's Storage__Provider naming convention with one extra segment).
///
/// A separate class rather than a second binding of StorageOptions, because .NET's options
/// binding has no built-in "bind this type twice under different sections" primitive without
/// named options, and a dedicated type keeps validation errors clearly attributable to "the
/// secondary config is wrong" vs "the primary config is wrong."
/// </summary>
public class SecondaryStorageOptions
{
    /// <summary>
    /// The default configuration section name for secondary storage options.
    /// </summary>
    public const string SectionName = "Storage:Secondary";

    /// <summary>
    /// The connection string to the secondary Azure Blob Storage account.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The default container or root folder name where secondary files are uploaded.
    /// </summary>
    public string Container { get; set; } = string.Empty;

    /// <summary>
    /// Optional endpoint override to rewrite presigned SAS URLs on the secondary provider.
    /// </summary>
    public string? PublicBlobEndpoint { get; set; }

    /// <summary>
    /// Which storage engine provider to register for the secondary: "AzureBlob" (default) or "LocalFile".
    /// </summary>
    public string Provider { get; set; } = "AzureBlob";

    /// <summary>
    /// Absolute directory path where files are stored under on the local filesystem (used only when Provider is "LocalFile").
    /// </summary>
    public string? RootPath { get; set; }

    /// <summary>
    /// HMAC-SHA256 secret key used to sign and verify presigned download URLs (used only when Provider is "LocalFile").
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    /// Base URL (scheme, host, and port) used to construct local presigned URLs, e.g. "https://localhost:8081" (used only when Provider is "LocalFile").
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// The route path segment used to generate and route local file download requests (used only when Provider is "LocalFile").
    /// Defaults to "api/v1/media/local-file".
    /// </summary>
    public string LocalFileDownloadRoute { get; set; } = "api/v1/media/local-file";
}
