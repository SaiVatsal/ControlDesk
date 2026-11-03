using System.Numerics;
using System.Security.Cryptography;

namespace ControlDesk.Security.Cryptography;

/// <summary>
/// Pure C# RFC 8032 Ed25519 implementation.
/// Self-contained, zero external dependencies, works across all .NET versions.
/// </summary>
public static class Ed25519Algorithm
{
    public const int PublicKeySize = 32;
    public const int SecretKeySize = 32;
    public const int SignatureSize = 64;

    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    private static readonly BigInteger L = BigInteger.Parse("7237005577332262213973186563042994240857116359379907606001950938285454250989");
    private static readonly BigInteger D = (-121665 * ModInverse(121666, P)) % P;
    private static readonly BigInteger I = BigInteger.ModPow(2, (P - 1) / 4, P);

    private static readonly BigInteger Bx = BigInteger.Parse("15112221349535400772501151409588531511454012693041857206046113283949847762202");
    private static readonly BigInteger By = BigInteger.Parse("46316835694926478169428394003475163141307993866256225615783033603165251855960");
    private static readonly Point B = new(Bx, By);

    private readonly record struct Point(BigInteger X, BigInteger Y)
    {
        public static readonly Point Zero = new(BigInteger.Zero, BigInteger.One);
    }

    private static BigInteger Mod(BigInteger a, BigInteger m)
    {
        var r = a % m;
        return r < 0 ? r + m : r;
    }

    private static BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        return BigInteger.ModPow(Mod(a, m), m - 2, m);
    }

    private static Point PointAdd(Point p1, Point p2)
    {
        var x1 = p1.X; var y1 = p1.Y;
        var x2 = p2.X; var y2 = p2.Y;

        var d_x1_x2_y1_y2 = Mod(D * x1 * x2 * y1 * y2, P);
        var x3 = Mod((x1 * y2 + y1 * x2) * ModInverse(1 + d_x1_x2_y1_y2, P), P);
        var y3 = Mod((y1 * y2 + x1 * x2) * ModInverse(1 - d_x1_x2_y1_y2, P), P);

        return new Point(x3, y3);
    }

    private static Point ScalarMult(Point p, BigInteger e)
    {
        if (e == 0) return Point.Zero;
        var r = Point.Zero;
        var q = p;
        while (e > 0)
        {
            if (!e.IsEven)
                r = PointAdd(r, q);
            q = PointAdd(q, q);
            e >>= 1;
        }
        return r;
    }

    private static byte[] EncodePoint(Point p)
    {
        var yBytes = p.Y.ToByteArray(isUnsigned: true, isBigEndian: false);
        var result = new byte[32];
        Array.Copy(yBytes, result, Math.Min(yBytes.Length, 32));
        if (p.X.IsEven == false) // odd
        {
            result[31] |= 0x80;
        }
        return result;
    }

    private static Point? DecodePoint(ReadOnlySpan<byte> span)
    {
        if (span.Length != 32) return null;
        var copy = span.ToArray();
        var xBit = (copy[31] & 0x80) != 0;
        copy[31] &= 0x7F;

        var y = new BigInteger(copy, isUnsigned: true, isBigEndian: false);
        if (y >= P) return null;

        var y2 = Mod(y * y, P);
        var u = Mod(y2 - 1, P);
        var v = Mod(D * y2 + 1, P);
        var xx = Mod(u * ModInverse(v, P), P);

        var x = BigInteger.ModPow(xx, (P + 3) / 8, P);
        if (Mod(x * x - xx, P) != 0)
        {
            x = Mod(x * I, P);
        }
        if (Mod(x * x - xx, P) != 0)
        {
            return null;
        }

        if (x.IsEven != !xBit)
        {
            x = Mod(-x, P);
        }

        return new Point(x, y);
    }

    public static void GenerateKeyPair(Span<byte> secretKey, Span<byte> publicKey)
    {
        RandomNumberGenerator.Fill(secretKey[..SecretKeySize]);
        DerivePublicKey(secretKey[..SecretKeySize], publicKey);
    }

    public static void DerivePublicKey(ReadOnlySpan<byte> secretKey, Span<byte> publicKey)
    {
        Span<byte> h = stackalloc byte[64];
        SHA512.HashData(secretKey[..32], h);

        h[0] &= 248;
        h[31] &= 127;
        h[31] |= 64;

        var a = new BigInteger(h[..32], isUnsigned: true, isBigEndian: false);
        var aPoint = ScalarMult(B, a);
        var encoded = EncodePoint(aPoint);
        encoded.CopyTo(publicKey);
    }

    public static byte[] Sign(ReadOnlySpan<byte> secretKey, ReadOnlySpan<byte> data)
    {
        Span<byte> h = stackalloc byte[64];
        SHA512.HashData(secretKey[..32], h);

        Span<byte> aBytes = stackalloc byte[32];
        h[..32].CopyTo(aBytes);
        aBytes[0] &= 248;
        aBytes[31] &= 127;
        aBytes[31] |= 64;
        var a = new BigInteger(aBytes, isUnsigned: true, isBigEndian: false);

        Span<byte> pubKey = stackalloc byte[32];
        DerivePublicKey(secretKey, pubKey);

        // r = SHA512(h[32..64] || data)
        using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        sha512.AppendData(h.Slice(32, 32));
        sha512.AppendData(data);
        var rHash = sha512.GetHashAndReset();

        var r = Mod(new BigInteger(rHash, isUnsigned: true, isBigEndian: false), L);
        var rPoint = ScalarMult(B, r);
        var rBytes = EncodePoint(rPoint);

        // k = SHA512(R || A || data)
        sha512.AppendData(rBytes);
        sha512.AppendData(pubKey);
        sha512.AppendData(data);
        var kHash = sha512.GetHashAndReset();

        var k = Mod(new BigInteger(kHash, isUnsigned: true, isBigEndian: false), L);
        var s = Mod(r + k * a, L);

        var sBytesRaw = s.ToByteArray(isUnsigned: true, isBigEndian: false);
        var sBytes = new byte[32];
        Array.Copy(sBytesRaw, sBytes, Math.Min(sBytesRaw.Length, 32));

        var signature = new byte[64];
        Buffer.BlockCopy(rBytes, 0, signature, 0, 32);
        Buffer.BlockCopy(sBytes, 0, signature, 32, 32);
        return signature;
    }

    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        if (publicKey.Length != 32 || signature.Length != 64)
            return false;

        var aPoint = DecodePoint(publicKey);
        if (aPoint == null)
            return false;

        var rBytes = signature[..32];
        var sBytes = signature.Slice(32, 32);

        var s = new BigInteger(sBytes, isUnsigned: true, isBigEndian: false);
        if (s >= L) return false;

        using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        sha512.AppendData(rBytes);
        sha512.AppendData(publicKey);
        sha512.AppendData(data);
        var kHash = sha512.GetHashAndReset();

        var k = Mod(new BigInteger(kHash, isUnsigned: true, isBigEndian: false), L);

        var sB = ScalarMult(B, s);
        var rPoint = DecodePoint(rBytes);
        if (rPoint == null) return false;

        var kA = ScalarMult(aPoint.Value, k);
        var expected = PointAdd(rPoint.Value, kA);

        return sB.X == expected.X && sB.Y == expected.Y;
    }
}
