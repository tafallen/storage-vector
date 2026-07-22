namespace Storage.Vector;

/// <summary>
/// Configuration options for the primary storage provider.
/// </summary>
public class StorageOptions : StorageOptionsBase
{
    /// <summary>
    /// The default configuration section name for primary storage options.
    /// </summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// Gating flag to control whether a secondary storage provider is registered under the keyed slot "secondary".
    /// </summary>
    public bool SyncEnabled { get; set; } = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageOptions"/> class.
    /// </summary>
    public StorageOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageOptions"/> class by copying properties from a base options instance.
    /// </summary>
    /// <param name="other">The options instance to copy from.</param>
    public StorageOptions(StorageOptionsBase other)
    {
        CopyFrom(other);
    }
}
