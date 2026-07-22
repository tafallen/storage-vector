namespace Storage.Vector;

/// <summary>
/// Specifies the supported storage engine providers.
/// </summary>
public enum StorageProviderKind
{
    /// <summary>
    /// Azure Blob Storage (or local Azurite emulator).
    /// </summary>
    AzureBlob,

    /// <summary>
    /// Local filesystem storage.
    /// </summary>
    LocalFile,

    /// <summary>
    /// Amazon Web Services S3 (or S3-compatible endpoints like LocalStack or MinIO).
    /// </summary>
    S3,
}
