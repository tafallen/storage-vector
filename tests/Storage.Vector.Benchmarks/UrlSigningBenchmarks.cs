using System.Text;
using BenchmarkDotNet.Attributes;
using Storage.Vector;

namespace Storage.Vector.Benchmarks;

[MemoryDiagnoser]
public class UrlSigningBenchmarks
{
    private const string SigningKey = "test-signing-key-long-enough-32-chars-minimum";
    private const string Container = "documents";
    private const string Key = "invoices/2026/invoice-12345.pdf";
    private const long Expiry = 1800000000;
    
    private byte[] _signingKeyBytes = Array.Empty<byte>();
    private string _signature = "";

    [GlobalSetup]
    public void Setup()
    {
        _signingKeyBytes = Encoding.UTF8.GetBytes(SigningKey);
        _signature = LocalFileUrlSigner.Compute(_signingKeyBytes, Container, Key, Expiry);
    }

    [Benchmark(Baseline = true)]
    public string ComputeSignatureOriginalStringKey()
    {
        return LocalFileUrlSigner.Compute(SigningKey, Container, Key, Expiry);
    }

    [Benchmark]
    public string ComputeSignatureOptimizedPreEncodedKey()
    {
        return LocalFileUrlSigner.Compute(_signingKeyBytes, Container, Key, Expiry);
    }

    [Benchmark]
    public bool VerifySignatureOriginalStringKey()
    {
        return LocalFileUrlSigner.Verify(SigningKey, Container, Key, Expiry, _signature);
    }

    [Benchmark]
    public bool VerifySignatureOptimizedPreEncodedKey()
    {
        return LocalFileUrlSigner.Verify(_signingKeyBytes, Container, Key, Expiry, _signature);
    }
}
