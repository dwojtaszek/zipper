using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ICSharpCode.SharpZipLib.Zip;

namespace Zipper.ArchiveTests;

internal sealed record AesZipFixtureArchiveSpec(
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("physicalSize")] long PhysicalSize,
    [property: JsonPropertyName("sha256")] string Sha256);

internal sealed record AesZipFixtureEntrySpec(
    [property: JsonPropertyName("ordinal")] int Ordinal,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("contentSize")] long ContentSize,
    [property: JsonPropertyName("contentSha256")] string ContentSha256,
    [property: JsonPropertyName("localHeaderMethod")] int LocalHeaderMethod,
    [property: JsonPropertyName("centralDirectoryMethod")] int CentralDirectoryMethod,
    [property: JsonPropertyName("innerCompressionMethod")] int InnerCompressionMethod,
    [property: JsonPropertyName("innerCompressionCodec")] string InnerCompressionCodec,
    [property: JsonPropertyName("encryptionStrength")] string EncryptionStrength);

internal sealed record AesZipFixtureManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("caseKey")] string CaseKey,
    [property: JsonPropertyName("container")] string Container,
    [property: JsonPropertyName("producer")] string Producer,
    [property: JsonPropertyName("producerVersion")] string ProducerVersion,
    [property: JsonPropertyName("encryptionStrength")] string EncryptionStrength,
    [property: JsonPropertyName("aesVendorVersion")] int AesVendorVersion,
    [property: JsonPropertyName("syntheticPasswordCase")] string SyntheticPasswordCase,
    [property: JsonPropertyName("regenerationRecipe")] string RegenerationRecipe,
    [property: JsonPropertyName("saltReplayNote")] string SaltReplayNote,
    [property: JsonPropertyName("archive")] AesZipFixtureArchiveSpec Archive,
    [property: JsonPropertyName("entries")] IReadOnlyList<AesZipFixtureEntrySpec> Entries);

internal sealed record AesZipInteroperabilityPublicationResult(
    IReadOnlyList<string> CaseKeys,
    string PublishedDirectory);

internal sealed record AesZipVerificationEntryResult(
    string Name,
    long ContentSize,
    string ContentSha256,
    int InnerCompressionMethod,
    int WireMethod);

internal sealed record AesZipReaderVerificationResult(
    string ReaderName,
    string Status,
    string? MissingCapability,
    string Message,
    IReadOnlyList<AesZipVerificationEntryResult> VerifiedEntries);

/// <summary>
/// Fixture-only deterministic WinZip AES-256 ZIP interoperability cases (#1472).
/// Keeps synthetic password cases out of the normal generation CLI and preserves
/// the frozen ArchiveTestCatalog / Expectation File schema contract.
/// </summary>
internal static class AesZipInteroperabilityFixtures
{
    internal const int AesWireMethod = 99;
    internal const int AesVendorVersion = 2;
    internal const string ContainerFormat = "zip";
    internal const string Aes256Strength = "AES-256";

    private const string EmptySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private const string SaltReplayExplanation =
        "The pinned producer generates a random 16-byte AES salt and 2-byte password verification value per write, " +
        "so fixture tooling pins the complete produced archive bytes (verified by SHA-256) for byte-identical replay " +
        "while recording the regeneration recipe for producing fresh equivalent archives.";

