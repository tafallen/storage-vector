using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Storage.Vector;

/// <summary>
/// An <see cref="IHealthCheck"/> implementation that validates storage provider connectivity
/// and permissions by probing the configured container.
/// </summary>
public sealed class StorageProviderHealthCheck : IHealthCheck
{
    private readonly IStorageProvider _provider;
    private readonly StorageOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageProviderHealthCheck"/> class.
    /// </summary>
    /// <param name="provider">The primary storage provider instance.</param>
    /// <param name="options">The storage options.</param>
    public StorageProviderHealthCheck(IStorageProvider provider, IOptions<StorageOptions> options)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = string.IsNullOrWhiteSpace(_options.Container) ? "default" : _options.Container;
            await _provider.EnsureContainerExistsAsync(container, cancellationToken).ConfigureAwait(false);

            var data = new Dictionary<string, object>
            {
                ["provider"] = _provider.GetType().Name,
                ["container"] = container,
            };

            return HealthCheckResult.Healthy($"Storage provider '{_provider.GetType().Name}' container '{container}' is accessible.", data);
        }
        catch (Exception ex)
        {
            var data = new Dictionary<string, object>
            {
                ["provider"] = _provider.GetType().Name,
                ["container"] = _options.Container ?? string.Empty,
            };

            return HealthCheckResult.Unhealthy($"Storage provider '{_provider.GetType().Name}' health check failed.", ex, data);
        }
    }
}
