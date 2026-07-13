using System.Security.Cryptography;
using System.Text;

namespace Storage.Vector;

// Signs local-file presigned URLs with HMAC-SHA256 over "{container}/{key}/{expiresAt}",
// since a local disk has no native SAS-token equivalent to Azure Blob's GenerateSasUri.
public static class LocalFileUrlSigner
{
    public static string Compute(string signingKey, string container, string key, long expiresAtUnixSeconds)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        var payload = $"{container}/{key}/{expiresAtUnixSeconds}";
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool Verify(string signingKey, string container, string key, long expiresAtUnixSeconds, string providedSignatureHex)
    {
        var expected = Compute(signingKey, container, key, expiresAtUnixSeconds);

        byte[] expectedBytes;
        byte[] providedBytes;
        try
        {
            expectedBytes = Convert.FromHexString(expected);
            providedBytes = Convert.FromHexString(providedSignatureHex);
        }
        catch (FormatException)
        {
            return false;
        }

        // Fixed-time comparison avoids leaking how many leading bytes matched via timing.
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
