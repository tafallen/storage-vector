using System;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Options;
using Storage.Vector;

namespace Storage.Vector.Benchmarks;

[MemoryDiagnoser]
public class PresignedUrlVerificationBenchmarks
{
    private LocalFileStorageProvider _localProvider = null!;
    private AzureBlobStorageProvider _azureProvider = null!;
    private AwsS3StorageProvider _awsProvider = null!;

    private string _localUrl = "";
    private string _azureSasUrl = "";
    private string _awsS3PresignedUrl = "";

    [GlobalSetup]
    public void Setup()
    {
        // 1. LocalFile Provider setup
        var localOptions = Options.Create(new StorageOptions
        {
            RootPath = AppContext.BaseDirectory,
            SigningKey = "test-signing-key-long-enough-32-chars-minimum",
            PublicBaseUrl = "http://localhost:5000",
        });
        _localProvider = new LocalFileStorageProvider(localOptions);
        var expiry = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var sig = LocalFileUrlSigner.Compute(System.Text.Encoding.UTF8.GetBytes("test-signing-key-long-enough-32-chars-minimum"), "docs", "invoice.pdf", expiry);
        _localUrl = $"http://localhost:5000/api/v1/media/local-file/docs/invoice.pdf?expires={expiry}&sig={sig}";

        // 2. Azure Provider setup (VerifyPresignedUrl checks SAS query parameters locally)
        var azureOptions = Options.Create(new StorageOptions());
        _azureProvider = new AzureBlobStorageProvider(null!, azureOptions);
        var azureExpiry = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ"));
        var azureSig = Uri.EscapeDataString(Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
        _azureSasUrl = $"https://account.blob.core.windows.net/container/file.txt?sp=r&st=2026-01-01T00:00:00Z&se={azureExpiry}&sv=2020-08-04&sr=b&sig={azureSig}";

        // 3. AWS S3 Provider setup (VerifyPresignedUrl checks SigV4 parameters locally)
        var awsOptions = Options.Create(new StorageOptions { Container = "my-bucket", AwsRegion = "eu-west-2" });
        _awsProvider = new AwsS3StorageProvider(null!, awsOptions, disposeClient: false);
        var amzDate = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("yyyyMMddTHHmmssZ");
        _awsS3PresignedUrl = $"https://my-bucket.s3.eu-west-2.amazonaws.com/photos/img.jpg?X-Amz-Date={amzDate}&X-Amz-Expires=3600";
    }

    [Benchmark(Baseline = true)]
    public bool VerifyLocalFilePresignedUrl()
    {
        return _localProvider.VerifyPresignedUrl(_localUrl);
    }

    [Benchmark]
    public bool VerifyAzureBlobSasUrl()
    {
        return _azureProvider.VerifyPresignedUrl(_azureSasUrl);
    }

    [Benchmark]
    public bool VerifyAwsS3PresignedUrl()
    {
        return _awsProvider.VerifyPresignedUrl(_awsS3PresignedUrl);
    }
}
