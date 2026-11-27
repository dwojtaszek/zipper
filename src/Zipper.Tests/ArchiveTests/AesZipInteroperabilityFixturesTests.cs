using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class AesZipInteroperabilityFixturesTests : WorkingDirectoryTestBase
{
    private static readonly IReadOnlyDictionary<string, string> SyntheticPasswords =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["aes256-ascii-standard"] = "zipper-aes-test-01",
            ["aes256-space-padded"] = "  zipper aes pass  ",
            ["aes256-non-ascii"] = "café-żółć-€",
        };

    [Fact]
    public void GetManifest_AllCases_RecordContainerProducerEncryptionAndEntryMethods()
    {
        Assert.Equal(
            ["aes256-ascii-standard", "aes256-space-padded", "aes256-non-ascii"],
            AesZipInteroperabilityFixtures.AllCaseKeys);

        foreach (var caseKey in AesZipInteroperabilityFixtures.AllCaseKeys)
        {
            var manifest = AesZipInteroperabilityFixtures.GetManifest(caseKey);
            Assert.Equal(1, manifest.SchemaVersion);
            Assert.Equal("zip", manifest.Container);
            Assert.Equal("AES-256", manifest.EncryptionStrength);
            Assert.Equal(2, manifest.AesVendorVersion);
            Assert.False(string.IsNullOrWhiteSpace(manifest.Producer));
            Assert.False(string.IsNullOrWhiteSpace(manifest.ProducerVersion));
            Assert.False(string.IsNullOrWhiteSpace(manifest.RegenerationRecipe));
            if (manifest.Producer == "7-Zip")
            {
                Assert.Contains(" && 7z ", manifest.RegenerationRecipe);
            }
            Assert.False(string.IsNullOrWhiteSpace(manifest.SaltReplayNote));

            Assert.Contains(manifest.Entries, entry => entry.ContentSize == 0 && entry.InnerCompressionMethod == 0);
            Assert.Contains(manifest.Entries, entry => entry.ContentSize > 0 && entry.InnerCompressionMethod == 0 && entry.InnerCompressionCodec == "stored");
            Assert.Contains(manifest.Entries, entry => entry.ContentSize > 0 && entry.InnerCompressionMethod == 8 && entry.InnerCompressionCodec == "deflate");
            Assert.All(manifest.Entries, entry =>
            {
                Assert.Equal(99, entry.LocalHeaderMethod);
                Assert.Equal(99, entry.CentralDirectoryMethod);
                Assert.Equal("AES-256", entry.EncryptionStrength);
            });
        }
    }

    [Fact]
    public async Task PublishAsync_TwoFreshDirectories_ProduceByteIdenticalEncryptedArchives()
    {
        var firstDir = Path.Combine(TempDir, "aes-replay-1");
        var secondDir = Path.Combine(TempDir, "aes-replay-2");

        var first = await AesZipInteroperabilityFixtures.PublishAsync(
            new DirectoryInfo(firstDir), caseKeys: null, CancellationToken.None);
        var second = await AesZipInteroperabilityFixtures.PublishAsync(
            new DirectoryInfo(secondDir), caseKeys: null, CancellationToken.None);

        Assert.Equal(AesZipInteroperabilityFixtures.AllCaseKeys, first.CaseKeys);
        Assert.Equal(first.CaseKeys, second.CaseKeys);

        foreach (var caseKey in first.CaseKeys)
        {
            var firstZip = await File.ReadAllBytesAsync(Path.Combine(firstDir, caseKey + ".zip"));
            var secondZip = await File.ReadAllBytesAsync(Path.Combine(secondDir, caseKey + ".zip"));
            Assert.Equal(firstZip, secondZip);

            var firstJson = await File.ReadAllBytesAsync(Path.Combine(firstDir, caseKey + ".json"));
            var secondJson = await File.ReadAllBytesAsync(Path.Combine(secondDir, caseKey + ".json"));
            Assert.Equal(firstJson, secondJson);
            Assert.Contains("\n  \"schemaVersion\"", System.Text.Encoding.UTF8.GetString(firstJson));
        }
    }

    [Fact]
    public async Task PublishAsync_ExistingTargetOrCancellation_PreservesExistingAndCleansStaging()
    {
        var existingDir = Path.Combine(TempDir, "existing-aes");
        Directory.CreateDirectory(existingDir);
        var sentinel = Path.Combine(existingDir, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "keep-me");

        await Assert.ThrowsAsync<IOException>(() =>
            AesZipInteroperabilityFixtures.PublishAsync(
                new DirectoryInfo(existingDir), caseKeys: null, CancellationToken.None));
        Assert.Equal("keep-me", await File.ReadAllTextAsync(sentinel));

        var cancelledDir = Path.Combine(TempDir, "cancelled-aes");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AesZipInteroperabilityFixtures.PublishAsync(
                new DirectoryInfo(cancelledDir), caseKeys: null, cts.Token));
        Assert.False(Directory.Exists(cancelledDir));
        Assert.Empty(Directory.GetDirectories(TempDir, "aes-interop-staging-*"));
    }

    [Theory]
    [InlineData("aes256-ascii-standard")]
    [InlineData("aes256-space-padded")]
    [InlineData("aes256-non-ascii")]
    public async Task VerifyWithCapableReader_ExactPassword_VerifiesAllEntriesAndHashes(string caseKey)
    {
        var outDir = Path.Combine(TempDir, "verify-" + caseKey);
        await AesZipInteroperabilityFixtures.PublishAsync(
            new DirectoryInfo(outDir), [caseKey], CancellationToken.None);

        var manifest = AesZipInteroperabilityFixtures.GetManifest(caseKey);
        var archivePath = Path.Combine(outDir, manifest.Archive.FileName);
        var archiveBytes = await File.ReadAllBytesAsync(archivePath);
        Assert.Equal(manifest.Archive.Sha256, Convert.ToHexStringLower(SHA256.HashData(archiveBytes)));

        var password = SyntheticPasswords[caseKey];
        var result = AesZipInteroperabilityVerifier.VerifyWithCapableReader(archivePath, manifest, password);

        Assert.Equal("sharpziplib", result.ReaderName);
        Assert.Equal("verified", result.Status);
        Assert.Null(result.MissingCapability);
        Assert.Equal(manifest.Entries.Count, result.VerifiedEntries.Count);

        foreach (var expected in manifest.Entries)
        {
            var actual = Assert.Single(result.VerifiedEntries, e => e.Name == expected.Name);
            Assert.Equal(expected.ContentSize, actual.ContentSize);
            Assert.Equal(expected.ContentSha256, actual.ContentSha256);
            Assert.Equal(expected.InnerCompressionMethod, actual.InnerCompressionMethod);
            Assert.Equal(99, actual.WireMethod);
        }

        // Optional E2E export hook mirroring ArchiveReferenceReaderTests
        var exportDir = Environment.GetEnvironmentVariable("ZIPPER_AES_INTEROP_DIR");
        if (!string.IsNullOrWhiteSpace(exportDir))
        {
            Directory.CreateDirectory(exportDir);
            await File.WriteAllBytesAsync(Path.Combine(exportDir, caseKey + ".zip"), archiveBytes);
            await File.WriteAllBytesAsync(
                Path.Combine(exportDir, caseKey + ".json"),
                await File.ReadAllBytesAsync(Path.Combine(outDir, caseKey + ".json")));
            var pwFile = Path.Combine(exportDir, "passwords.json");
            await File.WriteAllTextAsync(pwFile, JsonSerializer.Serialize(SyntheticPasswords));
        }
    }

    [Theory]
    [InlineData("aes256-ascii-standard", "wrong-password")]
    [InlineData("aes256-space-padded", "zipper aes pass")]
    [InlineData("aes256-space-padded", "wrong-password")]
    [InlineData("aes256-non-ascii", "cafe-zolc-eur")]
    public async Task VerifyWithCapableReader_IncorrectOrTrimmedPassword_ReportsCleanRejection(string caseKey, string wrongPassword)
    {
        ArgumentNullException.ThrowIfNull(wrongPassword);
        var outDir = Path.Combine(TempDir, $"reject-{caseKey}-{Guid.NewGuid():N}");
        await AesZipInteroperabilityFixtures.PublishAsync(
            new DirectoryInfo(outDir), [caseKey], CancellationToken.None);

        var manifest = AesZipInteroperabilityFixtures.GetManifest(caseKey);
        var archivePath = Path.Combine(outDir, manifest.Archive.FileName);
        var result = AesZipInteroperabilityVerifier.VerifyWithCapableReader(archivePath, manifest, wrongPassword);

        Assert.Equal("sharpziplib", result.ReaderName);
        Assert.Equal("incorrect-password-rejected", result.Status);
        Assert.Empty(result.VerifiedEntries);
    }

    [Fact]
    public async Task VerifyWithCapableReader_MalformedArchive_ReportsFailure()
    {
        const string caseKey = "aes256-ascii-standard";
        var archivePath = Path.Combine(TempDir, "malformed.zip");
        await File.WriteAllBytesAsync(archivePath, new byte[32]);

        var result = AesZipInteroperabilityVerifier.VerifyWithCapableReader(
            archivePath, AesZipInteroperabilityFixtures.GetManifest(caseKey), SyntheticPasswords[caseKey]);

        Assert.Equal("failed", result.Status);
        Assert.Empty(result.VerifiedEntries);
    }

    [Fact]
    public async Task PublishAsync_InvalidCaseSelection_ThrowsDescriptively()
    {
        var outDir = new DirectoryInfo(Path.Combine(TempDir, "invalid-selection"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            AesZipInteroperabilityFixtures.PublishAsync(outDir, Array.Empty<string>(), CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            AesZipInteroperabilityFixtures.PublishAsync(outDir, ["no-such-case"], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            AesZipInteroperabilityFixtures.PublishAsync(
                outDir, ["aes256-ascii-standard", "aes256-ascii-standard"], CancellationToken.None));
        Assert.Throws<KeyNotFoundException>(() => AesZipInteroperabilityFixtures.GetManifest("no-such-case"));
        Assert.Throws<KeyNotFoundException>(() => AesZipInteroperabilityFixtures.GetArchiveBytes("no-such-case"));
        Assert.False(outDir.Exists);
    }

    [Theory]
    [InlineData("aes256-ascii-standard")]
    [InlineData("aes256-space-padded")]
    [InlineData("aes256-non-ascii")]
    public async Task CheckUnsupportedDotNetReader_AllCases_ReportsMissingAesCapability(string caseKey)
    {
        var outDir = Path.Combine(TempDir, "unsupported-" + caseKey);
        await AesZipInteroperabilityFixtures.PublishAsync(
            new DirectoryInfo(outDir), [caseKey], CancellationToken.None);

        var archivePath = Path.Combine(outDir, caseKey + ".zip");
        var result = AesZipInteroperabilityVerifier.CheckUnsupportedDotNetReader(archivePath);

        Assert.Equal("dotnet-ziparchive", result.ReaderName);
        Assert.Equal("unsupported-capability", result.Status);
        Assert.Equal("aes-256", result.MissingCapability);
        Assert.Empty(result.VerifiedEntries);
    }

    [Fact]
    public async Task Cli_StandardGeneration_RejectsSyntheticPasswordFlag()
    {
        var outDir = Path.Combine(TempDir, "cli-out");
        var (exitCode, _, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(
            () => Program.Main(["--count", "1", "--output-path", outDir, "--password", "secret"]));

        Assert.Equal(1, exitCode);
        Assert.Contains("--password", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir));
    }
}
