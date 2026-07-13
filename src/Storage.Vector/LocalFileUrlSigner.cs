using System.Security.Cryptography;
using System.Text;

namespace Storage.Vector;

/// <summary>
/// Signs and verifies local-file presigned URLs with HMAC-SHA256 over "{container}/{key}/{expiresAt}".
/// This provides a secure local disk equivalent to Azure SAS tokens.
/// </summary>
public static class LocalFileUrlSigner
{
    /// <summary>
    /// Computes the HMAC-SHA256 signature for a given container, key, and expiration.
    /// </summary>
    /// <param name="signingKey">The HMAC signing key.</param>
    /// <param name="container">The container name.</param>
    /// <param name="key">The object key.</param>
    /// <param name="expiresAtUnixSeconds">The expiration timestamp in Unix seconds.</param>
    /// <returns>A lowercase hexadecimal signature string.</returns>
    public static string Compute(string signingKey, string container, string key, long expiresAtUnixSeconds)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        var payload = $"{container}/{key}/{expiresAtUnixSeconds}";
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies if a provided signature matches the expected signature for a given container, key, and expiration.
    /// Uses a fixed-time comparison to prevent timing attacks.
    /// </summary>
    /// <param name="signingKey">The HMAC signing key.</param>
    /// <param name="container">The container name.</param>
    /// <param name="key">The object key.</param>
    /// <param name="expiresAtUnixSeconds">The expiration timestamp in Unix seconds.</param>
    /// <param name="providedSignatureHex">The hexadecimal signature provided by the client.</param>
    /// <returns>True if the signature matches, false otherwise.</returns>
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
