using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Storage.Vector;

/// <summary>
/// A storage provider implementation that uses the local filesystem.
/// </summary>
public class LocalFileStorageProvider : IStorageProvider
{
    /// <summary>
    /// Caps how long a presigned URL can grant unauthenticated access (currently 1 hour).
    /// </summary>
    public static readonly TimeSpan MaxPresignedUrlExpiry = TimeSpan.FromHours(1);

    private readonly StorageOptions _options;
    private readonly string _normalizedRootPath;
    private readonly string _rootPathWithSeparator;
    private readonly byte[] _signingKeyBytes;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _createdDirectories = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _fileExistenceCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalFileStorageProvider"/> class.
    /// </summary>
    /// <param name="options">The storage configuration options.</param>
    public LocalFileStorageProvider(IOptions<StorageOptions> options)
    {
        _options = options.Value;
        var root = Path.GetFullPath(_options.RootPath ?? string.Empty);
        _normalizedRootPath = root;
        _rootPathWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        _signingKeyBytes = string.IsNullOrEmpty(_options.SigningKey) 
            ? Array.Empty<byte>() 
            : Encoding.UTF8.GetBytes(_options.SigningKey);
    }

    /// <inheritdoc />
    public async Task<string> PutObjectAsync(string container, string key, Stream data, string contentType, CancellationToken ct)
    {
        var path = ResolvePath(container, key);
        var dir = Path.GetDirectoryName(path)!;
        EnsureDirectoryCreated(dir);

        long length;
        try
        {
            // Use FileOptions.Asynchronous and useAsync: true for non-blocking handle creation and writes.
            await using var dest = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: _options.BufferSize,
                useAsync: true);
            await data.CopyToAsync(dest, _options.BufferSize, ct);
            length = dest.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException(StorageErrorKind.Unavailable, $"Could not write object '{container}/{key}'.", ex);
        }

        SetFileExistenceCache(path, true);

