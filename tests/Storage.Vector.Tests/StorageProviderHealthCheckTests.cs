using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Storage.Vector.Tests;

public class StorageProviderHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_HealthyProvider_ReturnsHealthy()
    {
        using var provider = new InMemoryStorageProvider();
        var options = Options.Create(new StorageOptions { Container = "health-container" });
        var healthCheck = new StorageProviderHealthCheck(provider, options);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("health-container", result.Description);
    }

    [Fact]
    public async Task AddStorageProviderHealthCheck_RegistersHealthCheckInDI()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddInMemoryStorageProvider(options => options.Container = "check-bucket");
        services.AddHealthChecks().AddStorageProviderHealthCheck();

        using var sp = services.BuildServiceProvider();
        var healthCheckService = sp.GetRequiredService<HealthCheckService>();

        var report = await healthCheckService.CheckHealthAsync();

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.True(report.Entries.ContainsKey("storage-vector"));
        Assert.Equal(HealthStatus.Healthy, report.Entries["storage-vector"].Status);
    }
}
