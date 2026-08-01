using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Storage.Vector;

/// <summary>
/// A zero-allocation struct implementation of <see cref="IStorageObject"/>.
/// </summary>
public readonly struct StorageObject : IStorageObject
{
    private readonly IStorageProvider _provider;
    private readonly StorageContainer _container;

    /// <inheritdoc />
    public string Key { get; }

    /// <inheritdoc />
    public IStorageContainer Container => _container;

    /// <summary>
    /// Gets the size in bytes of the object.
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Gets the last modified timestamp of the object.
    /// </summary>
    public DateTimeOffset LastModified { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageObject"/> struct.
    /// </summary>
    public StorageObject(IStorageProvider provider, StorageContainer container, string key, long size = 0, DateTimeOffset? lastModified = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _container = container;
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Size = size;
        LastModified = lastModified ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageObject"/> struct.
    /// </summary>
    public StorageObject(IStorageProvider provider, string container, string key, long size = 0, DateTimeOffset? lastModified = null)
        : this(provider, new StorageContainer(provider, container), key, size, lastModified)
    {
    }

    /// <inheritdoc />
    public Task<string> UploadAsync(Stream data, string contentType, CancellationToken ct = default) =>
        _provider.PutObjectAsync(_container.Name, Key, data, contentType, ct);

    /// <inheritdoc />
    public Task<Stream> DownloadAsync(CancellationToken ct = default) =>
        _provider.GetObjectAsync(_container.Name, Key, ct);

    /// <inheritdoc />
    public Task<Uri> GetPresignedUrlAsync(TimeSpan expiry, CancellationToken ct = default) =>
        _provider.GetPresignedUrlAsync(_container.Name, Key, expiry, ct);

    /// <inheritdoc />
    public Task DeleteAsync(CancellationToken ct = default) =>
        _provider.DeleteObjectAsync(_container.Name, Key, ct);
}
