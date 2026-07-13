using Azure;

namespace Storage.Vector;

/// <summary>
/// Specifies categories of storage failures.
/// </summary>
public enum StorageErrorKind
{
    /// <summary>The file or container was not found.</summary>
    NotFound,
    /// <summary>Permission denied or access is forbidden.</summary>
    AccessDenied,
    /// <summary>The service is temporarily unavailable or returned a 5xx response.</summary>
    Unavailable,
    /// <summary>An unexpected or uncategorized error occurred.</summary>
    Unknown,
}

/// <summary>
/// Unified exception class thrown by <see cref="IStorageProvider"/> implementations.
/// </summary>
public class StorageException : Exception
{
    /// <summary>
    /// Gets the category of the storage error.
    /// </summary>
    public StorageErrorKind Kind { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageException"/> class.
    /// </summary>
    /// <param name="kind">The category of the storage error.</param>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception that caused the error, if any.</param>
    public StorageException(StorageErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    /// <summary>
    /// Maps a raw Azure <see cref="RequestFailedException"/> to a structured <see cref="StorageException"/>.
    /// </summary>
    /// <param name="ex">The Azure request failed exception.</param>
    /// <returns>A mapped storage exception.</returns>
    public static StorageException FromAzureException(RequestFailedException ex)
    {
        var kind = ex.Status switch
        {
            404 => StorageErrorKind.NotFound,
            401 or 403 => StorageErrorKind.AccessDenied,
            >= 500 => StorageErrorKind.Unavailable,
            _ => StorageErrorKind.Unknown,
        };

        return new StorageException(kind, $"Storage operation failed: {ex.Message}", ex);
    }
}