        var lastWrite = File.GetLastWriteTimeUtc(path);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{lastWrite.Ticks:x}-{length:x}");
    }

    /// <inheritdoc />
    public Task<Uri> GetPresignedUrlAsync(string container, string key, TimeSpan expiry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var path = ResolvePath(container, key);
        
        if (!_fileExistenceCache.TryGetValue(path, out var exists))
        {
            exists = File.Exists(path);
            SetFileExistenceCache(path, exists);
        }

        if (!exists)
        {
            throw new StorageException(StorageErrorKind.NotFound, $"No object at '{container}/{key}'.");
        }

        var cappedExpiry = expiry > MaxPresignedUrlExpiry ? MaxPresignedUrlExpiry : expiry;
        var expiresAt = DateTimeOffset.UtcNow.Add(cappedExpiry).ToUnixTimeSeconds();
        var signature = LocalFileUrlSigner.Compute(_signingKeyBytes, container, key, expiresAt);

        var route = _options.LocalFileDownloadRoute.Trim('/');
        var url = $"{_options.PublicBaseUrl!.TrimEnd('/')}/{route}/{container}/{key}?expires={expiresAt}&sig={signature}";
        return Task.FromResult(new Uri(url));
    }

    /// <inheritdoc />
    public Task<Stream> GetObjectAsync(string container, string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var path = ResolvePath(container, key);

        try
        {
            // Attempt to open the FileStream directly with useAsync: true to avoid TOCTOU race conditions.
            Stream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: _options.BufferSize,
                useAsync: true);
            
            // If successfully opened, we can safely cache that the file exists
            SetFileExistenceCache(path, true);
            return Task.FromResult(stream);
        }
        catch (FileNotFoundException ex)
        {
            SetFileExistenceCache(path, false);
            throw new StorageException(StorageErrorKind.NotFound, $"No object at '{container}/{key}'.", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new StorageException(StorageErrorKind.NotFound, $"No container or object at '{container}/{key}'.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new StorageException(StorageErrorKind.AccessDenied, $"Access denied for '{container}/{key}'.", ex);
        }
        catch (Exception ex)
        {
            throw new StorageException(StorageErrorKind.Unavailable, $"Could not read object '{container}/{key}'.", ex);
        }
    }

    /// <inheritdoc />
    public Task<Stream> GetObjectAsync(string container, string key, long offset, long? length = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var path = ResolvePath(container, key);

        try
        {
            var fileStream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: _options.BufferSize,
                useAsync: true);

            if (offset > 0)
            {
                fileStream.Seek(offset, SeekOrigin.Begin);
            }

            SetFileExistenceCache(path, true);

            Stream resultStream = fileStream;
            if (length.HasValue)
            {
                resultStream = new BoundedStream(fileStream, length.Value);
            }

            return Task.FromResult(resultStream);
        }
        catch (FileNotFoundException ex)
        {
            SetFileExistenceCache(path, false);
            throw new StorageException(StorageErrorKind.NotFound, $"No object at '{container}/{key}'.", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new StorageException(StorageErrorKind.NotFound, $"No container or object at '{container}/{key}'.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new StorageException(StorageErrorKind.AccessDenied, $"Access denied for '{container}/{key}'.", ex);
        }
        catch (Exception ex)
        {
            throw new StorageException(StorageErrorKind.Unavailable, $"Could not read object '{container}/{key}'.", ex);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StorageObject> ListObjectsAsync(string container, string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var containerPath = LocalFilePathResolver.ResolveContainedFast(_normalizedRootPath, _rootPathWithSeparator, container, string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(containerPath))
        {
            yield break;
        }

        var files = Directory.EnumerateFiles(containerPath, "*", SearchOption.AllDirectories);
        var containerPrefixLength = containerPath.Length + 1;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var relativeKey = file.Substring(containerPrefixLength).Replace('\\', '/');
            if (!string.IsNullOrEmpty(prefix) && !relativeKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var fi = new FileInfo(file);
            yield return new StorageObject(this, container, relativeKey, fi.Length, fi.LastWriteTimeUtc);
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteObjectAsync(string container, string key, CancellationToken ct)
    {
        var path = ResolvePath(container, key);

        try
        {
            File.Delete(path);
            _fileExistenceCache.TryRemove(path, out _);
        }
        catch (DirectoryNotFoundException)
        {
            // Container directory never existed — nothing to delete.
            _fileExistenceCache.TryRemove(path, out _);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException(StorageErrorKind.Unavailable, $"Could not delete object '{container}/{key}'.", ex);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EnsureContainerExistsAsync(string container, CancellationToken ct)
    {
        try
        {
            var dir = Path.Combine(_normalizedRootPath, container);
            EnsureDirectoryCreated(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageException(StorageErrorKind.Unavailable, $"Could not create container '{container}'.", ex);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool VerifyPresignedUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var querySpan = uri.Query.AsSpan();
            if (querySpan.StartsWith("?"))
            {
                querySpan = querySpan.Slice(1);
            }

            ReadOnlySpan<char> expiresStr = default;
            ReadOnlySpan<char> sig = default;

            while (!querySpan.IsEmpty)
            {
                int ampIndex = querySpan.IndexOf('&');
                ReadOnlySpan<char> parameter = ampIndex == -1 ? querySpan : querySpan.Slice(0, ampIndex);
                querySpan = ampIndex == -1 ? default : querySpan.Slice(ampIndex + 1);

                int eqIndex = parameter.IndexOf('=');
                if (eqIndex != -1)
                {
                    var keySpan = parameter.Slice(0, eqIndex);
                    var valueSpan = parameter.Slice(eqIndex + 1);

                    if (keySpan.Equals("expires", StringComparison.Ordinal))
                    {
                        expiresStr = valueSpan;
                    }
                    else if (keySpan.Equals("sig", StringComparison.Ordinal))
                    {
                        sig = valueSpan;
                    }
                }
            }

            if (expiresStr.IsEmpty || sig.IsEmpty)
            {
                return false;
            }

            if (!long.TryParse(expiresStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var expiresAt))
            {
                return false;
            }

            if (DateTimeOffset.FromUnixTimeSeconds(expiresAt) <= DateTimeOffset.UtcNow)
            {
                return false;
            }

            // Extract container and key from the path
            var route = _options.LocalFileDownloadRoute.Trim('/');
            var path = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/');
            
            if (path.StartsWith(route, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(route.Length).Trim('/');
            }

            var firstSlash = path.IndexOf('/');
            if (firstSlash <= 0)
            {
                return false;
            }

            var container = path.Substring(0, firstSlash);
            var key = path.Substring(firstSlash + 1);

            return LocalFileUrlSigner.Verify(_signingKeyBytes, container, key, expiresAt, sig.ToString());
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string ResolvePath(string container, string key) =>
        LocalFilePathResolver.ResolveContainedFast(_normalizedRootPath, _rootPathWithSeparator, container, key);

    private void SetFileExistenceCache(string path, bool exists)
    {
        if (_fileExistenceCache.ContainsKey(path) || _fileExistenceCache.Count < _options.FileExistenceCacheCapacity)
        {
            _fileExistenceCache[path] = exists;
        }
    }

    private void EnsureDirectoryCreated(string dir)
    {
        if (!_createdDirectories.TryGetValue(dir, out _))
        {
            Directory.CreateDirectory(dir);
            if (_createdDirectories.Count < 1000)
            {
                _createdDirectories.TryAdd(dir, true);
            }
        }
    }

    private sealed class BoundedStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _length;
        private long _bytesRead;

        public BoundedStream(Stream baseStream, long length)
        {
            _baseStream = baseStream;
            _length = length;
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => Math.Min(_length, _baseStream.Length - _baseStream.Position);
        public override long Position
        {
            get => _bytesRead;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long remaining = _length - _bytesRead;
            if (remaining <= 0) return 0;
            int toRead = (int)Math.Min(count, remaining);
            int read = _baseStream.Read(buffer, offset, toRead);
            _bytesRead += read;
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            long remaining = _length - _bytesRead;
            if (remaining <= 0) return 0;
            int toRead = (int)Math.Min(count, remaining);
            int read = await _baseStream.ReadAsync(buffer, offset, toRead, cancellationToken).ConfigureAwait(false);
            _bytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            long remaining = _length - _bytesRead;
            if (remaining <= 0) return 0;
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = await _baseStream.ReadAsync(buffer.Slice(0, toRead), cancellationToken).ConfigureAwait(false);
            _bytesRead += read;
            return read;
        }

        public override void Flush() => _baseStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _baseStream.Dispose();
            base.Dispose(disposing);
        }
    }
}
