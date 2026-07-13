using Azure;

namespace Storage.Vector;

public enum StorageErrorKind
{
    NotFound,
    AccessDenied,
    Unavailable,
    Unknown,
}

public class StorageException : Exception
{
    public StorageErrorKind Kind { get; }

    public StorageException(StorageErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

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
