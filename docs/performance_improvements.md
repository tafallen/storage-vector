# Storage.Vector Performance & Efficiency Analysis

This document identifies performance bottlenecks in the library and proposes architectural and implementation optimizations. The focus is on reducing heap allocations, garbage collection (GC) pressure, CPU instructions in cryptographic hot paths, and redundant I/O operations.

---

## 1. Zero-Allocation Cryptographic Signatures

### Current Implementation
In `LocalFileUrlSigner.Compute`:
```csharp
using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
var payload = $"{container}/{key}/{expiresAtUnixSeconds}";
var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
return Convert.ToHexString(hash).ToLowerInvariant();
```
* **Bottlenecks**:
  1. Instantiates a new `HMACSHA256` object and performs key setup on every URL generation.
  2. Encodes the signing key into a byte array every time (`Encoding.UTF8.GetBytes`).
  3. Allocates the payload string (`$"{container}/{key}/{...}"`) and its corresponding UTF-8 byte array.
  4. `ToLowerInvariant` allocates a secondary string after conversion to hex.

### Optimization Strategy
Use **.NET 8 Span-based Cryptography** and stateless APIs to eliminate allocations completely:
1. **Pre-Encode the Signing Key**: Convert the key to a byte array once and cache it in the options class or provider instance.
2. **Stateless Hashing**: Use the static, thread-safe, and zero-allocation `HMACSHA256.HashData(key, source, destination)` method.
3. **Buffer Rented/Stack Allocations**: Format the payload string directly into a stack-allocated buffer (or rented array from `ArrayPool<byte>` if the payload exceeds 256 bytes) using Span-based UTF-8 encoding.
4. **Allocation-free Hex Formatting**: Use `Convert.ToHexString(hash)` or `hash.TryFormat(destination, out _, "x")` to write lowercase hex without creating secondary string copies.

---

## 2. Root Path Normalization Cache

### Current Implementation
In `LocalFilePathResolver.ResolveContained`:
```csharp
var root = Path.GetFullPath(rootPath);
var resolved = Path.GetFullPath(Path.Combine(root, container, key));
```
* **Bottleneck**:
  `Path.GetFullPath` is a system call that normalized relative segments, resolves symbolic links, and validates path characters. Calling it on `rootPath` for every single read, write, or delete operation is highly redundant.

### Optimization Strategy
* **Pre-Normalize base path**: Resolve `Path.GetFullPath(rootPath)` **once** during startup validation in `StorageServiceCollectionExtensions` or inside the constructor of `LocalFileStorageProvider`, storing the normalized path.
* **Reduce resolving overhead**: Inside `ResolveContained`, only call `Path.GetFullPath` on the combined target path, validating it against the pre-normalized root path.

---

## 3. Azure Entra ID User Delegation Key Caching

### Current Implementation
In `AzureBlobStorageProvider.GetPresignedUrlAsync`:
```csharp
var userDelegationKey = await _service.GetUserDelegationKeyAsync(startsOn, expiresOn, cancellationToken: ct);
```
* **Bottleneck**:
  When using Azure Active Directory (Entra ID) credentials, generating a User Delegation SAS requires calling the Azure Storage service over HTTP to obtain a User Delegation Key. Doing this on every single presigned URL call incurs significant network latency (~50–200ms per request) and will hit API rate limits.

### Optimization Strategy
* **Implement a Sliding Cache**: Cache the `UserDelegationKey` in memory within `AzureBlobStorageProvider`. The key is typically valid for up to 7 days.
* **Proactive Renewal**: Reuse the cached key, and fetch a new key asynchronously when the cached key is within a threshold of expiration (e.g., 30 minutes remaining).

---

## 4. Span-Based Query String Parsing in Url Verification

### Current Implementation
In `LocalFileStorageProvider.VerifyPresignedUrl`:
```csharp
var query = uri.Query.TrimStart('?').Split('&');
...
var path = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/');
var firstSlash = path.IndexOf('/');
var container = path.Substring(0, firstSlash);
var key = path.Substring(firstSlash + 1);
```
* **Bottleneck**:
  `Split('&')`, `Split('=')`, and multiple `Substring` calls allocate many small, short-lived string instances that put pressure on the garbage collector.

### Optimization Strategy
* Use `ReadOnlySpan<char>` to parse query and path segments. By sliding window index ranges over the URL query string, we can extract parameter keys, values, container names, and file keys without allocating any new strings during verification.

---

## 5. Reduced File System Metadata Overhead

### Current Implementation
In `LocalFileStorageProvider.PutObjectAsync`:
```csharp
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
...
var info = new FileInfo(path);
return $"{info.LastWriteTimeUtc.Ticks:x}-{info.Length:x}";
```
* **Bottlenecks**:
  1. `Directory.CreateDirectory` executes metadata checks on every single write, even if the directory exists 99.9% of the time.
  2. `FileInfo` instantiation performs additional system calls to fetch properties (`Length`, `LastWriteTimeUtc`) that can be fetched directly from the open `FileStream` or via fast OS metadata calls.

### Optimization Strategy
* **Cache Created Directories**: Keep a thread-safe concurrent set of known created container paths. Skip calling `Directory.CreateDirectory` if the directory path exists in the set.
* **Retrieve stream metrics directly**: Obtain `Length` directly from the open `FileStream` (`dest.Length`). Use `File.GetLastWriteTimeUtc(path)` directly instead of instantiating a full `FileInfo` helper object.
