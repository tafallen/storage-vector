using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Storage.Vector;

/// <summary>
/// Provides OpenTelemetry <see cref="ActivitySource"/> tracing and <see cref="Meter"/> metrics instrumentation
/// for Storage.Vector operations.
/// </summary>
public static class StorageDiagnostics
{
    /// <summary>
    /// The OpenTelemetry ActivitySource name ("Storage.Vector").
    /// </summary>
    public const string ActivitySourceName = "Storage.Vector";

    /// <summary>
    /// The OpenTelemetry Meter name ("Storage.Vector").
    /// </summary>
    public const string MeterName = "Storage.Vector";

    /// <summary>
    /// Gets the shared <see cref="ActivitySource"/> instance for tracing.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.5");

    /// <summary>
    /// Gets the shared <see cref="Meter"/> instance for metrics collection.
    /// </summary>
    public static readonly Meter Meter = new(MeterName, "1.0.5");

    /// <summary>
    /// Counter tracking total bytes uploaded to storage providers.
    /// </summary>
    public static readonly Counter<long> BytesUploaded = Meter.CreateCounter<long>(
        "storage_vector_bytes_uploaded",
        unit: "bytes",
        description: "Total bytes written to storage providers.");

    /// <summary>
    /// Counter tracking total bytes downloaded from storage providers.
    /// </summary>
    public static readonly Counter<long> BytesDownloaded = Meter.CreateCounter<long>(
        "storage_vector_bytes_downloaded",
        unit: "bytes",
        description: "Total bytes read from storage providers.");

    /// <summary>
    /// Counter tracking total storage operations by operation name and status.
    /// </summary>
    public static readonly Counter<long> OperationsCount = Meter.CreateCounter<long>(
        "storage_vector_operations_total",
        unit: "operations",
        description: "Total number of storage provider operations executed.");
}
