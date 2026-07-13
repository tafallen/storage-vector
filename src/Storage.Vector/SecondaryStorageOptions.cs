namespace Storage.Vector;

/// <summary>
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
    public const string SectionName = "Storage:Secondary";

    public string ConnectionString { get; set; } = string.Empty;

    public string Container { get; set; } = string.Empty;

    // See StorageOptions.PublicBlobEndpoint for the full rationale -- same semantics, applied to
    // the secondary provider's own blob endpoint.
    public string? PublicBlobEndpoint { get; set; }

    /// <summary>Which IStorageProvider implementation to register for the secondary: "AzureBlob" (default) or "LocalFile".</summary>
    public string Provider { get; set; } = "AzureBlob";

    // The following three are used only when Provider is "LocalFile"; they stay unset
    // (and unvalidated) when the default "AzureBlob" provider is selected.

    /// <summary>Absolute directory objects are stored under, one subdirectory per container.</summary>
    public string? RootPath { get; set; }

    /// <summary>HMAC-SHA256 key used to sign and verify local-file presigned download URLs.</summary>
    public string? SigningKey { get; set; }

    /// <summary>Base URL (scheme+host+port) local-file presigned URLs are built against, e.g. "http://localhost:8081".</summary>
    public string? PublicBaseUrl { get; set; }
}
