using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Options;
using Storage.Vector;

namespace Storage.Vector.Benchmarks;

[MemoryDiagnoser]
public class FileExistenceBenchmarks
{
    private string _tempDir = "";
    private LocalFileStorageProvider _provider = null!;
    private const string Container = "test-container";
    private const string Key = "test-file.txt";

    [GlobalSetup]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "storage-vector-bench-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        
        var options = Options.Create(new StorageOptions
        {
            RootPath = _tempDir,
            SigningKey = "test-signing-key-long-enough-32-chars-minimum",
            PublicBaseUrl = "http://localhost",
        });
        _provider = new LocalFileStorageProvider(options);

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        _provider.PutObjectAsync(Container, Key, stream, "text/plain", CancellationToken.None).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore cleanup exceptions
        }
    }

    [Benchmark]
    public async Task<Uri> GetPresignedUrl()
    {
        return await _provider.GetPresignedUrlAsync(Container, Key, TimeSpan.FromMinutes(10), CancellationToken.None);
    }
}
