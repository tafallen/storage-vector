# Storage.Vector Software Architecture Document

This document describes the software architecture, component design, and integration workflows of the `Storage.Vector` library.

---

## 1. Architectural Goals

`Storage.Vector` was designed around three core principles:
1. **Portability**: Maintain zero dependencies on ASP.NET Core hosting models or application-specific business logic, relying only on standard `.NET Core` and `Microsoft.Extensions` abstractions.
2. **Security**: Centralize path traversal defenses directly inside the filesystem provider to prevent directory breakout attacks.
3. **Flexibility**: Allow developers to inject multiple, independent storage backends (e.g., a primary local filesystem and a secondary cloud backup) and configure them cleanly.

---

## 2. Component Design & Class Structure

The library is organized into three primary layers:
- **Abstractions**: Defines the storage contract (`IStorageProvider`) and error model (`StorageException`).
- **Implementations**: Concrete providers for filesystem and cloud storage.
- **Dependency Injection**: Registration glue that parses configuration, executes boot-time options validation, and handles keyed DI resolution.

### Component Diagram (Mermaid)

```mermaid
classDiagram
    class IStorageProvider {
        <<interface>>
        +PutObjectAsync(container, key, data, contentType, ct) Task~string~
        +GetObjectAsync(container, key, ct) Task~Stream~
        +DeleteObjectAsync(container, key, ct) Task
        +GetPresignedUrlAsync(container, key, expiry, ct) Task~Uri~
        +EnsureContainerExistsAsync(container, ct) Task
    }

    class IStorageContainer {
        <<interface>>
        +Name string
        +File(key) IStorageObject
        +EnsureExistsAsync(ct) Task
    }

    class IStorageObject {
        <<interface>>
        +Key string
        +Container IStorageContainer
        +UploadAsync(data, contentType, ct) Task~string~
        +DownloadAsync(ct) Task~Stream~
        +GetPresignedUrlAsync(expiry, ct) Task~Uri~
        +DeleteAsync(ct) Task
    }

    class StorageContainer {
        -IStorageProvider _provider
        +Name string
        +File(key) StorageObject
        +EnsureExistsAsync(ct) Task
    }

    class StorageObject {
        -IStorageProvider _provider
        -StorageContainer _container
        +Key string
        +Container IStorageContainer
        +UploadAsync(...) Task~string~
        +DownloadAsync(...) Task~Stream~
        +GetPresignedUrlAsync(...) Task~Uri~
        +DeleteAsync(...) Task
    }

    class AzureBlobStorageProvider {
        -BlobServiceClient _client
        -StorageOptions _options
        +PutObjectAsync(...) Task~string~
        +GetObjectAsync(...) Task~Stream~
        +DeleteObjectAsync(...) Task
        +GetPresignedUrlAsync(...) Task~Uri~
        +EnsureContainerExistsAsync(...) Task
    }

    class LocalFileStorageProvider {
        -StorageOptions _options
        -LocalFileUrlSigner _signer
        +PutObjectAsync(...) Task~string~
        +GetObjectAsync(...) Task~Stream~
        +DeleteObjectAsync(...) Task
        +GetPresignedUrlAsync(...) Task~Uri~
        +EnsureContainerExistsAsync(...) Task
    }

    class LocalFilePathResolver {
        <<utility>>
        +ResolveContained(rootPath, container, key) string
    }

    class LocalFileUrlSigner {
        -byte[] _key
        +SignUrl(baseUrl, container, key, expiry) Uri
        +VerifyUrl(urlWithSignature) bool
    }

    class AwsS3StorageProvider {
        -IAmazonS3 _s3
        -StorageOptions _options
        +PutObjectAsync(...) Task~string~
        +GetObjectAsync(...) Task~Stream~
        +DeleteObjectAsync(...) Task
        +GetPresignedUrlAsync(...) Task~Uri~
        +EnsureContainerExistsAsync(...) Task
    }

    class InMemoryStorageProvider {
        -ConcurrentDictionary _store
        +PutObjectAsync(...) Task~string~
        +GetObjectAsync(...) Task~Stream~
        +DeleteObjectAsync(...) Task
        +GetPresignedUrlAsync(...) Task~Uri~
        +ListObjectsAsync(...) IAsyncEnumerable~StorageObject~
        +Clear() void
    }

    class StorageDiagnostics {
        <<static>>
        +ActivitySource ActivitySource
        +Meter Meter
        +BytesUploaded Counter~long~
        +BytesDownloaded Counter~long~
        +OperationsCount Counter~long~
    }

    class StorageProviderHealthCheck {
        -IStorageProvider _provider
        -StorageOptions _options
        +CheckHealthAsync(...) Task~HealthCheckResult~
    }

    IStorageProvider <|.. AzureBlobStorageProvider
    IStorageProvider <|.. LocalFileStorageProvider
    IStorageProvider <|.. AwsS3StorageProvider
    IStorageProvider <|.. InMemoryStorageProvider
    IStorageContainer <|.. StorageContainer
    IStorageObject <|.. StorageObject
    StorageContainer ..> StorageObject : Resolves
    LocalFileStorageProvider ..> LocalFilePathResolver : Uses
    LocalFileStorageProvider ..> LocalFileUrlSigner : Uses
```

