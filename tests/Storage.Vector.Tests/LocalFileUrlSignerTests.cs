using System.Text;
using Storage.Vector;
using Xunit;

namespace Storage.Vector.Tests;

public class LocalFileUrlSignerTests
{
    private static readonly byte[] KeyBytes = Encoding.UTF8.GetBytes("test-signing-key");
    private static readonly byte[] OtherKeyBytes = Encoding.UTF8.GetBytes("other-signing-key");

    [Fact]
    public void Compute_SameInputs_ProducesSameSignature()
    {
        var sig1 = LocalFileUrlSigner.Compute(KeyBytes, "famtree-media", "events/E001/cert.jpg", 1_800_000_000);
        var sig2 = LocalFileUrlSigner.Compute(KeyBytes, "famtree-media", "events/E001/cert.jpg", 1_800_000_000);

        Assert.Equal(sig1, sig2);
        Assert.Matches("^[0-9a-f]{64}$", sig1);
    }

    [Fact]
    public void Compute_DifferentKey_ProducesDifferentSignature()
    {
        var sig1 = LocalFileUrlSigner.Compute(KeyBytes, "famtree-media", "events/E001/cert.jpg", 1_800_000_000);
        var sig2 = LocalFileUrlSigner.Compute(OtherKeyBytes, "famtree-media", "events/E001/cert.jpg", 1_800_000_000);

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void Verify_MatchingSignature_ReturnsTrue()
    {
        var sig = LocalFileUrlSigner.Compute(KeyBytes, "famtree-media", "events/E001/cert.jpg", 1_800_000_000);

        Assert.True(LocalFileUrlSigner.Verify(KeyBytes, "famtree-media", "events/E001/cert.jpg", 1_800_000_000, sig));
    }

    [Fact]
    public void Verify_TamperedContainer_ReturnsFalse()
    {
        var sig = LocalFileUrlSigner.Compute(KeyBytes, "famtree-media", "events/E001/cert.jpg", 1_800_000_000);

        Assert.False(LocalFileUrlSigner.Verify(KeyBytes, "some-other-container", "events/E001/cert.jpg", 1_800_000_000, sig));
    }

    [Fact]
    public void Verify_MalformedSignature_ReturnsFalseInsteadOfThrowing()
    {
        Assert.False(LocalFileUrlSigner.Verify(KeyBytes, "famtree-media", "events/E001/cert.jpg", 1_800_000_000, "not-hex!!"));
        Assert.False(LocalFileUrlSigner.Verify(KeyBytes, "famtree-media", "events/E001/cert.jpg", 1_800_000_000, null!));
        Assert.False(LocalFileUrlSigner.Verify(KeyBytes, "famtree-media", "events/E001/cert.jpg", 1_800_000_000, ""));
        Assert.False(LocalFileUrlSigner.Verify(KeyBytes, "famtree-media", "events/E001/cert.jpg", 1_800_000_000, "1234")); // Too short
        Assert.False(LocalFileUrlSigner.Verify(KeyBytes, "famtree-media", "events/E001/cert.jpg", 1_800_000_000, new string('0', 63) + "Z")); // Invalid char
    }

    [Fact]
    public void Compute_LargePayload_RentsFromArrayPoolAndComputesSignature()
    {
        var longKey = new string('a', 300);
        var sig = LocalFileUrlSigner.Compute(KeyBytes, "famtree-media", longKey, 1_800_000_000);

        Assert.NotNull(sig);
        Assert.Equal(64, sig.Length);
        Assert.True(LocalFileUrlSigner.Verify(KeyBytes, "famtree-media", longKey, 1_800_000_000, sig));
    }

    [Fact]
    public void Verify_UppercaseHex_ParsesAndVerifiesCorrectly()
    {
        var sig = LocalFileUrlSigner.Compute(KeyBytes, "famtree-media", "key.txt", 1_800_000_000);
        var upperSig = sig.ToUpperInvariant();

        Assert.True(LocalFileUrlSigner.Verify(KeyBytes, "famtree-media", "key.txt", 1_800_000_000, upperSig));
    }

#pragma warning disable CS0618
    [Fact]
    public void Compute_NullArguments_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => LocalFileUrlSigner.Compute(KeyBytes, null!, "key", 100));
        Assert.Throws<ArgumentNullException>(() => LocalFileUrlSigner.Compute(KeyBytes, "container", null!, 100));
        Assert.Throws<ArgumentNullException>(() => LocalFileUrlSigner.Compute((string)null!, "container", "key", 100));

        Assert.Throws<ArgumentNullException>(() => LocalFileUrlSigner.Verify(KeyBytes, null!, "key", 100, "0000000000000000000000000000000000000000000000000000000000000000"));
        Assert.Throws<ArgumentNullException>(() => LocalFileUrlSigner.Verify(KeyBytes, "container", null!, 100, "0000000000000000000000000000000000000000000000000000000000000000"));
        Assert.Throws<ArgumentNullException>(() => LocalFileUrlSigner.Verify((string)null!, "container", "key", 100, "0000000000000000000000000000000000000000000000000000000000000000"));
    }
#pragma warning restore CS0618

#pragma warning disable CS0618
    [Fact]
    public void ObsoleteStringOverloads_WorkCorrectly()
    {
        var sig = LocalFileUrlSigner.Compute("test-signing-key", "famtree-media", "key.txt", 1_800_000_000);
        Assert.True(LocalFileUrlSigner.Verify("test-signing-key", "famtree-media", "key.txt", 1_800_000_000, sig));
    }
#pragma warning restore CS0618
}
