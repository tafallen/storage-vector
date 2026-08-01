using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Storage.Vector;

/// <summary>
/// A thread-safe, high-performance in-memory implementation of <see cref="IStorageProvider"/>
/// designed for unit testing and local development without disk or external emulator dependencies.
/// </summary>
public sealed class InMemoryStorageProvider : IStorageProvider, IDisposable
{
    private readonly ConcurrentDictionary<(string Container, string Key), StorageEntry> _store = new();
    private readonly ConcurrentDictionary<string, bool> _containers = new(StringComparer.Ordinal);
    private readonly byte[] _signingKey;
    private readonly string _baseUrl;

    private readonly record struct StorageEntry(byte[] Content, string ContentType, DateTimeOffset LastModified);

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryStorageProvider"/> class with default options.
    /// </summary>
    public InMemoryStorageProvider()
        : this(Options.Create(new StorageOptions
        {
            SigningKey = "in-memory-signing-key-32-chars-minimum-length!",
            PublicBaseUrl = "http://localhost/in-memory",
        }))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryStorageProvider"/> class.
    /// </summary>
    /// <param name="options">The storage options.</param>
    public InMemoryStorageProvider(IOptions<StorageOptions> options)
    {
        var opts = options?.Value ?? new StorageOptions();
        var key = string.IsNullOrWhiteSpace(opts.SigningKey)
            ? "in-memory-signing-key-32-chars-minimum-length!"
            : opts.SigningKey;

        _signingKey = Encoding.UTF8.GetBytes(key);
        _baseUrl = (opts.PublicBaseUrl ?? "http://localhost/in-memory").TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(opts.Container))
        {
            _containers.TryAdd(opts.Container, true);
        }
    }

    /// <inheritdoc />
    public async Task<string> PutObjectAsync(string container, string key, Stream data, string contentType, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(data);

        ct.ThrowIfCancellationRequested();

        using var ms = new MemoryStream();
        await data.CopyToAsync(ms, ct).ConfigureAwait(false);
        var bytes = ms.ToArray();

        var now = DateTimeOffset.UtcNow;
        var entry = new StorageEntry(bytes, contentType ?? "application/octet-stream", now);

        _store[(container, key)] = entry;
        _containers.TryAdd(container, true);

        return string.Create(CultureInfo.InvariantCulture, $"{now.Ticks:x}-{bytes.Length:x}");
    }

    /// <inheritdoc />
    public Task<Stream> GetObjectAsync(string container, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        ct.ThrowIfCancellationRequested();

        if (!_store.TryGetValue((container, key), out var entry))
        {
            throw new StorageException(StorageErrorKind.NotFound, $"Object '{container}/{key}' was not found in memory.");
        }

        return Task.FromResult<Stream>(new MemoryStream(entry.Content, writable: false));
    }

    /// <inheritdoc />
    public Task<Stream> GetObjectAsync(string container, string key, long offset, long? length = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        ct.ThrowIfCancellationRequested();

        if (!_store.TryGetValue((container, key), out var entry))
        {
            throw new StorageException(StorageErrorKind.NotFound, $"Object '{container}/{key}' was not found in memory.");
        }

        int start = (int)Math.Min(offset, entry.Content.Length);
        int count = length.HasValue ? (int)Math.Min(length.Value, entry.Content.Length - start) : entry.Content.Length - start;

        return Task.FromResult<Stream>(new MemoryStream(entry.Content, start, count, writable: false));
    }

    /// <inheritdoc />
    public Task DeleteObjectAsync(string container, string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        ct.ThrowIfCancellationRequested();

        _store.TryRemove((container, key), out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EnsureContainerExistsAsync(string container, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        ct.ThrowIfCancellationRequested();

        _containers.TryAdd(container, true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Uri> GetPresignedUrlAsync(string container, string key, TimeSpan expiry, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        ct.ThrowIfCancellationRequested();

        var expires = DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds();
        var sig = LocalFileUrlSigner.Compute(_signingKey, container, key, expires);
        var url = $"{_baseUrl}/{Uri.EscapeDataString(container)}/{Uri.EscapeDataString(key)}?expires={expires}&sig={sig}";

        return Task.FromResult(new Uri(url));
    }

    /// <inheritdoc />
    public bool VerifyPresignedUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            var uri = new Uri(url);
            var querySpan = uri.Query.AsSpan();
            if (querySpan.StartsWith("?"))
            {
                querySpan = querySpan.Slice(1);
            }

            ReadOnlySpan<char> expiresSpan = default;
            ReadOnlySpan<char> sigSpan = default;

            while (!querySpan.IsEmpty)
            {
                int ampIndex = querySpan.IndexOf('&');
                ReadOnlySpan<char> parameter = ampIndex == -1 ? querySpan : querySpan.Slice(0, ampIndex);
                querySpan = ampIndex == -1 ? default : querySpan.Slice(ampIndex + 1);

                int eqIndex = parameter.IndexOf('=');
                if (eqIndex > 0)
                {
                    var keySpan = parameter.Slice(0, eqIndex);
                    var valueSpan = parameter.Slice(eqIndex + 1);

                    if (keySpan.Equals("expires", StringComparison.OrdinalIgnoreCase))
                    {
                        expiresSpan = valueSpan;
                    }
                    else if (keySpan.Equals("sig", StringComparison.OrdinalIgnoreCase))
                    {
                        sigSpan = valueSpan;
                    }
                }
            }

            if (expiresSpan.IsEmpty || sigSpan.IsEmpty)
            {
                return false;
            }

            if (!long.TryParse(expiresSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresUnix))
            {
                return false;
            }

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresUnix)
            {
                return false;
            }

            // Extract container and key from path: /in-memory/{container}/{key}
            var path = uri.AbsolutePath.Trim('/');
            var segments = path.Split('/');
            if (segments.Length < 2)
            {
                return false;
            }

            var container = Uri.UnescapeDataString(segments[^2]);
            var key = Uri.UnescapeDataString(segments[^1]);

            return LocalFileUrlSigner.Verify(_signingKey, container, key, expiresUnix, sigSpan.ToString());
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task<bool> VerifyPresignedUrlAsync(string url, CancellationToken ct = default)
    {
        return Task.FromResult(VerifyPresignedUrl(url));
    }

    /// <summary>
    /// Clears all stored in-memory containers and objects.
    /// </summary>
    public void Clear()
    {
        _store.Clear();
        _containers.Clear();
    }

    /// <summary>
    /// Gets the number of objects currently stored in memory.
    /// </summary>
    public int Count => _store.Count;

    /// <inheritdoc />
    public void Dispose()
    {
        _store.Clear();
        _containers.Clear();
    }
}
