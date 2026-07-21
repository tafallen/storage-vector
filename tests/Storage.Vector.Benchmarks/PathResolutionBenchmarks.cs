using BenchmarkDotNet.Attributes;
using Storage.Vector;

namespace Storage.Vector.Benchmarks;

[MemoryDiagnoser]
public class PathResolutionBenchmarks
{
    private const string RootPath = "C:\\ProgramData\\MyApp\\Storage";
    private const string Container = "documents";
    private const string Key = "invoices/2026/invoice-12345.pdf";

    private string _normalizedRoot = "";
    private string _rootWithSeparator = "";

    [GlobalSetup]
    public void Setup()
    {
        _normalizedRoot = Path.GetFullPath(RootPath);
        _rootWithSeparator = _normalizedRoot.EndsWith(Path.DirectorySeparatorChar) ? _normalizedRoot : _normalizedRoot + Path.DirectorySeparatorChar;
    }

    [Benchmark(Baseline = true)]
    public string ResolveContainedOriginal()
    {
        return LocalFilePathResolver.ResolveContained(RootPath, Container, Key);
    }

    [Benchmark]
    public string ResolveContainedOptimized()
    {
        return LocalFilePathResolver.ResolveContainedFast(_normalizedRoot, _rootWithSeparator, Container, Key);
    }
}
