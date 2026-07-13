namespace Storage.Vector;

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string ConnectionString { get; set; } = string.Empty;

    public string Container { get; set; } = string.Empty;

    // Optional. When set, presigned download URLs have their scheme/host/port
    // rewritten to this public-facing endpoint (path and query — including the
    // SAS signature — are preserved verbatim). Needed only when the storage
    // account's internal endpoint (e.g. a Docker Compose service hostname like
    // `storage`) is not reachable from outside the container network — a real
    // Azure Storage account is already publicly reachable, so this should stay
    // unset in production.
    public string? PublicBlobEndpoint { get; set; }

    /// <summary>Which IStorageProvider implementation to register: "AzureBlob" (default) or "LocalFile".</summary>
    public string Provider { get; set; } = "AzureBlob";

    // The following three are used only when Provider is "LocalFile"; they stay unset
    // (and unvalidated) when the default "AzureBlob" provider is selected.

    /// <summary>Absolute directory objects are stored under, one subdirectory per container.</summary>
    public string? RootPath { get; set; }

    /// <summary>HMAC-SHA256 key used to sign and verify local-file presigned download URLs.</summary>
    public string? SigningKey { get; set; }

    /// <summary>Base URL (scheme+host+port) local-file presigned URLs are built against, e.g. "http://localhost:8080".</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// Opt-in flag (default false) gating whether a secondary IStorageProvider (bound from
    /// "Storage:Secondary:*", see SecondaryStorageOptions) is registered under the DI key
    /// "secondary" and media sync (FAM-NF-11) runs. Lives on the primary options class since it
    /// gates whether the secondary is used at all, alongside Provider.
    /// </summary>
    public bool SyncEnabled { get; set; } = false;
}
