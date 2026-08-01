# Storage.Vector

A portable **.NET 8** storage provider abstraction and implementations for Azure Blob Storage (including local Azurite emulation) and Local Filesystem (supporting local directories, NAS, and SMB/NFS mounts). Supports secondary/backup storage mirroring, presigned download URLs, path traversal protection, and validation.

[![CI](https://github.com/tafallen/storage-vector/actions/workflows/ci.yml/badge.svg)](https://github.com/tafallen/storage-vector/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Storage.Vector)](https://www.nuget.org/packages/Storage.Vector)
[![License: PolyForm Noncommercial](https://img.shields.io/badge/license-PolyForm%20Noncommercial-blue)](LICENSE)

---

## Features

- 📁 **Unified interface** — swap between local directories, NAS, and cloud providers purely via configuration
- ☁️ **Azurite & Azure support** — fully compatible with local Azurite emulator for dev/testing and cloud Azure Blob Storage
- ☁️ **AWS S3 support** — first-class S3 provider with single-bucket mode (`container` = key prefix), presigned URL generation, and LocalStack / MinIO compatibility
- ⚡ **Cloudflare R2 & MinIO presets** — dedicated DI extension helpers `AddCloudflareR2StorageProvider()` and `AddMinIOStorageProvider()`
- 🧪 **In-Memory provider** — thread-safe `InMemoryStorageProvider` with presigned URL simulation for unit testing and local development
- ✂️ **Byte-Range partial downloads** — `GetObjectAsync(container, key, offset, length)` overload for fast range reads
- 🔄 **Streaming object enumeration** — `IAsyncEnumerable<StorageObject> ListObjectsAsync()` for zero-allocation streaming container pagination
- 🏥 **ASP.NET Core Health Checks** — `StorageProviderHealthCheck` and `builder.AddStorageProviderHealthCheck()` for `/healthz` container readiness probing
- 📊 **OpenTelemetry instrumentation** — native `ActivitySource` tracing and `Meter` counters (`BytesUploaded`, `BytesDownloaded`, `OperationsCount`)
- 🔒 **Path traversal protection** — cross-platform directory breakout prevention (enforces security on Windows, Linux, and macOS)
- 👯 **Keyed secondary mirroring** — configure and inject independent backup/sync storage targets via keyed DI
- 🔑 **Presigned URLs** — generate signed download URLs (HMAC-SHA256 signatures for LocalFile/InMemory, SAS tokens for Azure, SigV4 for S3)
- 🚀 **High Performance & Zero-Allocation** — optimized via thread-safe in-memory metadata caches, pre-computed path normalizations, and span-based zero-allocation url signing
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

// Optional: Register ASP.NET Core Health Check
builder.Services.AddHealthChecks()
    .AddStorageProviderHealthCheck();
```

Configure the provider options in your settings:
```json
// appsettings.json
{
  "Storage": {
    "Provider": "LocalFile", // "LocalFile", "AzureBlob", "S3", or "InMemory"
    "Container": "uploads",
    "Local": {
      "RootPath": "C:\\ProgramData\\MyApp\\Storage",
      "PublicBaseUrl": "https://localhost:5001/api/v1/storage",
      "SigningKey": "your-hmac-sha256-signing-key-minimum-32-chars-long"
    }
  }
}
```

### Cloudflare R2 & MinIO Presets

```csharp
// Cloudflare R2
builder.Services.AddCloudflareR2StorageProvider(options =>
{
    options.Container = "my-r2-bucket";
    options.AwsAccessKeyId = "r2-access-key";
    options.AwsSecretAccessKey = "r2-secret-key";
    options.AwsServiceUrl = "https://<account-id>.r2.cloudflarestorage.com";
});

// MinIO
builder.Services.AddMinIOStorageProvider(options =>
{
    options.Container = "my-minio-bucket";
    options.AwsAccessKeyId = "minioadmin";
    options.AwsSecretAccessKey = "minioadmin";
    options.AwsServiceUrl = "http://localhost:9000";
});
```

### In-Memory Provider for Unit Testing

```csharp
// DI registration
builder.Services.AddInMemoryStorageProvider(options =>
{
    options.Container = "test-bucket";
});

// Direct instantiation
using var inMemoryStorage = new InMemoryStorageProvider();
```

### AWS S3

```json
{
  "Storage": {
    "Provider": "S3",
    "Container": "my-app-bucket",
    "AwsRegion": "eu-west-2",
    "AwsAccessKeyId": "AKIAIOSFODNN7EXAMPLE",
    "AwsSecretAccessKey": "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
  }
}
```

> **LocalStack / MinIO**: Add `"AwsServiceUrl": "http://localhost:4566"` and `"AwsForcePathStyle": true` to target a local S3-compatible endpoint. Omit `AwsAccessKeyId` / `AwsSecretAccessKey` entirely to use the ambient IAM credential chain (ECS task role, EC2 instance profile, etc.).

Object keys are namespaced as `{container}/{key}` within the configured S3 bucket, so a single bucket can serve multiple logical containers.

### Upload, Download, and Partial Range Reads

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

    // Byte-Range Download (e.g. video streaming, resume downloads)
    public async Task<Stream> ReadChunkAsync(string key, long offset, long length, CancellationToken ct)
    {
        return await storage.GetObjectAsync("documents", key, offset, length, ct);
    }
}
```

### Streaming Object Enumeration

Stream container contents asynchronously without loading all metadata into memory:

```csharp
public async Task ListAllDocumentsAsync(IStorageProvider storage, CancellationToken ct)
{
    await foreach (var item in storage.ListObjectsAsync("documents", prefix: "invoices/", ct))
    {
        Console.WriteLine($"Key: {item.Key}, Size: {item.Size} bytes, Modified: {item.LastModified}");
    }
}
```

### OpenTelemetry Tracing & Metrics

`Storage.Vector` includes native OpenTelemetry instrumentation via `StorageDiagnostics`:

* **ActivitySource**: `"Storage.Vector"`
* **Meter**: `"Storage.Vector"` (`storage_vector_bytes_uploaded`, `storage_vector_bytes_downloaded`, `storage_vector_operations_total`)

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(StorageDiagnostics.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(StorageDiagnostics.MeterName));
```

### Fluent Scoped Operations

To avoid repeating the container name or key in consecutive actions, scope your operations using the fluent API:

```csharp
public class DocumentService(IStorageProvider storage)
{
    public async Task ProcessInvoiceAsync(string key, Stream data, CancellationToken ct)
    {
        // 1. Scope to a container
        var container = storage.Container("documents");
        await container.EnsureExistsAsync(ct);

        // 2. Scope to a file
        var file = container.File(key);

        // 3. Perform actions on that file fluently
        await file.UploadAsync(data, "application/pdf", ct);
        
        var downloadUrl = await file.GetPresignedUrlAsync(TimeSpan.FromMinutes(15), ct);
        
        using var downloadStream = await file.DownloadAsync(ct);
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

---

## Detailed API & Options Reference

### Generating and Validating Presigned URLs
Generate a URL that routes download requests through your local endpoint and validates them with a signature:

```csharp
// Generate
var url = await storage.GetPresignedUrlAsync("documents", "invoice.pdf", TimeSpan.FromMinutes(15), ct);

// Validate (in your Controller/Endpoint)
var requestUrl = $"{Request.Path}{Request.QueryString}";

if (!storage.VerifyPresignedUrl(requestUrl))
{
    return Forbid("Presigned URL is expired or has an invalid signature.");
}
```

### Unified Exception Handling
All underlying filesystem, Azure SDK, or AWS S3 network/authorization errors are mapped into a `StorageException` containing a `StorageErrorKind` enum:

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
        case StorageErrorKind.Unavailable:
            Console.WriteLine("Storage provider unavailable or network issue.");
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
| `Storage:Provider`          | `string` | *(None)*  | Storage engine selection: `"LocalFile"`, `"AzureBlob"`, `"S3"`, or `"InMemory"` |
| `Storage:Container`         | `string` | *(None)*  | Default container/folder name to build roots in |
| `Storage:Local:RootPath`    | `string` | *(None)*  | Directory containing storage containers (`LocalFile` only) |
| `Storage:Local:PublicBaseUrl`| `string`| *(None)*  | Base URL to route signed requests (`LocalFile` only) |
| `Storage:Local:SigningKey`  | `string` | *(None)*  | Secret key used to sign URLs (`LocalFile` only) |
| `Storage:Azure:ConnectionString`| `string`| *(None)*| Storage Account Connection String (`AzureBlob` only) |
| `Storage:Azure:PublicBlobEndpoint`| `string`| *(None)*| Optional CDN public blob endpoint overlay (`AzureBlob` only) |
| `Storage:AwsRegion`         | `string` | *(None)*  | AWS Region (e.g. `"eu-west-2"`, `"us-east-1"`, `"auto"`) |
| `Storage:AwsAccessKeyId`    | `string` | *(None)*  | AWS / S3 access key ID (optional if using ambient IAM) |
| `Storage:AwsSecretAccessKey`| `string` | *(None)*  | AWS / S3 secret access key (optional if using ambient IAM) |
| `Storage:AwsServiceUrl`     | `string` | *(None)*  | Service URL for LocalStack, MinIO, or Cloudflare R2 |
| `Storage:AwsForcePathStyle` | `bool`   | `false`   | Enables path-style bucket access (`true` for MinIO/LocalStack) |

---

## License

This project is licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE).

