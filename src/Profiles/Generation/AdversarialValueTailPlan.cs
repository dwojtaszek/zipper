using System.Text;
using Zipper.Utils;

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

    private readonly long recordCount;
    private readonly HashSet<long>? targetedRecords;
    private readonly bool targetAllRecords;
    private readonly Random? random;
    private readonly string escapedDelimiterValue;
    private readonly string platformLengthValue;
    private long remainingTargetCount;
    private bool emittedPlatformLengthValue;

    private const string QuoteLookalikeValue = "Quote lookalike: Ã¾";

    private AdversarialValueTailPlan(
        long recordCount,
        long targetCount,
        HashSet<long>? targetedRecords,
        bool targetAllRecords,
        Random? random,
        char columnDelimiter,
        char quoteDelimiter)
    {
        this.recordCount = recordCount;
        this.remainingTargetCount = targetCount;
        this.targetedRecords = targetedRecords;
        this.targetAllRecords = targetAllRecords;
        this.random = random;
        this.escapedDelimiterValue = BuildEscapedDelimiterValue(columnDelimiter, quoteDelimiter);
        string platformLengthPrefix = $"{this.escapedDelimiterValue} | {QuoteLookalikeValue} | ";
        this.platformLengthValue = platformLengthPrefix + new string('L', PlatformFieldLength - platformLengthPrefix.Length);
    }

    internal static AdversarialValueTailPlan? Create(FileGenerationRequest request, long recordCount)
    {
        if (!request.Chaos.ChaosMode || request.Chaos.ChaosScenario is not { } scenarioName)
        {
            return null;
        }

        var scenario = ChaosScenarios.GetByName(scenarioName);
        if (scenario?.GenerateAdversarialValues != true)
        {
            return null;
        }

        long targetCount = recordCount == 0
            ? 0
            : ChaosSampler.ParseChaosAmount(request.Chaos.ChaosAmount ?? scenario.DefaultAmount, recordCount);
        targetCount = Math.Min(targetCount, recordCount);
        HashSet<long>? selectedRecords = null;
        Random? selectionRandom = null;
        if (targetCount > 0 && targetCount < recordCount)
        {
            // PRNG is required for deterministic, seeded selection of simulated records.
#pragma warning disable S2245 // Seeded randomness selects mock-data records, not security decisions.
            var selectionGenerator = request.Metadata.Seed.HasValue
                ? new Random(request.Metadata.Seed.Value)
                : Random.Shared;
#pragma warning restore S2245
            if (recordCount <= int.MaxValue)
            {
                selectedRecords = ChaosSampler.SelectTargetLines((int)recordCount, (int)targetCount, selectionGenerator);
            }
            else
            {
                selectionRandom = selectionGenerator;
            }
        }

        return new AdversarialValueTailPlan(
            recordCount,
            targetCount,
            selectedRecords,
            recordCount > 0 && targetCount >= recordCount,
            selectionRandom,
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

        string escapedDelimiters = BuildEscapedDelimiterValue(columnDelimiter, quoteDelimiter);
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

    private static string BuildEscapedDelimiterValue(char columnDelimiter, char quoteDelimiter) =>
        $"Escaped delimiters: left{columnDelimiter}middle{quoteDelimiter}right";

    internal static bool IsEligibleColumn(ColumnDefinition column)
    {
        if (column.Type.ToLowerInvariant() is not ("text" or "longtext" or "legacyauthor" or "emailsubject" or "syntheticemailsubject"))
        {
            return false;
        }

        if (StandardRowValueGenerators.ByName.TryGetValue(column.Name, out var rowValueGenerator)
            && rowValueGenerator is not ProfileValueUnlessChildGenerator)
        {
            return false;
        }

        string upperName = column.Name.ToUpperInvariant();
        return Config.HashUtility.GetColumnAlgorithm(upperName) is null
            && upperName is not (
            "FILEPATH" or "FILE_PATH" or "FILE PATH" or
            "NATIVEPATH" or "NATIVE_PATH" or "NATIVE PATH" or
            "TEXTPATH" or "TEXT_PATH" or "TEXT PATH" or
            "FILENAME" or "FILEEXT" or "DOCID" or
            "CONTROLNUMBER" or "CONTROL_NUMBER" or "CONTROL NUMBER" or
            "BEGBATES" or "ENDBATES" or "BATES" or "BATESNUMBER");
    }

    internal static ColumnDefinition? FindColumn(
        IEnumerable<ColumnDefinition> columns,
        IEnumerable<string>? sourceMetadataKeys = null)
    {
        HashSet<string>? normalizedSourceMetadataKeys = null;
        if (sourceMetadataKeys is not null)
        {
            foreach (var key in sourceMetadataKeys)
            {
                (normalizedSourceMetadataKeys ??= new HashSet<string>(StringComparer.Ordinal)).Add(SourceMetadataKeyNormalizer.Normalize(key));
            }
        }

        bool IsAvailableColumn(ColumnDefinition column) =>
            IsEligibleColumn(column)
            && (normalizedSourceMetadataKeys is null || !normalizedSourceMetadataKeys.Contains(SourceMetadataKeyNormalizer.Normalize(column.Name)));

        return columns.FirstOrDefault(column =>
                   IsAvailableColumn(column)
                   && column.Name.Equals("AUTHOR", StringComparison.OrdinalIgnoreCase))
               ?? columns.FirstOrDefault(IsAvailableColumn);
    }

    internal string? GetValue(long recordIndex)
    {
        if (recordIndex < 1 || recordIndex > this.recordCount || this.remainingTargetCount == 0)
        {
            return null;
        }

        bool isTargeted = this.targetAllRecords;
        if (!isTargeted)
        {
            if (this.targetedRecords is not null)
            {
                isTargeted = this.targetedRecords.Contains(recordIndex);
            }
            else if (this.random is not null)
            {
                isTargeted = this.random.NextInt64(this.recordCount - recordIndex + 1) < this.remainingTargetCount;
            }
            else
            {
                throw new InvalidOperationException("The adversarial-value target plan has no record selector.");
            }
        }

        if (!isTargeted)
        {
            return null;
        }

        this.remainingTargetCount--;
        if (!this.emittedPlatformLengthValue)
        {
            this.emittedPlatformLengthValue = true;
            return this.platformLengthValue;
        }

        return recordIndex % 2 == 0 ? QuoteLookalikeValue : this.escapedDelimiterValue;
    }
}
