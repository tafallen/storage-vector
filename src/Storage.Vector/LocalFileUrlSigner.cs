using System;
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
        if (signingKey == null) throw new ArgumentNullException(nameof(signingKey));
        byte[] keyBytes = Encoding.UTF8.GetBytes(signingKey);
        return Compute(keyBytes, container, key, expiresAtUnixSeconds);
    }

    /// <summary>
    /// Computes the HMAC-SHA256 signature for a given container, key, and expiration using pre-encoded signing key bytes.
    /// </summary>
    public static string Compute(ReadOnlySpan<byte> signingKeyBytes, string container, string key, long expiresAtUnixSeconds)
    {
        if (container == null) throw new ArgumentNullException(nameof(container));
        if (key == null) throw new ArgumentNullException(nameof(key));

        int containerByteCount = Encoding.UTF8.GetByteCount(container);
        int keyByteCount = Encoding.UTF8.GetByteCount(key);
        int totalPayloadSize = containerByteCount + keyByteCount + 22; // Buffer for slashes and expires long

        byte[]? rented = null;
        Span<byte> payloadBuffer = totalPayloadSize <= 256 
            ? stackalloc byte[256] 
            : (rented = System.Buffers.ArrayPool<byte>.Shared.Rent(totalPayloadSize));
            
        try
        {
            int bytesWritten = 0;
            bytesWritten += Encoding.UTF8.GetBytes(container, payloadBuffer.Slice(bytesWritten));
            payloadBuffer[bytesWritten++] = (byte)'/';
            bytesWritten += Encoding.UTF8.GetBytes(key, payloadBuffer.Slice(bytesWritten));
            payloadBuffer[bytesWritten++] = (byte)'/';
            
            if (!expiresAtUnixSeconds.TryFormat(payloadBuffer.Slice(bytesWritten), out int expiresBytesWritten, default, System.Globalization.CultureInfo.InvariantCulture))
            {
                var expiresStr = expiresAtUnixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                expiresBytesWritten = Encoding.UTF8.GetBytes(expiresStr, payloadBuffer.Slice(bytesWritten));
            }
            bytesWritten += expiresBytesWritten;
            
            Span<byte> hashBuffer = stackalloc byte[32];
            HMACSHA256.HashData(signingKeyBytes, payloadBuffer.Slice(0, bytesWritten), hashBuffer);
            
            return ConvertToHexLower(hashBuffer);
        }
        finally
        {
            if (rented != null)
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
            }
        }
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
        if (signingKey == null) throw new ArgumentNullException(nameof(signingKey));
        byte[] keyBytes = Encoding.UTF8.GetBytes(signingKey);
        return Verify(keyBytes, container, key, expiresAtUnixSeconds, providedSignatureHex);
    }

    /// <summary>
    /// Verifies if a provided signature matches using pre-encoded signing key bytes.
    /// Uses a fixed-time comparison to prevent timing attacks.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> signingKeyBytes, string container, string key, long expiresAtUnixSeconds, string providedSignatureHex)
    {
        if (container == null) throw new ArgumentNullException(nameof(container));
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (providedSignatureHex == null || providedSignatureHex.Length != 64)
        {
            return false;
        }

        Span<byte> providedBytes = stackalloc byte[32];
        if (!TryHexToBytes(providedSignatureHex, providedBytes))
        {
            return false;
        }

        Span<byte> expectedBytes = stackalloc byte[32];
        if (!TryComputeRawHash(signingKeyBytes, container, key, expiresAtUnixSeconds, expectedBytes))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private static bool TryComputeRawHash(ReadOnlySpan<byte> signingKeyBytes, string container, string key, long expiresAtUnixSeconds, Span<byte> destination)
    {
        int containerByteCount = Encoding.UTF8.GetByteCount(container);
        int keyByteCount = Encoding.UTF8.GetByteCount(key);
        int totalPayloadSize = containerByteCount + keyByteCount + 22;

        byte[]? rented = null;
        Span<byte> payloadBuffer = totalPayloadSize <= 256 
            ? stackalloc byte[256] 
            : (rented = System.Buffers.ArrayPool<byte>.Shared.Rent(totalPayloadSize));
            
        try
        {
            int bytesWritten = 0;
            bytesWritten += Encoding.UTF8.GetBytes(container, payloadBuffer.Slice(bytesWritten));
            payloadBuffer[bytesWritten++] = (byte)'/';
            bytesWritten += Encoding.UTF8.GetBytes(key, payloadBuffer.Slice(bytesWritten));
            payloadBuffer[bytesWritten++] = (byte)'/';
            
            if (!expiresAtUnixSeconds.TryFormat(payloadBuffer.Slice(bytesWritten), out int expiresBytesWritten, default, System.Globalization.CultureInfo.InvariantCulture))
            {
                return false;
            }
            bytesWritten += expiresBytesWritten;
            
            HMACSHA256.HashData(signingKeyBytes, payloadBuffer.Slice(0, bytesWritten), destination);
            return true;
        }
        finally
        {
            if (rented != null)
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static string ConvertToHexLower(ReadOnlySpan<byte> bytes)
    {
        return string.Create(64, (Part1: System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(0, 8)),
                                  Part2: System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8, 8)),
                                  Part3: System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(16, 8)),
                                  Part4: System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(24, 8))), 
            (span, state) =>
            {
                WriteUlongHex(state.Part1, span.Slice(0, 16));
                WriteUlongHex(state.Part2, span.Slice(16, 16));
                WriteUlongHex(state.Part3, span.Slice(32, 16));
                WriteUlongHex(state.Part4, span.Slice(48, 16));
            });
    }

    private static void WriteUlongHex(ulong val, Span<char> dest)
    {
        for (int i = 0; i < 8; i++)
        {
            byte b = (byte)(val >> (i * 8));
            dest[i * 2] = GetHexChar(b >> 4);
            dest[i * 2 + 1] = GetHexChar(b & 0x0F);
        }
    }

    private static char GetHexChar(int value)
    {
        return value < 10 ? (char)('0' + value) : (char)('a' + (value - 10));
    }

    private static bool TryHexToBytes(ReadOnlySpan<char> hex, Span<byte> bytes)
    {
        if (hex.Length != bytes.Length * 2)
            return false;

        for (int i = 0; i < bytes.Length; i++)
        {
            int h1 = GetHexVal(hex[i * 2]);
            int h2 = GetHexVal(hex[i * 2 + 1]);
            if (h1 == -1 || h2 == -1)
                return false;

            bytes[i] = (byte)((h1 << 4) | h2);
        }
        return true;
    }

    private static int GetHexVal(char hex)
    {
        int val = (int)hex;
        return val switch
        {
            >= '0' and <= '9' => val - '0',
            >= 'A' and <= 'F' => val - 'A' + 10,
            >= 'a' and <= 'f' => val - 'a' + 10,
            _ => -1
        };
    }
}
