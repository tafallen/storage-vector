namespace Storage.Vector;

/// <summary>
/// Configuration options for the primary storage provider.
/// </summary>
public class StorageOptions
{
    /// <summary>
    /// The default configuration section name for primary storage options.
    /// </summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// The connection string to the Azure Blob Storage account or local Azurite emulator.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The default container or root folder name where files are uploaded.
    /// </summary>
    public string Container { get; set; } = string.Empty;

    /// <summary>
    /// Optional endpoint override (e.g. a CDN or local proxy) to rewrite presigned SAS URLs.
    /// Preserves the path and query string parameters (including SAS tokens) untouched.
    /// </summary>
    public string? PublicBlobEndpoint { get; set; }

    /// <summary>
    /// Which storage engine provider to register: "AzureBlob" (default) or "LocalFile".
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
    /// Base URL (scheme, host, and port) used to construct local presigned URLs, e.g. "https://localhost:5001" (used only when Provider is "LocalFile").
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// The route path segment used to generate and route local file download requests (used only when Provider is "LocalFile").
    /// Defaults to "api/v1/media/local-file".
    /// </summary>
    public string LocalFileDownloadRoute { get; set; } = "api/v1/media/local-file";

    /// <summary>
    /// The buffer size in bytes used when reading and writing files asynchronously on the local filesystem (used only when Provider is "LocalFile").
    /// Defaults to 65536 bytes (64KB).
    /// </summary>
    public int BufferSize { get; set; } = 65536;

    /// <summary>
    /// Gating flag to control whether a secondary storage provider is registered under the keyed slot "secondary".
    /// </summary>
    public bool SyncEnabled { get; set; } = false;
}
