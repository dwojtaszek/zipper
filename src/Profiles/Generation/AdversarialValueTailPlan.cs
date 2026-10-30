using System.Text;

namespace Zipper.Profiles.Generation;

/// <summary>
/// Supplies deterministic valid DAT value tails for the adversarial-values scenario.
/// The platform-length tail is emitted once per Load File; other selected records cycle
/// escaped delimiters and quote lookalikes to keep large runs bounded.
/// </summary>
internal sealed class AdversarialValueTailPlan
{
    internal const int PlatformFieldLength = 32_768;
    internal const int TrailingEmptyRowCount = 2;

    private readonly HashSet<long>? targetedRecords;
    private readonly bool targetAllRecords;
    private readonly long firstTargetRecord;
    private readonly string escapedDelimiterValue;
    private readonly string platformLengthValue;

    private const string QuoteLookalikeValue = "Quote lookalike: Ã¾";

    private AdversarialValueTailPlan(
        HashSet<long>? targetedRecords,
        bool targetAllRecords,
        char columnDelimiter,
        char quoteDelimiter)
    {
        this.targetedRecords = targetedRecords;
        this.targetAllRecords = targetAllRecords;
        this.firstTargetRecord = targetAllRecords ? 1 : targetedRecords?.Min() ?? 0;
        this.escapedDelimiterValue = $"Escaped delimiters: left{columnDelimiter}middle{quoteDelimiter}right";
        string platformLengthPrefix = $"{this.escapedDelimiterValue} | {QuoteLookalikeValue} | ";
        this.platformLengthValue = platformLengthPrefix + new string('L', PlatformFieldLength - platformLengthPrefix.Length);
    }

    internal static AdversarialValueTailPlan? Create(FileGenerationRequest request)
    {
        if (!request.Chaos.ChaosMode || !ChaosScenarios.GeneratesAdversarialValues(request.Chaos.ChaosScenario))
        {
            return null;
        }

        var scenario = ChaosScenarios.GetByName(request.Chaos.ChaosScenario!)!;
        int recordCount = checked((int)request.Output.FileCount);
        int targetCount = recordCount == 0
            ? 0
            : ChaosSampler.ParseChaosAmount(request.Chaos.ChaosAmount ?? scenario.DefaultAmount, recordCount);
        bool allRecords = recordCount > 0 && targetCount == recordCount;
        HashSet<long>? selectedRecords = null;
        if (targetCount > 0 && !allRecords)
        {
            var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value) : Random.Shared;
            selectedRecords = ChaosSampler.SelectTargetLines(recordCount, targetCount, random);
        }

        return new AdversarialValueTailPlan(
            selectedRecords,
            allRecords,
            request.Delimiters.ColumnDelimiter[0],
            request.Delimiters.QuoteDelimiter[0]);
    }

    internal static bool HasValidDatConfiguration(Zipper.Config.DelimiterConfig delimiters, string encodingName)
    {
        if (delimiters.ColumnDelimiter.Length != 1
            || delimiters.QuoteDelimiter.Length != 1
            || delimiters.NewlineDelimiter.Length != 1)
        {
            return false;
        }

        char columnDelimiter = delimiters.ColumnDelimiter[0];
        char quoteDelimiter = delimiters.QuoteDelimiter[0];
        char newlineDelimiter = delimiters.NewlineDelimiter[0];
        if (columnDelimiter is '\0' or '\r' or '\n'
            || columnDelimiter == quoteDelimiter
            || quoteDelimiter is '\0' or '\r' or '\n'
            || newlineDelimiter is '\0' or '\r' or '\n'
            || newlineDelimiter == quoteDelimiter)
        {
            return false;
        }

        var encoding = EncodingHelper.GetEncoding(encodingName);
        if (encoding is null)
        {
            return false;
        }

        string escapedDelimiters = $"Escaped delimiters: left{columnDelimiter}middle{quoteDelimiter}right";
        return CanRoundTrip(encoding, $"{escapedDelimiters}{newlineDelimiter}")
            && CanRoundTrip(encoding, QuoteLookalikeValue)
            && HasParserSafeEncoderFallback(encoding, quoteDelimiter);
    }

    private static bool HasParserSafeEncoderFallback(Encoding encoding, char quoteDelimiter)
    {
        if (encoding.EncoderFallback is EncoderExceptionFallback)
        {
            return true;
        }

        if (encoding.EncoderFallback is not EncoderReplacementFallback replacementFallback)
        {
            return false;
        }

        string replacement = replacementFallback.DefaultString;
        return !replacement.Contains(quoteDelimiter, StringComparison.Ordinal)
            && !replacement.Contains('\r')
            && !replacement.Contains('\n');
    }

    private static bool CanRoundTrip(Encoding encoding, string value)
    {
        try
        {
            return string.Equals(encoding.GetString(encoding.GetBytes(value)), value, StringComparison.Ordinal);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    internal static bool IsEligibleColumn(ColumnDefinition column)
    {
        if (column.Type.ToLowerInvariant() is not ("text" or "longtext" or "legacyauthor" or "emailsubject" or "syntheticemailsubject"))
        {
            return false;
        }

        return column.Name.ToUpperInvariant() is not (
            "FILEPATH" or "FILE_PATH" or "FILE PATH" or
            "NATIVEPATH" or "NATIVE_PATH" or "NATIVE PATH" or
            "TEXTPATH" or "TEXT_PATH" or "TEXT PATH" or
            "FILENAME" or "FILEEXT" or "DOCID" or
            "CONTROLNUMBER" or "CONTROL_NUMBER" or "CONTROL NUMBER" or
            "BEGBATES" or "ENDBATES" or "BATES" or "BATESNUMBER" or
            "MD5HASH" or "MD5_HASH" or "MD5 HASH" or
            "SHA1HASH" or "SHA1_HASH" or "SHA1 HASH" or
            "SHA256HASH" or "SHA256_HASH" or "SHA256 HASH");
    }

    internal static ColumnDefinition? FindColumn(IEnumerable<ColumnDefinition> columns) =>
        columns.FirstOrDefault(column => IsEligibleColumn(column) && column.Name.Equals("AUTHOR", StringComparison.OrdinalIgnoreCase))
        ?? columns.FirstOrDefault(IsEligibleColumn);

    internal string? GetValue(long recordIndex)
    {
        if (recordIndex < 1
            || this.targetedRecords is not null && !this.targetedRecords.Contains(recordIndex)
            || this.targetedRecords is null && !this.targetAllRecords)
        {
            return null;
        }

        return recordIndex == this.firstTargetRecord
            ? this.platformLengthValue
            : recordIndex % 2 == 0 ? QuoteLookalikeValue : this.escapedDelimiterValue;
    }
}
