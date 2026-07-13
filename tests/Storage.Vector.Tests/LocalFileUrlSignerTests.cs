using Storage.Vector;
using Xunit;

namespace Storage.Vector.Tests;

public class LocalFileUrlSignerTests
{
    [Fact]
    public void Compute_SameInputs_ProducesSameSignature()
    {
        var sig1 = LocalFileUrlSigner.Compute("test-signing-key", "famtree-media", "events/E001/cert.jpg", 1_800_000_000);
        var sig2 = LocalFileUrlSigner.Compute("test-signing-key", "famtree-media", "events/E001/cert.jpg", 1_800_000_000);

        Assert.Equal(sig1, sig2);
        Assert.Matches("^[0-9a-f]{64}$", sig1);
    }

    [Fact]
    public void Compute_DifferentKey_ProducesDifferentSignature()
    {
        var sig1 = LocalFileUrlSigner.Compute("test-signing-key", "famtree-media", "events/E001/cert.jpg", 1_800_000_000);
        var sig2 = LocalFileUrlSigner.Compute("other-signing-key", "famtree-media", "events/E001/cert.jpg", 1_800_000_000);

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void Verify_MatchingSignature_ReturnsTrue()
    {
        var sig = LocalFileUrlSigner.Compute("test-signing-key", "famtree-media", "events/E001/cert.jpg", 1_800_000_000);

        Assert.True(LocalFileUrlSigner.Verify("test-signing-key", "famtree-media", "events/E001/cert.jpg", 1_800_000_000, sig));
    }

    [Fact]
    public void Verify_TamperedContainer_ReturnsFalse()
    {
        var sig = LocalFileUrlSigner.Compute("test-signing-key", "famtree-media", "events/E001/cert.jpg", 1_800_000_000);

        Assert.False(LocalFileUrlSigner.Verify("test-signing-key", "some-other-container", "events/E001/cert.jpg", 1_800_000_000, sig));
    }

    [Fact]
    public void Verify_MalformedSignature_ReturnsFalseInsteadOfThrowing()
    {
        Assert.False(LocalFileUrlSigner.Verify("test-signing-key", "famtree-media", "events/E001/cert.jpg", 1_800_000_000, "not-hex!!"));
    }
}