    private sealed record PinnedAesFixtureDefinition(
        AesZipFixtureManifest Manifest,
        string Base64ArchiveBytes);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly IReadOnlyDictionary<string, PinnedAesFixtureDefinition> Definitions =
        new Dictionary<string, PinnedAesFixtureDefinition>(StringComparer.Ordinal)
        {
            ["aes256-ascii-standard"] = new(
                new AesZipFixtureManifest(
                    SchemaVersion: 1,
                    CaseKey: "aes256-ascii-standard",
                    Container: ContainerFormat,
                    Producer: "7-Zip",
                    ProducerVersion: "26.06 (x64)",
                    EncryptionStrength: Aes256Strength,
                    AesVendorVersion: AesVendorVersion,
                    SyntheticPasswordCase: "ascii",
                    RegenerationRecipe:
                        "7z a -tzip -mem=AES256 -mx=0 -p<ascii-password> out.zip empty.txt store.bin && " +
                        "7z a -tzip -mem=AES256 -mx=9 -p<ascii-password> out.zip deflate.txt",
                    SaltReplayNote: SaltReplayExplanation,
                    Archive: new AesZipFixtureArchiveSpec(
                        "aes256-ascii-standard.zip",
                        634,
                        "c20340901d81009462cfbfeb39f2b5b328a93445d73c6ad3886160e95914eb58"),
                    Entries:
                    [
                        new AesZipFixtureEntrySpec(0, "deflate.txt", 512, "81dde2c525bf6b6a9e944db69c69828ab2363718bcadb7a4d744d5cd0b8bcc75", AesWireMethod, AesWireMethod, 8, "deflate", Aes256Strength),
                        new AesZipFixtureEntrySpec(1, "empty.txt", 0, EmptySha256, AesWireMethod, AesWireMethod, 0, "stored", Aes256Strength),
                        new AesZipFixtureEntrySpec(2, "store.bin", 30, "75456b5bdddef0ff8776abd70120adfc0b9886abcec29068b8bc915168142bee", AesWireMethod, AesWireMethod, 0, "stored", Aes256Strength),
                    ]),
                "UEsDBDMAAQBjAIade10AAAAAQgAAAAACAAALAAsAZGVmbGF0ZS50eHQBmQcAAgBBRQMIAKS0S2xWf6mZHN/tCYhXSVqAVCQZ5Wl3KvEC3k20obJJoKX+V5XyRt7SzguE4atHkBxeSmINQAv5pjj0wi1Twrv8JlBLAwQzAAEAYwCGnXtdAAAAABwAAAAAAAAACQALAGVtcHR5LnR4dAGZBwACAEFFAwAA7vIYlAIMdUnt4mgxUGQk59yvQ5wzr6/ZXoZ8/FBLAwQzAAEAYwCGnXtdAAAAADoAAAAeAAAACQALAHN0b3JlLmJpbgGZBwACAEFFAwAAbVXXzKaCY3JDYwxariIs0dexm9HOCtU+LGntbV8u4v4kwk+AiI0CGetda56+b/OJlFqNW+FJFCfZAlBLAQI/AzMAAQBjAIade10AAAAAQgAAAAACAAALAC8AAAAAAAAAIICkgQAAAABkZWZsYXRlLnR4dAoAIAAAAAAAAQAYAC6xzEqfft0BAAAAAAAAAAAAAAAAAAAAAAGZBwACAEFFAwgAUEsBAj8DMwABAGMAhp17XQAAAAAcAAAAAAAAAAkALwAAAAAAAAAggKSBdgAAAGVtcHR5LnR4dAoAIAAAAAAAAQAYAFOazEqfft0BAAAAAAAAAAAAAAAAAAAAAAGZBwACAEFFAwAAUEsBAj8DMwABAGMAhp17XQAAAAA6AAAAHgAAAAkALwAAAAAAAAAggKSBxAAAAHN0b3JlLmJpbgoAIAAAAAAAAQAYAAevzEqfft0BAAAAAAAAAAAAAAAAAAAAAAGZBwACAEFFAwAAUEsFBgAAAAADAAMANAEAADABAAAAAA=="),
            ["aes256-space-padded"] = new(
                new AesZipFixtureManifest(
                    SchemaVersion: 1,
                    CaseKey: "aes256-space-padded",
                    Container: ContainerFormat,
                    Producer: "7-Zip",
                    ProducerVersion: "26.06 (x64)",
                    EncryptionStrength: Aes256Strength,
                    AesVendorVersion: AesVendorVersion,
                    SyntheticPasswordCase: "leading-trailing-space",
                    RegenerationRecipe:
                        "7z a -tzip -mem=AES256 -mx=0 -p<space-padded-password> out.zip empty.txt store.bin && " +
                        "7z a -tzip -mem=AES256 -mx=9 -p<space-padded-password> out.zip deflate.txt",
                    SaltReplayNote: SaltReplayExplanation,
                    Archive: new AesZipFixtureArchiveSpec(
                        "aes256-space-padded.zip",
                        634,
                        "6b928797af65cd64aad6d89de6ac006504895ee277af7360f6593b70e5772f7a"),
                    Entries:
                    [
                        new AesZipFixtureEntrySpec(0, "deflate.txt", 512, "ae6477fb388db84c47bb3b49fad8193ca1450751beb74e7851ea026c07f9d216", AesWireMethod, AesWireMethod, 8, "deflate", Aes256Strength),
                        new AesZipFixtureEntrySpec(1, "empty.txt", 0, EmptySha256, AesWireMethod, AesWireMethod, 0, "stored", Aes256Strength),
                        new AesZipFixtureEntrySpec(2, "store.bin", 30, "ed92d79c44241004a0eff81deb76dbfb025728c8c875b676127e0482fc9cdc9d", AesWireMethod, AesWireMethod, 0, "stored", Aes256Strength),
                    ]),
                "UEsDBDMAAQBjAIade10AAAAAQgAAAAACAAALAAsAZGVmbGF0ZS50eHQBmQcAAgBBRQMIALwi9KMWZ1iqkoV7yET37SC8JP83riUHAQTBLp7K900qcSUHeXsWYS8gRyfmqss9OMNbrWID8MF1O6B8caNxTRharVBLAwQzAAEAYwCGnXtdAAAAABwAAAAAAAAACQALAGVtcHR5LnR4dAGZBwACAEFFAwAALAlzp1FwtLF/2wYoPk6RM6Ev6TtBa3x4GyGKoVBLAwQzAAEAYwCGnXtdAAAAADoAAAAeAAAACQALAHN0b3JlLmJpbgGZBwACAEFFAwAA5SG0SgtzQrA2sTcQ4G1Kuu94sZgLJhgX0nOgN+t5DQadyz5lhR1r585j66YIixd+sPgx2z4b3yuMslBLAQI/AzMAAQBjAIade10AAAAAQgAAAAACAAALAC8AAAAAAAAAIICkgQAAAABkZWZsYXRlLnR4dAoAIAAAAAAAAQAYAOAh1Eqfft0BAAAAAAAAAAAAAAAAAAAAAAGZBwACAEFFAwgAUEsBAj8DMwABAGMAhp17XQAAAAAcAAAAAAAAAAkALwAAAAAAAAAggKSBdgAAAGVtcHR5LnR4dAoAIAAAAAAAAQAYAG3t00qfft0BAAAAAAAAAAAAAAAAAAAAAAGZBwACAEFFAwAAUEsBAj8DMwABAGMAhp17XQAAAAA6AAAAHgAAAAkALwAAAAAAAAAggKSBxAAAAHN0b3JlLmJpbgoAIAAAAAAAAQAYACIc1Eqfft0BAAAAAAAAAAAAAAAAAAAAAAGZBwACAEFFAwAAUEsFBgAAAAADAAMANAEAADABAAAAAA=="),
            ["aes256-non-ascii"] = new(
                new AesZipFixtureManifest(
                    SchemaVersion: 1,
                    CaseKey: "aes256-non-ascii",
                    Container: ContainerFormat,
                    Producer: "ICSharpCode.SharpZipLib",
                    ProducerVersion: "1.4.2",
                    EncryptionStrength: Aes256Strength,
                    AesVendorVersion: AesVendorVersion,
                    SyntheticPasswordCase: "non-ascii-utf8",
                    RegenerationRecipe:
                        "ICSharpCode.SharpZipLib 1.4.2 ZipOutputStream with AESKeySize=256 and UTF-8 non-ASCII password " +
                        "(7-Zip 26.06 rejects non-ASCII passwords on ZIP creation with E_INVALIDARG, while extracting/verifying them cleanly).",
                    SaltReplayNote: SaltReplayExplanation,
                    Archive: new AesZipFixtureArchiveSpec(
                        "aes256-non-ascii.zip",
                        726,
                        "00dbf71ccc625df2fb12e5083abe30d2f54c8fec61ba4b1d93e9b5c3be25b802"),
                    Entries:
                    [
                        new AesZipFixtureEntrySpec(0, "empty.txt", 0, EmptySha256, AesWireMethod, AesWireMethod, 0, "stored", Aes256Strength),
                        new AesZipFixtureEntrySpec(1, "store.bin", 33, "93ec9d35d7f5237ab8e63cb559257a548feb9df299341db188a71bb5fcefff8d", AesWireMethod, AesWireMethod, 0, "stored", Aes256Strength),
                        new AesZipFixtureEntrySpec(2, "deflate.txt", 560, "bdd3fa9407b78dd821146fae859bdfba5cb023e4c8c932cca0afd74ca86d9f97", AesWireMethod, AesWireMethod, 8, "deflate", Aes256Strength),
                    ]),
                "UEsDBDMACQhjAAAAe10AAAAA//////////8JAB8AZW1wdHkudHh0AQAQAAAAAAAAAAAAHAAAAAAAAAABmQcAAgBBRQMAADk4teGucrM4vf4+MGtDC4QqDEi6wAxfpN6K6nhQSwcIAAAAABwAAAAAAAAAAAAAAAAAAABQSwMEMwAJCGMAAAB7XQAAAAD//////////wkAHwBzdG9yZS5iaW4BABAAIQAAAAAAAAA9AAAAAAAAAAGZBwACAEFFAwAAPw4m2XFwWRHa0penvwNvb3wowOEURgSHLyvanWRO+jVuVGTqbPwhzsxRH9KsEFkY7831aT2Z5SCHA5KtsFBLBwgAAAAAPQAAAAAAAAAhAAAAAAAAAFBLAwQzAAkIYwAAAHtdAAAAAP//////////CwAfAGRlZmxhdGUudHh0AQAQADACAAAAAAAARwAAAAAAAAABmQcAAgBBRQMIALlrdLk8Ap5LkGo9Cz7R+v54cUQxgiT7S7ylhS+f+Yr407bIRwMNccBSh3G8F+cesxNFlQGtz2PG+joVT0xvfLbTBMB+PQo3UEsHCAAAAABHAAAAAAAAADACAAAAAAAAUEsBAjMAMwAJCGMAAAB7XQAAAAD//////////wkAHwAAAAAAAAAAAAAAAAAAAGVtcHR5LnR4dAEAEAAAAAAAAAAAABwAAAAAAAAAAZkHAAIAQUUDAABQSwECMwAzAAkIYwAAAHtdAAAAAP//////////CQAfAAAAAAAAAAAAAAB6AAAAc3RvcmUuYmluAQAQACEAAAAAAAAAPQAAAAAAAAABmQcAAgBBRQMAAFBLAQIzADMACQhjAAAAe10AAAAA//////////8LAB8AAAAAAAAAAAAAABUBAABkZWZsYXRlLnR4dAEAEAAwAgAAAAAAAEcAAAAAAAAAAZkHAAIAQUUDCABQSwUGAAAAAAMAAwAEAQAAvAEAAAAA"),
        };

