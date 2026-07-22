namespace Storage.Vector;

/// <summary>
/// Abstract base class containing shared configuration options for storage providers.
/// </summary>
public abstract class StorageOptionsBase
{
    /// <summary>
    /// The connection string to the Azure Blob Storage account or local Azurite emulator.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The default container or root folder name where files are uploaded.
    /// For AWS S3, this is used as the S3 bucket name.
    /// </summary>
    public string Container { get; set; } = string.Empty;

    /// <summary>
    /// Optional endpoint override (e.g. a CDN or local proxy) to rewrite presigned SAS/S3 URLs.
    /// Preserves path and query parameters untouched.
    /// </summary>
    public string? PublicBlobEndpoint { get; set; }

    /// <summary>
    /// Which storage engine provider to register: "AzureBlob" (default), "LocalFile", or "S3".
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

    // ── AWS S3 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// AWS region, e.g. "eu-west-2". Required when Provider is "S3".
    /// </summary>
    public string? AwsRegion { get; set; }

    /// <summary>
    /// AWS access key ID. Omit to use the ambient IAM / environment credential chain.
    /// </summary>
    public string? AwsAccessKeyId { get; set; }

    /// <summary>
    /// AWS secret access key. Omit to use the ambient IAM / environment credential chain.
    /// Must be set if <see cref="AwsAccessKeyId" /> is set, and omitted if it is not.
    /// </summary>
    public string? AwsSecretAccessKey { get; set; }

    /// <summary>
    /// Overrides the S3 endpoint URL. Use this for LocalStack (http://localhost:4566)
    /// or other S3-compatible services (e.g. MinIO). Leave empty for standard AWS.
    /// </summary>
    public string? AwsServiceUrl { get; set; }

    /// <summary>
    /// When <see langword="true" />, forces path-style addressing for S3 requests.
    /// Required for LocalStack and MinIO. Defaults to <see langword="false" />.
    /// </summary>
    public bool AwsForcePathStyle { get; set; } = false;

    /// <summary>
    /// Copies configuration property values from another <see cref="StorageOptionsBase"/> instance.
    /// </summary>
    /// <param name="other">The source options instance to copy from.</param>
    protected void CopyFrom(StorageOptionsBase other)
    {
        ArgumentNullException.ThrowIfNull(other);

        ConnectionString = other.ConnectionString;
        Container = other.Container;
        PublicBlobEndpoint = other.PublicBlobEndpoint;
        Provider = other.Provider;
        RootPath = other.RootPath;
        SigningKey = other.SigningKey;
        PublicBaseUrl = other.PublicBaseUrl;
        LocalFileDownloadRoute = other.LocalFileDownloadRoute;
        BufferSize = other.BufferSize;
        AwsRegion = other.AwsRegion;
        AwsAccessKeyId = other.AwsAccessKeyId;
        AwsSecretAccessKey = other.AwsSecretAccessKey;
        AwsServiceUrl = other.AwsServiceUrl;
        AwsForcePathStyle = other.AwsForcePathStyle;
    }
}
