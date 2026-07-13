# Storage.Vector

A portable **.NET 8** storage provider abstraction and implementations for Azure Blob Storage and Local Filesystem. Supports secondary/backup storage mirroring, presigned download URLs, path traversal protection, and validation.

[![CI](https://github.com/tafallen/storage-vector/actions/workflows/ci.yml/badge.svg)](https://github.com/tafallen/storage-vector/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Storage.Vector)](https://www.nuget.org/packages/Storage.Vector)
[![License: PolyForm Noncommercial](https://img.shields.io/badge/license-PolyForm%20Noncommercial-blue)](LICENSE)

---

## Features

- 📁 **Unified interface** — swap between local filesystem and cloud providers purely via configuration
- 🔒 **Path traversal protection** — local provider containment checks prevent directory breakout attacks
- 👯 **Keyed secondary mirroring** — configure and inject independent backup/sync storage targets via keyed DI
- 🔑 **Presigned URLs** — generate signed download URLs (HMAC-SHA256 signatures for LocalFile, SAS tokens for Azure)
- ⚙️ **Startup validation** — throws clear errors on application boot if options or paths are missing
- 📦 **NuGet-ready** — structured for `dotnet pack` with symbols (`.snupkg`)
- 💉 **DI-friendly** — integrates with `Microsoft.Extensions.DependencyInjection` via `AddStorageProvider()`
- 🛡️ **Unified error handling** — catches and translates underlying API exceptions into a structured `StorageException`

---

## Quick Start

### Install

```bash
dotnet add package Storage.Vector
```

### Register with Dependency Injection

To register the primary storage provider:
```csharp
// Program.cs / Startup.cs
builder.Services.AddStorageProvider(builder.Configuration);
```

Configure the provider options in your settings:
```json
// appsettings.json
{
  "Storage": {
    "Provider": "LocalFile", // "LocalFile" or "AzureBlob"
    "Container": "uploads",
    "Local": {
      "RootPath": "C:\\ProgramData\\MyApp\\Storage",
      "PublicBaseUrl": "https://localhost:5001/api/v1/storage",
      "SigningKey": "your-hmac-sha256-signing-key-minimum-32-chars-long"
    }
  }
}
```

### Upload and Download Files

Inject `IStorageProvider` into your services:
```csharp
public class DocumentService(IStorageProvider storage)
{
    public async Task SaveFileAsync(string key, Stream data, CancellationToken ct)
    {
        await storage.PutObjectAsync("documents", key, data, "application/pdf", ct);
    }

    public async Task<Stream> ReadFileAsync(string key, CancellationToken ct)
    {
        return await storage.GetObjectAsync("documents", key, ct);
    }
}
```

### Register a Keyed Secondary Provider (Backup Sync)

```csharp
// Register Primary IStorageProvider
builder.Services.AddStorageProvider(builder.Configuration);

// Register Secondary Keyed IStorageProvider ("secondary")
builder.Services.AddSecondaryStorageProvider(builder.Configuration);
```

Configure both in settings:
```json
{
  "Storage": {
    "Provider": "LocalFile",
    "Container": "uploads",
    "Local": {
      "RootPath": "C:\\Storage\\Primary",
      "PublicBaseUrl": "https://localhost:5001/storage",
      "SigningKey": "primary-key"
    },
    "Secondary": {
      "Provider": "AzureBlob",
      "Container": "backups",
      "Azure": {
        "ConnectionString": "UseDevelopmentStorage=true"
      }
    }
  }
}
```

Resolve the secondary provider using the `SecondaryProviderKey` constant:
```csharp
public class SyncService(
    IStorageProvider primary,
    [FromKeyedServices(StorageServiceCollectionExtensions.SecondaryProviderKey)] IStorageProvider secondary)
{
    public async Task MirrorAsync(string key, CancellationToken ct)
    {
        using var data = await primary.GetObjectAsync("documents", key, ct);
        await secondary.PutObjectAsync("documents", key, data, "application/octet-stream", ct);
    }
}
```

### Without DI (direct use)

```csharp
// LocalFile
var localOptions = Options.Create(new StorageOptions
{
    Provider = "LocalFile",
    RootPath = "C:\\Storage",
    PublicBaseUrl = "https://localhost:5001/storage",
    SigningKey = "secret-signing-key"
});
IStorageProvider localProvider = new LocalFileStorageProvider(localOptions);

// AzureBlob
var azureOptions = Options.Create(new StorageOptions
{
    Provider = "AzureBlob",
    Container = "media",
    ConnectionString = "UseDevelopmentStorage=true"
});
var client = new BlobServiceClient(azureOptions.Value.ConnectionString);
IStorageProvider azureProvider = new AzureBlobStorageProvider(client, azureOptions);
```

---

## Detailed API & Options Reference

### Generating and Validating Presigned URLs
Generate a URL that routes download requests through your local endpoint and validates them with a signature:

```csharp
// Generate
var url = await storage.GetPresignedUrlAsync("documents", "invoice.pdf", TimeSpan.FromMinutes(15), ct);

// Validate (in your Controller/Endpoint)
var signer = new LocalFileUrlSigner(options.Value.SigningKey);
var requestUrl = $"{Request.Path}{Request.QueryString}";

if (!signer.VerifyUrl(requestUrl))
{
    return Forbid("Presigned URL is expired or has an invalid signature.");
}
```

### Unified Exception Handling
All underlying filesystem or Azure SDK network/authorization errors are mapped into a `StorageException` containing a `StorageErrorKind` enum:

```csharp
try
{
    await storage.GetObjectAsync("documents", "missing.pdf", ct);
}
catch (StorageException ex)
{
    switch (ex.ErrorKind)
    {
        case StorageErrorKind.NotFound:
            Console.WriteLine("File or container not found.");
            break;
        case StorageErrorKind.AccessDenied:
            Console.WriteLine("Unauthorized access to storage path.");
            break;
        case StorageErrorKind.Transient:
            Console.WriteLine("Transient network issue. Retry later.");
            break;
        default:
            Console.WriteLine($"Storage operation failed: {ex.Message}");
            break;
    }
}
```

---

## Configuration

| Option                      | Type     | Default   | Description |
|-----------------------------|----------|-----------|-------------|
| `Storage:Provider`          | `string` | *(None)*  | Storage engine selection: `"LocalFile"` or `"AzureBlob"` |
| `Storage:Container`         | `string` | *(None)*  | Default container/folder name to build roots in |
| `Storage:Local:RootPath`    | `string` | *(None)*  | Directory containing storage containers (`LocalFile` only) |
| `Storage:Local:PublicBaseUrl`| `string`| *(None)*  | Base URL to route signed requests (`LocalFile` only) |
| `Storage:Local:SigningKey`  | `string` | *(None)*  | Secret key used to sign URLs (`LocalFile` only) |
| `Storage:Azure:ConnectionString`| `string`| *(None)*| Storage Account Connection String (`AzureBlob` only) |
| `Storage:Azure:PublicBlobEndpoint`| `string`| *(None)*| Optional CDN public blob endpoint overlay (`AzureBlob` only) |

---

## License

This project is licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE).

