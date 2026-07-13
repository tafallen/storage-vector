# Storage.Vector

`Storage.Vector` is a portable, dependency-free .NET 8 class library providing a unified abstraction over storage providers. It includes native implementations for **Azure Blob Storage** and the **Local Filesystem**, along with features for secondary mirroring (backup/sync), presigned download URLs, path traversal protection, and robust error mapping.

## Features

- **Unified Interface**: Swap between local filesystem and cloud providers purely via configuration changes.
- **Path Traversal Protection**: The Local Filesystem provider enforces containment checks, ensuring file operations cannot escape the configured container/root path boundaries.
- **Keyed Secondary Mirroring**: Configure and inject a secondary storage provider (e.g., for automated background backup or migration paths) using keyed DI.
- **Presigned URLs**: Easily generate signed download URLs with custom expirations. The Local Filesystem provider uses HMAC-SHA256 signature validation.
- **Startup Validation**: Options validation executes on boot, throwing clear startup errors if connections or paths are missing or misconfigured.
- **Unified Error Handling**: Storage errors map directly to a typed `StorageException` for consistent error handling.

## Installation

Add the NuGet package to your project:

```shell
dotnet add package Storage.Vector
```

---

## The Storage Contract (`IStorageProvider`)

All providers implement `IStorageProvider`:

```csharp
namespace Storage.Vector;

public interface IStorageProvider
{
    // Uploads or updates an object. Returns the resolved access string/URL.
    Task<string> PutObjectAsync(string container, string key, Stream data, string contentType, CancellationToken ct);

    // Downloads the object stream. Throws StorageException if not found.
    Task<Stream> GetObjectAsync(string container, string key, CancellationToken ct);

    // Deletes the object. Idempotent (succeeds even if the object doesn't exist).
    Task DeleteObjectAsync(string container, string key, CancellationToken ct);

    // Generates a short-lived presigned download URL.
    Task<Uri> GetPresignedUrlAsync(string container, string key, TimeSpan expiry, CancellationToken ct);

    // Ensures the container (or local directory) is provisioned.
    Task EnsureContainerExistsAsync(string container, CancellationToken ct);
}
```

---

## Configuration (`appsettings.json`)

Configure your storage options under the `"Storage"` section.

### 1. Azure Blob Storage Setup
```json
{
  "Storage": {
    "Provider": "AzureBlob",
    "Container": "media-uploads",
    "Azure": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=youraccount;AccountKey=...;EndpointSuffix=core.windows.net",
      "PublicBlobEndpoint": "https://yourcustomdomain.com" // Optional public CDN endpoint override
    }
  }
}
```

### 2. Local Filesystem Setup
```json
{
  "Storage": {
    "Provider": "LocalFile",
    "Container": "media-uploads",
    "Local": {
      "RootPath": "C:\\ProgramData\\MyApp\\Storage", // Directory containing storage containers
      "PublicBaseUrl": "https://localhost:5001/api/v1/storage", // Base URL routing to your local file download controller
      "SigningKey": "your-hmac-sha256-signing-key-minimum-32-chars-long" // Used to sign presigned URLs
    }
  }
}
```

### 3. Dual-Provider (Primary + Keyed Secondary Backup) Setup
```json
{
  "Storage": {
    "Provider": "LocalFile",
    "Container": "media-uploads",
    "Local": {
      "RootPath": "C:\\ProgramData\\MyApp\\Storage",
      "PublicBaseUrl": "https://localhost:5001/api/v1/storage",
      "SigningKey": "primary-signing-key"
    },
    "Secondary": {
      "Provider": "AzureBlob",
      "Container": "media-backup",
      "Azure": {
        "ConnectionString": "UseDevelopmentStorage=true"
      }
    }
  }
}
```

---

## Dependency Injection Registration

Use the extension methods on `IServiceCollection` to register your storage providers.

