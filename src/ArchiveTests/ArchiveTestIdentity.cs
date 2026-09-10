using System.Security.Cryptography;
using System.Text;

namespace Zipper.ArchiveTests;

/// <summary>
/// Deterministic Fixture ID identity (REQ-210): atc- plus 64 lowercase SHA-256 hex characters
/// over the canonical LF-terminated descriptor. The final Archive hash is computed by the
/// caller and passed in; this function never hashes paths, timestamps, suite names, or runtime
/// hash codes. Folder and suite selection are not arguments.
/// </summary>
internal static class ArchiveTestIdentity
{
    internal const string DescriptorMagic = "zipper-archive-test";
    internal const string DescriptorVersion = "1";
    internal const string Prefix = "atc-";

    /// <summary>
    /// Builds the exact UTF-8 canonical descriptor. No BOM, LF delimiters, invariant decimal
    /// integers, final LF included. Fields: magic, descriptor version, generator contract
    /// version, Case Key, case revision, expectation revision, Seed, final Archive SHA-256.
    /// </summary>
    internal static byte[] BuildDescriptorBytes(
        string generatorContractVersion, string caseKey, int caseRevision, int expectationRevision, int seed, string archiveSha256)
    {
        ValidateContractVersion(generatorContractVersion);
        ValidateCaseKey(caseKey);
        ValidateRevision(caseRevision, nameof(caseRevision));
        ValidateRevision(expectationRevision, nameof(expectationRevision));
        ValidateSha256(archiveSha256, nameof(archiveSha256));

        var descriptor = new StringBuilder()
            .Append(DescriptorMagic).Append('\n')
            .Append(DescriptorVersion).Append('\n')
            .Append(generatorContractVersion).Append('\n')
            .Append(caseKey).Append('\n')
            .Append(caseRevision).Append('\n')
            .Append(expectationRevision).Append('\n')
            .Append(seed).Append('\n')
            .Append(archiveSha256).Append('\n');

        return Encoding.UTF8.GetBytes(descriptor.ToString());
    }

    /// <summary>
    /// Computes the Fixture ID: "atc-" + lowercase hex of SHA-256 over the canonical descriptor.
    /// Culture-independent; the same inputs always reproduce the same ID (REQ-211 boundary).
    /// </summary>
    internal static string ComputeFixtureId(
        string generatorContractVersion, string caseKey, int caseRevision, int expectationRevision, int seed, string archiveSha256)
    {
        var descriptorBytes = BuildDescriptorBytes(generatorContractVersion, caseKey, caseRevision, expectationRevision, seed, archiveSha256);
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(descriptorBytes));
    }

    internal static bool IsFixtureId(string value) =>
        value.Length == 68
        && value.StartsWith(Prefix, StringComparison.Ordinal)
        && IsLowercaseHex(value.AsSpan(Prefix.Length));

    internal static bool IsSha256Hex(string value) => value.Length == 64 && IsLowercaseHex(value);

    private static bool IsLowercaseHex(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateContractVersion(string version)
    {
        if (!int.TryParse(version, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new ArgumentException($"generatorContractVersion '{version}' must be a positive integer string.");
        }
    }

    private static void ValidateCaseKey(string caseKey)
    {
        if (caseKey.Length == 0 || !KeySegments(caseKey))
        {
            throw new ArgumentException($"caseKey '{caseKey}' must be lowercase ASCII segments: [a-z0-9]+(-[a-z0-9]+)*.");
        }
    }

    private static bool KeySegments(string caseKey)
    {
        foreach (var segment in caseKey.Split('-'))
        {
            if (segment.Length == 0)
            {
                return false;
            }

            foreach (var c in segment)
            {
                if (c is not (>= 'a' and <= 'z' or >= '0' and <= '9'))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void ValidateRevision(int revision, string name)
    {
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be positive.");
        }
    }

    private static void ValidateSha256(string value, string name)
    {
        if (!IsSha256Hex(value))
        {
            throw new ArgumentException($"{name} must be 64 lowercase hex characters.");
        }
    }
}