---

## 3. Detailed Component Descriptions

### A. Abstractions & Observability
- **`IStorageProvider`**: The unified interface for uploading, downloading (full and byte-range), streaming object enumeration (`ListObjectsAsync`), deleting, and presigning objects. It hides underlying directory structures or storage account APIs from callers.
- **`StorageException` & `StorageErrorKind`**: Direct exceptions from Azure SDK (e.g. `RequestFailedException`), AWS SDK (`AmazonS3Exception`), or filesystem I/O (`UnauthorizedAccessException`, `FileNotFoundException`) are translated into `StorageException` with a standard `StorageErrorKind` (`NotFound`, `AccessDenied`, `Unavailable`, `Unknown`).
- **`StorageDiagnostics`**: Provides native OpenTelemetry `ActivitySource` tracing ("Storage.Vector") and `Meter` metrics ("Storage.Vector") with counters tracking `BytesUploaded`, `BytesDownloaded`, and `OperationsCount`.
- **`StorageProviderHealthCheck`**: An ASP.NET Core `IHealthCheck` implementation that probes container readiness and permissions via `EnsureContainerExistsAsync`.

### B. Local Filesystem Components
- **`LocalFileStorageProvider`**: Manages reading and writing files under a defined root path. Supports byte-range downloads via `BoundedStream`. Pre-caches normalized base path references, manages a thread-safe directory creation cache, and utilizes a local file existence cache to bypass disk I/O when generating presigned URLs.
- **`LocalFilePathResolver`**: Implements cross-platform path-traversal containment checks. It validates against colons (`:`), drive letters, and leading path separators on all operating systems (Windows, Linux, macOS), and optimizes standard subpath joins using `Path.Join` with `ReadOnlySpan<char>`.
- **`LocalFileUrlSigner`**: Signs download URLs using HMAC-SHA256 with a pre-encoded private key, incorporating expiration timestamps. Uses stateless `.NET 8` cryptographic APIs (`HMACSHA256.HashData`) and zero-allocation span formatting.

### C. Cloud & In-Memory Provider Components
- **`AzureBlobStorageProvider`**: Wraps Azure SDK's `BlobServiceClient`. Handles blob operations, byte-range downloads (`HttpRange`), `GetBlobsAsync` streaming pagination, SAS token generation, and maps Azure exceptions to `StorageException`. Implements a thread-safe sliding cache for Azure's `UserDelegationKey`.
- **`AwsS3StorageProvider`**: Wraps `IAmazonS3` from the AWS SDK. Uses single-bucket mode (`StorageOptions.Container`) where the `container` method argument becomes a key prefix (`{container}/{key}`). Supports byte-range downloads (`ByteRange`) and `ListObjectsV2Paginator` streaming enumeration. LocalStack, MinIO, and Cloudflare R2 are supported via `AwsServiceUrl` and `AwsForcePathStyle`.
- **`InMemoryStorageProvider`**: Thread-safe, in-memory `IStorageProvider` backed by `ConcurrentDictionary`. Features HMAC-SHA256 presigned URL simulation, range slicing, `ListObjectsAsync` filtering, and `Clear()`/`Count` helpers for unit testing and local development.