### Primary Storage Provider
```csharp
using Storage.Vector;

var builder = WebApplication.CreateBuilder(args);

// Registers the default IStorageProvider based on "Storage:Provider" value
builder.Services.AddStorageProvider(builder.Configuration);
```

### Primary + Secondary Keyed Setup
If you want to configure a secondary backup or migration target, register it alongside the primary provider:
```csharp
using Storage.Vector;

var builder = WebApplication.CreateBuilder(args);

// Register Primary (resolves to IStorageProvider)
builder.Services.AddStorageProvider(builder.Configuration);

// Register Secondary (resolves to IStorageProvider keyed under "secondary")
builder.Services.AddSecondaryStorageProvider(builder.Configuration);
```

---

## Code Examples

### 1. Uploading and Downloading Files
```csharp
using Storage.Vector;

public class DocumentManager(IStorageProvider storage)
{
    public async Task SaveInvoiceAsync(Guid invoiceId, Stream stream, CancellationToken ct)
    {
        // Container boundaries are enforced.
        await storage.PutObjectAsync("invoices", $"{invoiceId}.pdf", stream, "application/pdf", ct);
    }

    public async Task<Stream> OpenInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        try
        {
            return await storage.GetObjectAsync("invoices", $"{invoiceId}.pdf", ct);
        }
        catch (StorageException ex) when (ex.ErrorKind == StorageErrorKind.NotFound)
        {
            // Object or container was not found
            throw new FileNotFoundException("Invoice could not be located.", ex);
        }
    }
}
```

### 2. Resolving Keyed Secondary Storage (Backup Sync Pattern)
Inject the secondary provider using the `SecondaryProviderKey` constant:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Storage.Vector;

public class StorageBackupWorker(
    IStorageProvider primaryStorage,
    [FromKeyedServices(StorageServiceCollectionExtensions.SecondaryProviderKey)] IStorageProvider secondaryStorage)
{
    public async Task SyncFileAsync(string container, string key, CancellationToken ct)
    {
        // Download from Primary
        using var stream = await primaryStorage.GetObjectAsync(container, key, ct);

        // Upload to Keyed Secondary Backup
        await secondaryStorage.PutObjectAsync(container, key, stream, "application/octet-stream", ct);
    }
}
```

### 3. Presigned URLs (Local File Validation example)
For the `LocalFile` provider, generating a URL returns a route pointing to your custom file endpoint with signature query parameters. You can validate these incoming parameters directly:

```csharp
[HttpGet("api/v1/storage/{container}/{key}")]
public async Task<IActionResult> DownloadFile(
    string container,
    string key,
    [FromQuery] string expires,
    [FromQuery] string signature,
    [FromServices] IStorageProvider storage,
    [FromServices] IOptions<StorageOptions> options)
{
    // For LocalFile, we can validate the presigned URL HMAC-SHA256 signature
    var signer = new LocalFileUrlSigner(options.Value.SigningKey);
    var requestUrl = $"{Request.Path}{Request.QueryString}";

    if (!signer.VerifyUrl(requestUrl))
    {
        return Forbid("Presigned URL is expired or has an invalid signature.");
    }

    var stream = await storage.GetObjectAsync(container, key, HttpContext.RequestAborted);
    return File(stream, "application/octet-stream");
}
```

---

## Exception Handling

Errors thrown by storage operations (e.g. file locks, network timeouts, access denied, container missing) are automatically caught and wrapped inside `StorageException`.

Use `ex.ErrorKind` to cleanly handle storage errors programmatically:
```csharp
try
{
    await storage.GetObjectAsync("media", "image.png", ct);
}
catch (StorageException ex)
{
    switch (ex.ErrorKind)
    {
        case StorageErrorKind.NotFound:
            // Handle 404
            break;
        case StorageErrorKind.AccessDenied:
            // Handle unauthorized access
            break;
        case StorageErrorKind.Transient:
            // Retry transient network issues
            break;
        default:
            // General internal error
            break;
    }
}
```

---

## License

This project is licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE).

