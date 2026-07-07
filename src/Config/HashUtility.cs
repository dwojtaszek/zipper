using System.Security.Cryptography;

namespace Zipper.Config;

internal static class HashUtility
{
    internal static string ComputeHashHex(ReadOnlySpan<byte> data, HashAlgorithm algo)
    {
#pragma warning disable S4790 // Cryptographic algorithms should be robust
#pragma warning disable S4426 // Weak cryptographic algorithm
#pragma warning disable CA5350 // Weak cryptographic algorithm is used for e-discovery compat
        var hashBytes = algo switch
        {
            HashAlgorithm.MD5 => MD5.HashData(data),
            HashAlgorithm.SHA1 => SHA1.HashData(data),
            HashAlgorithm.SHA256 => SHA256.HashData(data),
            _ => throw new ArgumentOutOfRangeException(nameof(algo)),
        };
#pragma warning restore CA5350
#pragma warning restore S4426
#pragma warning restore S4790
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    internal static Random CreateSeededRandom(FileGenerationRequest request, long workItemIndex)
    {
#pragma warning disable S2245 // Pseudo-randomness is safe for mock metadata generation
        var seed = request.Metadata.Seed.HasValue
            ? unchecked((int)(request.Metadata.Seed.Value + workItemIndex))
            : (int)workItemIndex;
        return new Random(seed);
#pragma warning restore S2245
    }

    internal static string GenerateSimulatedHash(HashAlgorithm algo, Random rng)
    {
        const string chars = "0123456789abcdef";
        var length = algo switch
        {
            HashAlgorithm.MD5 => 32,
            HashAlgorithm.SHA1 => 40,
            HashAlgorithm.SHA256 => 64,
            _ => 32,
        };
        Span<char> buffer = stackalloc char[length];
        for (int i = 0; i < length; i++)
            buffer[i] = chars[rng.Next(chars.Length)];
        return new string(buffer);
    }
}