### D. Fluent Interface Layer
- **`IStorageContainer` & `IStorageObject`**: Contextual client contracts representing container-level and object-level boundaries.
- **`StorageContainer` & `StorageObject`**: Zero-allocation `readonly struct` implementations that wrap `IStorageProvider` to offer fluent, scoped methods (e.g., `provider.Container("docs").File("resume.pdf").DownloadAsync()`).
- **`FluentStorageExtensions`**: Extension entry points (`Container(...)` and `File(...)`) on `IStorageProvider` to initiate the fluent builder chains.

---

## 4. Key Workflows & Control Flows

### LocalFile Read Workflow (with Containment Check)
The following sequence diagram shows the step-by-step resolution, containment validation, and file retrieval during a read operation:

```mermaid
sequenceDiagram
    autonumber
    actor App as Application Code
    participant Provider as LocalFileStorageProvider
    participant Resolver as LocalFilePathResolver
    participant OS as Filesystem

    App->>Provider: GetObjectAsync("invoices", "2026/invoice-123.pdf")
    Provider->>Resolver: ResolveContained(RootPath, "invoices", "2026/invoice-123.pdf")
    Note over Resolver: Computes absolute paths &<br/>checks containment
    alt Path escapes RootPath boundary
        Resolver-->>Provider: Throws StorageException (AccessDenied)
        Provider-->>App: Throws StorageException
    else Path is secure
        Resolver-->>Provider: Returns absolute path
    end
    Provider->>OS: File.OpenRead(absolutePath)
    alt File does not exist
        OS-->>Provider: Throws FileNotFoundException
        Provider-->>App: Throws StorageException (NotFound)
    else File exists
        OS-->>Provider: Returns FileStream
        Provider-->>App: Returns Stream
    end
```

---

## 5. Dependency Injection Configuration

`StorageServiceCollectionExtensions` handles options binding, boot-time verification, and dependency resolution.

### Primary Registration
When `AddStorageProvider(configuration)` is invoked, it reads `Storage:Provider` (e.g., `"LocalFile"` or `"AzureBlob"`):
1. Binds the corresponding configuration section to `StorageOptions`.
2. Validates parameters at application boot (`ValidateOnStart()`), verifying connection strings or folder configurations are present before accepting connections.
3. Registers the matched implementation as a Singleton under `IStorageProvider`.

### Keyed Secondary Registration (Backup Mirroring)
To support backup sync paths, `AddSecondaryStorageProvider(configuration)` reads `Storage:Secondary:Provider`:
1. Binds options under `Storage:Secondary` into `SecondaryStorageOptions`.
2. Adapts `SecondaryStorageOptions` to the primary constructor parameter shape via `ToPrimaryShapedOptions(secondaryOptions)` at registration.
3. Registers the provider as a keyed singleton using `AddKeyedSingleton<IStorageProvider>("secondary")` (aliased by the `SecondaryProviderKey` constant).

```mermaid
graph TD
    Config[Configuration Provider] -->|AddStorageProvider| PrimarySelect{Provider?}
    PrimarySelect -->|LocalFile| LocalProv[LocalFileStorageProvider]
    PrimarySelect -->|AzureBlob| AzureProv[AzureBlobStorageProvider]
    LocalProv -->|Register| DI[DI Container: IStorageProvider]
    AzureProv -->|Register| DI

    Config -->|AddSecondaryStorageProvider| SecSelect{Secondary Provider?}
    SecSelect -->|LocalFile| SecLocal[LocalFileStorageProvider]
    SecSelect -->|AzureBlob| SecAzure[AzureBlobStorageProvider]
    SecLocal -->|Map Options| KeyedDI[DI Container: Keyed "secondary"]
    SecAzure -->|Map Options| KeyedDI
```
