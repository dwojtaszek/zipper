using System.Text;
using System.Text.Json;

namespace Zipper.ArchiveTests;

/// <summary>
/// Deterministic Expectation File serialization (REQ-217): UTF-8 without BOM, compact output,
/// stable property and array ordering (record declaration order), and exactly one terminal
/// LF per document. No wall-clock time or absolute paths are serialized. Culture-independent:
/// all numbers are written invariantly by System.Text.Json.
/// </summary>
internal static class ArchiveTestJson
{
    /// <summary>Policy: every serialized Expectation File ends with exactly one LF.</summary>
    internal const string TerminalNewline = "\n";

    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
    };

    internal static byte[] SerializeToUtf8Bytes(ArchiveTestCase testCase)
    {
        var json = JsonSerializer.Serialize(testCase, Options);
        return Encoding.UTF8.GetBytes(json + TerminalNewline);
    }

    /// <summary>
    /// Parses an Expectation File. Enforces the terminal-newline policy (exactly one trailing
    /// LF, no leading LF) and rejects documents with missing or null sections instead of
    /// letting nulls flow into semantic validation.
    /// </summary>
    internal static ArchiveTestCase Parse(ReadOnlySpan<byte> utf8Bytes)
    {
        if (utf8Bytes.Length == 0 || utf8Bytes[0] == (byte)'\n')
        {
            throw new JsonException("Expectation File must not start with a line feed.");
        }

        if (utf8Bytes[^1] != (byte)'\n')
        {
            throw new JsonException("Expectation File must end with exactly one terminal line feed.");
        }

        if (utf8Bytes[^2] == (byte)'\n')
        {
            throw new JsonException("Expectation File must not end with multiple line feeds.");
        }

        var testCase = JsonSerializer.Deserialize<ArchiveTestCase>(utf8Bytes[..^1], Options)
            ?? throw new JsonException("Expectation File deserialized to null.");

        if (testCase.Archive is null || testCase.Entries is null || testCase.Mutations is null
            || testCase.Expectations is null || testCase.Limits is null)
        {
            throw new JsonException("Expectation File is missing a required section (archive, entries, mutations, expectations, limits).");
        }

        return testCase;
    }
}
