# Technical Debt & Architecture Assessment: Storage.Vector

This document provides a deep technical debt review of the `Storage.Vector` library. It covers architectural improvements, security enhancements, performance bottlenecks, and usability gaps, along with recommended resolutions.

---

## 1. Summary of Identified Technical Debt

| Area | Issue Description | Severity | Impact | Status |
|---|---|---|---|---|
| **Usability / Coupling** | Hardcoded presigned URL route (`/api/v1/media/local-file/`) in `LocalFileStorageProvider` | High | Couples the library to the specific controller route of the FAMTree API, reducing reusability in other apps. | **Resolved** (in v1.0.1 via `LocalFileDownloadRoute`) |
| **Performance** | Synchronous file creation/opening in `LocalFileStorageProvider` before streaming | Medium | Blocks thread-pool threads during I/O initialization on high-throughput deployments. | **Resolved** (in v1.0.1 via async `FileStream` constructors) |
| **Stability** | TOCTOU (Time-of-Check to Time-of-Use) race condition in file access | Medium | Possibility of a file being deleted or locked between `File.Exists` and `File.OpenRead` calls. | **Resolved** (in v1.0.1 by opening directly inside `try-catch`) |
| **Usability** | Suppressed compiler warnings (`CS1591`) for missing XML documentation comments | Low | Missing IDE IntelliSense guidance for developers consuming the library. | **Resolved** (in v1.0.1 by adding comments & removing warning suppression) |
| **Stability / Cloud** | Azure SAS token generation assumes Shared Access Keys connection strings | High | Crashes under TokenCredential/Managed Identity setups. | **Resolved** (in v1.0.2 via User Delegation SAS fallback) |
| **Performance** | Hardcoded FileStream buffer sizes (`4KB`) | Medium | High OS syscall overhead on large file transfers. | **Resolved** (in v1.0.2 via configurable `BufferSize` option, default `64KB`) |
| **Usability** | URL signature verification requires manual instantiation of `LocalFileUrlSigner` | Medium | Leaks verification details into controller-level logic. | **Resolved** (in v1.0.2 via `IStorageProvider.VerifyPresignedUrl`) |
| **Security** | Missing argument validation for container and key names | High | Invalid/empty arguments could cause undefined directory resolution. | **Resolved** (in v1.0.2 via strict guard checks in `LocalFilePathResolver`) |

---

## 2. Detailed Findings & Proposed Resolutions

### Finding 1: Hardcoded URL Route for Local Files
In `LocalFileStorageProvider.cs`:
```csharp
var url = $"{_options.PublicBaseUrl!.TrimEnd('/')}/api/v1/media/local-file/{container}/{key}?expires={expiresAt}&sig={signature}";
```
**Problem**: The path segment `/api/v1/media/local-file/` is hardcoded. Any other application importing `Storage.Vector` would be forced to implement this exact route layout to download files.
**Resolution**: Make the route template configurable via `StorageOptions`. Introduce a `LocalFileDownloadRoute` option defaulting to `"api/v1/media/local-file"`.
```csharp
// In StorageOptions.cs
public string LocalFileDownloadRoute { get; set; } = "api/v1/media/local-file";
```

---

### Finding 2: Synchronous File I/O Initialization
In `LocalFileStorageProvider.PutObjectAsync`:
```csharp
await using var dest = File.Create(path); // Sync I/O handle creation
await data.CopyToAsync(dest, ct);
```
And in `GetObjectAsync`:
```csharp
Stream stream = File.OpenRead(path); // Sync I/O handle creation
```
**Problem**: `File.Create` and `File.OpenRead` execute blocking, synchronous OS handle requests. For large files or high-concurrency scenarios (e.g., NAS/network shares), this can degrade performance.
**Resolution**: Use the `FileStream` constructor with `FileOptions.Asynchronous` to ensure Windows/Linux asynchronous file I/O pipelines are utilized:
```csharp
// For writing
var dest = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);

// For reading
var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
```

---

### Finding 3: TOCTOU (Time-of-Check to Time-of-Use) Race Conditions
In `LocalFileStorageProvider.GetObjectAsync`:
```csharp
if (!File.Exists(path))
{
    throw new StorageException(StorageErrorKind.NotFound, $"No object at '{container}/{key}'.");
}
// Race window: file could be deleted here by another thread/process
Stream stream = File.OpenRead(path);
```
**Problem**: Checking `File.Exists` before opening introduces a race condition. If the file is deleted or locked inside the race window, it throws a raw `FileNotFoundException` or `IOException`, which maps to `StorageErrorKind.Unavailable` instead of `NotFound`.
**Resolution**: Attempt to open the file directly inside a `try-catch` block and map `FileNotFoundException` or `DirectoryNotFoundException` explicitly to `StorageErrorKind.NotFound`:
```csharp
try
{
    return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true));
}
catch (FileNotFoundException ex)
{
    throw new StorageException(StorageErrorKind.NotFound, $"No object at '{container}/{key}'.", ex);
}
catch (DirectoryNotFoundException ex)
{
    throw new StorageException(StorageErrorKind.NotFound, $"No container or object at '{container}/{key}'.", ex);
}
```

---

### Finding 4: Missing Public XML Comments (CS1591 Suppressed)
In `Storage.Vector.csproj`:
```xml
<NoWarn>$(NoWarn);CS1591</NoWarn>
```
**Problem**: Public classes like `IStorageProvider`, `StorageException`, and options are undocumented in code, forcing developers to look up README markdown files rather than receiving inline documentation in their IDE.
**Resolution**: Write high-quality XML comments for all public structures and remove the `CS1591` warning suppression to enforce documentation coverage.

---

## 3. Secondary Tech Debt & Future Improvement Candidates

Additionally, the following secondary architectural and performance items have surfaced:

### Finding 5: Azure Token-Credential (Managed Identity) Support Gaps
* **Problem**: In `AzureBlobStorageProvider.GetPresignedUrlAsync`, the code calls `blobClient.GenerateSasUri(...)` directly. This method throws an `InvalidOperationException` if the `BlobServiceClient` is authenticated using Microsoft Entra ID (Managed Identity / `TokenCredential`) instead of a Shared Access Key connection string. To generate SAS URIs securely using token credentials, the provider must request a **User Delegation Key** first.
* **Resolution**: Enhance `GetPresignedUrlAsync` to detect if the service client can acquire a user delegation key, or allow configuring SAS parameters for token credentials.

### Finding 6: Hardcoded FileStream Buffer Sizes
* **Problem**: `LocalFileStorageProvider` hardcodes `bufferSize: 4096` in `FileStream` allocations. For high-throughput servers streaming media files (e.g. video files, large backups), a `4KB` buffer causes excessive OS syscall overhead. 
* **Resolution**: Elevate the default buffer size to `64KB` (`65536` bytes), or expose it as a configurable parameter (`BufferSize`) in `StorageOptions`.

### Finding 7: Signature Verification Usability Gap
* **Problem**: `LocalFileUrlSigner` is a static helper class. In ASP.NET Core controllers, developers are forced to manually construct it and pass the `SigningKey` from injected options. This leaks details of signature verification into application-level code.
* **Resolution**: Expose a verification abstraction (e.g. `bool VerifyPresignedUrl(string url)`) directly on `IStorageProvider` or introduce an `IUrlSigner` interface, letting the DI container resolve the provider and sign/verify seamlessly.