    internal static IReadOnlyList<string> AllCaseKeys { get; } =
    [
        "aes256-ascii-standard",
        "aes256-space-padded",
        "aes256-non-ascii",
    ];

    internal static AesZipFixtureManifest GetManifest(string caseKey)
    {
        if (!Definitions.TryGetValue(caseKey, out var definition))
        {
            throw new KeyNotFoundException(
                $"Unknown AES interoperability Case Key '{caseKey}'. Known keys: {string.Join(", ", AllCaseKeys)}.");
        }

        return definition.Manifest;
    }

    internal static byte[] GetArchiveBytes(string caseKey)
    {
        if (!Definitions.TryGetValue(caseKey, out var definition))
        {
            throw new KeyNotFoundException(
                $"Unknown AES interoperability Case Key '{caseKey}'. Known keys: {string.Join(", ", AllCaseKeys)}.");
        }

        var bytes = Convert.FromBase64String(definition.Base64ArchiveBytes);
        if (bytes.LongLength != definition.Manifest.Archive.PhysicalSize
            || bytes.LongLength > ArchiveTestCaseSemantics.MaxArchivePhysicalBytes)
        {
            throw new InvalidOperationException(
                $"Pinned AES fixture '{caseKey}' size {bytes.LongLength} disagrees with declared physicalSize {definition.Manifest.Archive.PhysicalSize}.");
        }

        var expandedTotal = definition.Manifest.Entries.Sum(entry => checked(entry.ContentSize));
        if (expandedTotal > ArchiveTestCaseSemantics.MaxExpandedBytesBudget)
        {
            throw new InvalidOperationException(
                $"Pinned AES fixture '{caseKey}' expanded size {expandedTotal} exceeds budget.");
        }

        var actualSha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actualSha, definition.Manifest.Archive.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Pinned AES fixture '{caseKey}' SHA-256 '{actualSha}' does not match '{definition.Manifest.Archive.Sha256}'.");
        }

