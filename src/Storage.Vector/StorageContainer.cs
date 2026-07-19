using System;
using System.Threading;
using System.Threading.Tasks;

namespace Storage.Vector;

/// <summary>
/// A zero-allocation struct implementation of <see cref="IStorageContainer"/>.
/// </summary>
public readonly struct StorageContainer : IStorageContainer
{
    private readonly IStorageProvider _provider;

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageContainer"/> struct.
    /// </summary>
    public StorageContainer(IStorageProvider provider, string name)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Resolves an object context within this container.
    /// </summary>
    public StorageObject File(string key) => new(_provider, this, key);

    /// <inheritdoc />
    IStorageObject IStorageContainer.File(string key) => File(key);

    /// <inheritdoc />
    public Task EnsureExistsAsync(CancellationToken ct = default) =>
        _provider.EnsureContainerExistsAsync(Name, ct);
}
