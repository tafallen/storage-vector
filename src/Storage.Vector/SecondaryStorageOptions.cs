namespace Storage.Vector;

/// <summary>
/// Configuration options for the secondary (backup/mirror) storage provider.
/// Inherits all configuration properties from <see cref="StorageOptionsBase"/> except <see cref="StorageOptions.SyncEnabled"/>.
/// Bound from "Storage:Secondary:*".
/// </summary>
public class SecondaryStorageOptions : StorageOptionsBase
{
    /// <summary>
    /// The default configuration section name for secondary storage options.
    /// </summary>
    public const string SectionName = "Storage:Secondary";
}