        return bytes;
    }

    internal static async Task<AesZipInteroperabilityPublicationResult> PublishAsync(
        DirectoryInfo outputDirectory,
        IReadOnlyList<string>? caseKeys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        if (outputDirectory.Exists || File.Exists(outputDirectory.FullName))
        {
            throw new IOException($"Destination '{outputDirectory.FullName}' already exists.");
        }

        var selectedKeys = ResolveSelectedKeys(caseKeys);
        var parent = outputDirectory.Parent
            ?? throw new ArgumentException($"Output directory '{outputDirectory.FullName}' has no parent directory.");
        Directory.CreateDirectory(parent.FullName);

        var staging = Path.Combine(parent.FullName, string.Create(CultureInfo.InvariantCulture, $"aes-interop-staging-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(staging);

        try
        {
            long suiteBytes = 0;
            foreach (var caseKey in selectedKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifest = GetManifest(caseKey);
                var archiveBytes = GetArchiveBytes(caseKey);
                var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
                if (jsonBytes.Length > ArchiveTestRequest.MaxSidecarBytes)
                {
                    throw new InvalidOperationException(
                        $"AES manifest '{caseKey}' ({jsonBytes.Length} bytes) exceeds sidecar budget.");
                }

                suiteBytes = checked(suiteBytes + archiveBytes.LongLength + jsonBytes.LongLength);
                if (suiteBytes > ArchiveTestRequest.MaxSuiteBytes)
                {
                    throw new InvalidOperationException(
                        $"AES interoperability suite exceeds {ArchiveTestRequest.MaxSuiteBytes}-byte budget.");
                }

                var zipPath = Path.Combine(staging, manifest.Archive.FileName);
                await WriteExclusiveAsync(zipPath, archiveBytes, cancellationToken).ConfigureAwait(false);

                var jsonPath = Path.Combine(staging, caseKey + ".json");
                await WriteExclusiveAsync(jsonPath, jsonBytes, cancellationToken).ConfigureAwait(false);
            }

            if (Directory.Exists(outputDirectory.FullName) || File.Exists(outputDirectory.FullName))
            {
                throw new IOException(
                    $"Destination '{outputDirectory.FullName}' appeared during publication; staged files discarded.");
            }

            Directory.Move(staging, outputDirectory.FullName);
            return new AesZipInteroperabilityPublicationResult(selectedKeys, outputDirectory.FullName);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    private static IReadOnlyList<string> ResolveSelectedKeys(IReadOnlyList<string>? caseKeys)
    {
        if (caseKeys is null)
        {
            return AllCaseKeys;
        }

        if (caseKeys.Count == 0)
        {
            throw new ArgumentException("At least one AES interoperability Case Key must be specified.", nameof(caseKeys));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var resolved = new List<string>(caseKeys.Count);
        foreach (var key in caseKeys)
        {
            if (!Definitions.ContainsKey(key))
            {
                throw new KeyNotFoundException($"Unknown AES interoperability Case Key '{key}'.");
            }

            if (!seen.Add(key))
            {
                throw new ArgumentException($"Duplicate AES interoperability Case Key '{key}'.", nameof(caseKeys));
            }

            resolved.Add(key);
        }

        return resolved;
    }

    private static async Task WriteExclusiveAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Preserve the original failure or cancellation exception.
        }
    }
}

/// <summary>
/// Capability and content verifier for AES-256 ZIP interoperability fixtures (#1472).
/// Preserves the caller-supplied password verbatim (no trimming or normalization).
/// </summary>
internal static class AesZipInteroperabilityVerifier
{
    private const int StreamBufferSize = 8192;

    internal static AesZipReaderVerificationResult VerifyWithCapableReader(
        string archivePath,
        AesZipFixtureManifest manifest,
        string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(password);

        try
        {
            using var archive = new ICSharpCode.SharpZipLib.Zip.ZipFile(archivePath);
            // Pass the synthetic password verbatim without trimming or normalization.
            archive.Password = password;

            if (archive.Count != manifest.Entries.Count)
            {
                return new AesZipReaderVerificationResult(
                    "sharpziplib",
                    "failed",
                    MissingCapability: null,
                    string.Create(CultureInfo.InvariantCulture, $"Entry count {archive.Count} does not match expected {manifest.Entries.Count}."),
                    []);
            }

            var verified = new List<AesZipVerificationEntryResult>(manifest.Entries.Count);
            foreach (var expected in manifest.Entries)
            {
                var entry = archive[expected.Ordinal];
                if (!string.Equals(entry.Name, expected.Name, StringComparison.Ordinal))
                {
                    return new AesZipReaderVerificationResult(
                        "sharpziplib",
                        "failed",
                        MissingCapability: null,
                        $"Entry ordinal {expected.Ordinal} name '{entry.Name}' does not match expected '{expected.Name}'.",
                        []);
                }

                if (entry.AESKeySize != 256)
                {
                    return new AesZipReaderVerificationResult(
                        "sharpziplib",
                        "failed",
                        MissingCapability: null,
                        string.Create(CultureInfo.InvariantCulture, $"Entry '{entry.Name}' reported AES key size {entry.AESKeySize}, expected 256."),
                        []);
                }

                using var entryStream = archive.GetInputStream(entry);
                var (actualSize, actualSha) = HashStreamBounded(entryStream, expected.ContentSize);
                if (actualSize != expected.ContentSize
                    || !string.Equals(actualSha, expected.ContentSha256, StringComparison.Ordinal))
                {
                    return new AesZipReaderVerificationResult(
                        "sharpziplib",
                        "failed",
                        MissingCapability: null,
                        string.Create(CultureInfo.InvariantCulture, $"Content mismatch for '{entry.Name}': size {actualSize}/{expected.ContentSize}, sha {actualSha}."),
                        []);
                }

                verified.Add(new AesZipVerificationEntryResult(
                    entry.Name,
                    actualSize,
                    actualSha,
                    (int)entry.CompressionMethod,
                    AesZipInteroperabilityFixtures.AesWireMethod));
            }

            return new AesZipReaderVerificationResult(
                "sharpziplib",
                "verified",
                MissingCapability: null,
                "All AES-256 entries decrypted and matched expected SHA-256 hashes.",
                verified);
        }
        catch (ZipException ex)
        {
            return new AesZipReaderVerificationResult(
                "sharpziplib",
                string.Equals(ex.Message, "Invalid password for AES", StringComparison.Ordinal)
                    ? "incorrect-password-rejected"
                    : "failed",
                MissingCapability: null,
                ex.Message,
                []);
        }
    }

    internal static AesZipReaderVerificationResult CheckUnsupportedDotNetReader(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count == 0)
        {
            return new AesZipReaderVerificationResult(
                "dotnet-ziparchive",
                "failed",
                MissingCapability: null,
                "Archive contained no entries.",
                []);
        }

        try
        {
            using var entryStream = archive.Entries[0].Open();
            return new AesZipReaderVerificationResult(
                "dotnet-ziparchive",
                "failed",
                MissingCapability: null,
                "Expected System.IO.Compression.ZipArchive to reject AES-256 entry.",
                []);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            return new AesZipReaderVerificationResult(
                "dotnet-ziparchive",
                "unsupported-capability",
                MissingCapability: "aes-256",
                ex.Message,
                []);
        }
    }

    private static (long Size, string Sha256) HashStreamBounded(Stream stream, long expectedSize)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > expectedSize || total > ArchiveTestCaseSemantics.MaxExpandedBytesBudget)
                {
                    throw new InvalidDataException($"Decrypted stream exceeded expected size {expectedSize}.");
                }

                hasher.AppendData(buffer, 0, read);
            }

            return (total, Convert.ToHexStringLower(hasher.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
