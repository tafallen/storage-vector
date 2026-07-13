# Storage.Vector

A portable .NET 8 storage provider abstraction and implementations for Azure Blob Storage and Local Filesystem. Supports secondary/backup storage mirroring, presigned download URLs, and validation.

## Features

- **Storage Provider Abstraction**: Implementations for Local File System and Azure Blob Storage.
- **Secondary Mirroring**: Optionally mirror writes and deletes to a secondary storage provider (e.g. backup or migration target).
- **Presigned URLs**: Easily generate signed download URLs with custom expiration.
- **Dependency Injection**: Seamless registration into Microsoft.Extensions.DependencyInjection.

## Installation

Add the package via NuGet:

```shell
dotnet add package Storage.Vector
```

## Setup & Usage

### 1. Configuration Options

Add the following to your `appsettings.json`:

```json
{
  "Storage": {
    "Provider": "LocalFile", // "LocalFile" or "AzureBlob"
    "Container": "your-container-or-folder",
    "Local": {
      "RootPath": "C:\\path\\to\\local\\storage",
      "SigningKey": "your-url-signing-key-for-local-provider"
    },
    "Azure": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=..."
    },
    "Secondary": {
      "Provider": "AzureBlob",
      "Container": "backup-container",
      "Azure": {
        "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=..."
      }
    }
  }
}
```

### 2. Dependency Injection Registration

Register the storage services in your startup/program file:

```csharp
using Storage.Vector;

var builder = WebApplication.CreateBuilder(args);

// Register Storage Services
builder.Services.AddStorageServices(builder.Configuration);
```

### 3. Usage Example

Inject `IStorageProvider` into your services:

```csharp
public class MediaService
{
    private readonly IStorageProvider _storageProvider;

    public MediaService(IStorageProvider storageProvider)
    {
        _storageProvider = storageProvider;
    }

    public async Task SaveFileAsync(string filename, Stream contentStream, string contentType)
    {
        await _storageProvider.PutObjectAsync(
            "my-container",
            filename,
            contentStream,
            contentType,
            CancellationToken.None
        );
    }
}
```

## License

This project is licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE).
