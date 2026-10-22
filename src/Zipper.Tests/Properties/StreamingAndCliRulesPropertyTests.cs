using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FsCheck.Xunit;
using Xunit;
using Zipper.Cli;
using Zipper.Cli.Modules;
using Zipper.Config;
using Zipper.Emails;

namespace Zipper.Tests.Properties;

/// <summary>
/// Property-based tests for the streaming back-pressure window (<see cref="OrderingWindow"/>),
/// the disk-backed File Data spool (<see cref="DiskBackedFileDataList"/> and
/// <see cref="FileDataSerializer"/>), the hash helpers (<see cref="HashUtility"/>), the CLI
/// cross-cutting exclusivity rules (<see cref="CrossCuttingRules"/>), and the delimiter flag
/// contract (<see cref="DelimiterModule"/>).
/// </summary>
[Trait("Category", "Property")]
[Properties(Arbitrary = [typeof(ZipperArbitraries)], MaxTest = 100)]
[Collection("ConsoleTests")]
public sealed class StreamingAndCliRulesPropertyTests
{
    // ------------------------------------------------------------------
    // 1. OrderingWindow — admission frontier, commit order, cancellation
    // ------------------------------------------------------------------

    /// <summary>
    /// The Ordering Window admits exactly the items whose index is within the window of the
    /// last committed index: the item on the frontier is admitted immediately, the next one
    /// parks until a commit advances the frontier, and a stale commit never rewinds it.
    /// </summary>
    [Property]
    public void OrderingWindow_WaitForPermitAsync_ShouldAdmitOnlyItemsWithinTheWindowOfTheLastCommit(
        OrderingWindowPropertyInput input)
    {
        int windowSize = Math.Max(1, input.WindowSize);
        var window = new OrderingWindow(windowSize);

        // A fresh window has committed nothing, so the first window items are admitted.
        for (long item = 1; item <= windowSize; item++)
        {
            Assert.True(
                window.WaitForPermitAsync(item, CancellationToken.None).IsCompleted,
                $"Item {item} must be admitted inside the initial window of {windowSize}.");
        }

        long committed = 0;
        for (long item = 1; item <= Math.Max(0, input.CommitsBeforeProbe); item++)
        {
            window.OnItemCommitted(item);
            committed = item;

            Assert.True(
                window.WaitForPermitAsync(committed + windowSize, CancellationToken.None).IsCompleted,
                $"Item {committed + windowSize} must be admitted once {committed} items are committed.");
            Assert.False(
                window.WaitForPermitAsync(committed + windowSize + 1, CancellationToken.None).IsCompleted,
                $"Item {committed + windowSize + 1} must park until the frontier advances.");
        }

        // A waiter parked beyond the frontier is released by the next commit.
        var parked = window.WaitForPermitAsync(committed + windowSize + 1, CancellationToken.None);
        Assert.False(parked.IsCompleted);
        window.OnItemCommitted(committed + 1);
        Assert.True(parked.IsCompleted);

        // A stale commit never rewinds the frontier.
        long frontier = committed + 1;
        window.OnItemCommitted(frontier - 1);
        Assert.False(
            window.WaitForPermitAsync(frontier + windowSize + 1, CancellationToken.None).IsCompleted,
            "A stale commit must not move the admission frontier backwards.");
    }

    /// <summary>
    /// Items start in index order and complete in a generated permutation of that order, while
    /// the consumer commits the contiguous prefix: the commit sequence is strictly sequential
    /// and the number of in-flight items never exceeds the window size.
    /// </summary>
    [Property]
    public void OrderingWindow_OnItemCommitted_ShouldCommitTheContiguousPrefixAndBoundInFlightItems(
        OrderingWindowPropertyInput input)
    {
        int windowSize = Math.Max(1, input.WindowSize);
        int itemCount = Math.Max(1, input.ItemCount);
        int[] completionOrder = input.CompletionOrder is { Length: > 0 } order ? order : [1];

        var window = new OrderingWindow(windowSize);
        var started = new List<long>();
        var completed = new bool[itemCount + 1];
        long committed = 0;
        long nextToStart = 1;

        while (committed < itemCount)
        {
            while (nextToStart <= itemCount && nextToStart <= committed + windowSize)
            {
                var permit = window.WaitForPermitAsync(nextToStart, CancellationToken.None);
                Assert.True(
                    permit.IsCompleted,
                    $"Item {nextToStart} must be admitted while the frontier is {committed} and the window is {windowSize}.");
                started.Add(nextToStart);
                nextToStart++;
            }

            Assert.True(
                started.Count - committed <= windowSize,
                $"In-flight items ({started.Count - committed}) must never exceed the window size ({windowSize}).");

            long candidate = -1;
            foreach (var index in completionOrder)
            {
                if (index >= 1 && index <= itemCount && started.Contains(index) && !completed[index])
                {
                    candidate = index;
                    break;
                }
            }

            Assert.True(candidate > 0, "A started item must always be available to complete.");
            completed[candidate] = true;

            while (committed + 1 <= itemCount && completed[committed + 1])
            {
                committed++;
                window.OnItemCommitted(committed);
            }
        }

        Assert.Equal(itemCount, committed);
        Assert.Equal(itemCount, started.Count);
    }

    /// <summary>
    /// Cancelling the pipeline faults pending waiters with the supplied exception and cancels
    /// them when no exception is supplied, so a blocked producer never hangs.
    /// </summary>
    [Property]
    public void OrderingWindow_CancelAll_ShouldFaultOrCancelPendingWaiters(OrderingWindowPropertyInput input)
    {
        int windowSize = Math.Max(1, input.WindowSize);

        var faultingWindow = new OrderingWindow(windowSize);
        var faulted = faultingWindow.WaitForPermitAsync(windowSize + 1, CancellationToken.None);
        Assert.False(faulted.IsCompleted);

        var failure = new InvalidOperationException("pipeline fault");
        faultingWindow.CancelAll(failure);

        Assert.True(faulted.IsFaulted);
        Assert.Same(failure, faulted.Exception?.InnerException);

        var cancellingWindow = new OrderingWindow(windowSize);
        var cancelled = cancellingWindow.WaitForPermitAsync(windowSize + 1, CancellationToken.None);
        Assert.False(cancelled.IsCompleted);

        cancellingWindow.CancelAll();

        Assert.True(cancelled.IsCanceled);
    }

    // ------------------------------------------------------------------
    // 2. FileDataSerializer / DiskBackedFileDataList — metadata round-trip
    // ------------------------------------------------------------------

    /// <summary>
    /// The disk-backed spool keeps the metadata projection of every File Data record: the
    /// work item identity, lengths, hashes, attachment filename plus length, email header
    /// fields, identity overrides, and source metadata all survive the round-trip, while the
    /// heavy payload bytes are intentionally dropped.
    /// </summary>
    [Property]
    public void FileDataSerializer_SerializeAndDeserialize_ShouldRoundTripTheMetadataProjection(
        FileDataPropertyInput input)
    {
        var original = BuildFileData(input);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            FileDataSerializer.Serialize(writer, original);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        var restored = FileDataSerializer.Deserialize(reader);

        Assert.Equal(original.WorkItem.Index, restored.WorkItem.Index);
        Assert.Equal(original.WorkItem.FolderNumber, restored.WorkItem.FolderNumber);
        Assert.Equal(original.WorkItem.FolderName, restored.WorkItem.FolderName);
        Assert.Equal(original.WorkItem.FileName, restored.WorkItem.FileName);
        Assert.Equal(original.WorkItem.FilePathInZip, restored.WorkItem.FilePathInZip);
        Assert.Equal(original.WorkItem.FileType, restored.WorkItem.FileType);
        Assert.Equal(original.DataLength, restored.DataLength);
        Assert.Equal(original.PageCount, restored.PageCount);
        Assert.Equal(original.Hash, restored.Hash);

        // Identity overrides collapse empty strings to null on the way back.
        Assert.Equal(original.WorkItem.ControlNumberOverride, restored.WorkItem.ControlNumberOverride);
        Assert.Equal(original.WorkItem.BatesNumberOverride, restored.WorkItem.BatesNumberOverride);

        if (original.Hashes is null)
        {
            Assert.Null(restored.Hashes);
        }
        else
        {
            Assert.NotNull(restored.Hashes);
            Assert.Equal(original.Hashes.Count, restored.Hashes.Count);
            foreach (var kvp in original.Hashes)
            {
                Assert.True(restored.Hashes.TryGetValue(kvp.Key, out var restoredValue));
                Assert.Equal(kvp.Value, restoredValue);
            }
        }

        if (original.Attachment is null)
        {
            Assert.Null(restored.Attachment);
            Assert.Equal(0, restored.AttachmentLength);
        }
        else
        {
            Assert.NotNull(restored.Attachment);
            Assert.Equal(original.Attachment.Value.filename, restored.Attachment.Value.filename);
            Assert.Equal(original.AttachmentLength, restored.AttachmentLength);

            // The attachment payload is deliberately not spooled to disk.
            Assert.Empty(restored.Attachment.Value.content);
        }

        if (original.Email is null)
        {
            Assert.Null(restored.Email);
        }
        else
        {
            Assert.NotNull(restored.Email);
            Assert.Equal(original.Email.To, restored.Email.To);
            Assert.Equal(original.Email.From, restored.Email.From);
            Assert.Equal(original.Email.Subject, restored.Email.Subject);
            Assert.Equal(original.Email.SentDate.Ticks, restored.Email.SentDate.Ticks);
            Assert.Equal(DateTimeKind.Utc, restored.Email.SentDate.Kind);
            Assert.Equal(original.Email.Cc, restored.Email.Cc);
        }

        if (original.WorkItem.SourceMetadata is null)
        {
            Assert.Null(restored.WorkItem.SourceMetadata);
        }
        else
        {
            Assert.NotNull(restored.WorkItem.SourceMetadata);
            Assert.Equal(original.WorkItem.SourceMetadata.Count, restored.WorkItem.SourceMetadata.Count);
            foreach (var kvp in original.WorkItem.SourceMetadata)
            {
                Assert.True(restored.WorkItem.SourceMetadata.TryGetValue(kvp.Key, out var restoredValue));
                Assert.Equal(kvp.Value, restoredValue);
            }
        }
    }

    /// <summary>
    /// The disk-backed spool preserves insertion order and count across sequential
    /// enumeration, is re-readable, and refuses random access.
    /// </summary>
    [Property]
    public void DiskBackedFileDataList_AddAndEnumerate_ShouldPreserveInsertionOrderAndMetadata(
        FileDataPropertyInput input)
    {
        int itemCount = input.ItemCount;
        var expected = new List<FileData>(itemCount);
        using var list = new DiskBackedFileDataList();

        for (int i = 0; i < itemCount; i++)
        {
            var template = BuildFileData(input);
            var fileData = template with
            {
                WorkItem = template.WorkItem with { Index = i + 1 },
            };
            expected.Add(fileData);
            list.Add(fileData);
        }

        Assert.Equal(itemCount, list.Count);

        var first = list.ToList();
        var second = list.ToList();
        Assert.Equal(itemCount, first.Count);
        Assert.Equal(itemCount, second.Count);

        for (int i = 0; i < itemCount; i++)
        {
            Assert.Equal(expected[i].WorkItem.Index, first[i].WorkItem.Index);
            Assert.Equal(expected[i].WorkItem.FileName, first[i].WorkItem.FileName);
            Assert.Equal(expected[i].WorkItem.FilePathInZip, first[i].WorkItem.FilePathInZip);
            Assert.Equal(expected[i].DataLength, first[i].DataLength);
            Assert.Equal(expected[i].PageCount, first[i].PageCount);

            // Re-reading the spool yields the same projection.
            Assert.Equal(first[i].WorkItem.Index, second[i].WorkItem.Index);
            Assert.Equal(first[i].DataLength, second[i].DataLength);
        }

        Assert.Throws<NotSupportedException>(() => { _ = list[0]; });
    }

    // ------------------------------------------------------------------
    // 3. HashUtility — digest agreement and simulated hash shape
    // ------------------------------------------------------------------

    /// <summary>
    /// The computed hash is the lowercase hexadecimal digest of the payload for the requested
    /// algorithm, matching an independent incremental-hash computation.
    /// </summary>
    [Property]
    public void HashUtility_ComputeHashHex_ShouldMatchTheIncrementalHashDigest(HashPropertyInput input)
    {
        var expected = Convert.ToHexString(ComputeIncrementalHash(input.Algorithm, input.Data)).ToLowerInvariant();
        var actual = HashUtility.ComputeHashHex(input.Data, input.Algorithm);

        Assert.Equal(expected, actual);
        Assert.Equal(ComputeIncrementalHash(input.Algorithm, input.Data).Length * 2, actual.Length);
        Assert.All(actual, character => Assert.True(IsLowerHex(character), $"'{character}' is not a lowercase hex digit."));
    }

    /// <summary>
    /// A simulated hash is a lowercase hexadecimal string of the algorithm's digest length and
    /// is reproducible for the same seed.
    /// </summary>
    [Property]
    public void HashUtility_GenerateSimulatedHash_ShouldProduceADeterministicLowercaseHexString(
        HashPropertyInput input)
    {
        var first = HashUtility.GenerateSimulatedHash(input.Algorithm, new Random(input.Seed));
        var second = HashUtility.GenerateSimulatedHash(input.Algorithm, new Random(input.Seed));

        Assert.Equal(first, second);
        Assert.Equal(ComputeIncrementalHash(input.Algorithm, input.Data).Length * 2, first.Length);
        Assert.All(first, character => Assert.True(IsLowerHex(character), $"'{character}' is not a lowercase hex digit."));
    }

    // ------------------------------------------------------------------
    // 4. CrossCuttingRules — flag exclusivity
    // ------------------------------------------------------------------

    /// <summary>
    /// The cross-cutting rules reject a flag combination exactly when it breaks one of the
    /// documented exclusivity rules, and the reported error is one of the broken rules.
    /// </summary>
    [Property]
    public void CrossCuttingRules_Validate_ShouldRejectExactlyTheBrokenFlagCombinations(
        CliFlagCombinationPropertyInput input)
    {
        var modules = CliModules.Create();

        if (input.HasFileType)
        {
            modules.Output.TryApply("--type", "pdf");
        }

        if (input.HasFileTypes)
        {
            modules.Output.TryApply("--types", "pdf:70,xls:30");
        }

        if (input.HasCount)
        {
            modules.Output.TryApply("--count", "10");
        }

        if (input.LoadfileOnly)
        {
            modules.LoadFile.TryApply("--loadfile-only", null);
        }

        if (input.ProductionSet)
        {
            modules.Production.TryApply("--production-set", null);
        }

        if (input.RedactedProduction)
        {
            modules.Production.TryApply("--redacted-production", null);
        }

        if (input.HasBatesPrefix)
        {
            modules.Bates.TryApply("--bates-prefix", "PREFIX");
        }

        if (input.HasColumnProfile)
        {
            modules.Metadata.TryApply("--column-profile", "standard");
        }

        if (input.HasSourceInput)
        {
            modules.SourceInput.TryApply("--input-csv", "source-records.csv");
        }

        if (input.HasSourcePathMode)
        {
            modules.Production.TryApply("--source-path-mode", "preserve");
        }

        var violated = ComputeViolatedRules(input);
        var (isValid, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => CrossCuttingRules.Validate(modules));
        var reported = ExtractErrorMessages(stderr);

        Assert.Equal(violated.Count == 0, isValid);

        if (violated.Count == 0)
        {
            Assert.Empty(reported);
            return;
        }

        // CrossCuttingRules reports the first rule it breaks and stops, so the message it
        // prints must be the first violated rule in production's evaluation order.
        // ComputeViolatedRules lists the rules in that same order.
        Assert.Single(reported);
        Assert.Equal(violated[0], reported[0]);
    }

    // ------------------------------------------------------------------
    // 5. DelimiterModule — strict, legacy, and End-of-Line flag contracts
    // ------------------------------------------------------------------

    /// <summary>
    /// Strict delimiter flags accept only <c>ascii:&lt;N&gt;</c> and <c>char:&lt;c&gt;</c>
    /// values (plus the quote-only <c>none</c> keyword) and reject anything else with the
    /// flag-specific prefix error.
    /// </summary>
    [Property]
    public void DelimiterModule_TryBuild_StrictDelimiterValues_ShouldRejectValuesWithoutTheRequiredPrefix(
        StrictDelimiterPropertyInput input)
    {
        var flag = ZipperArbitraries.StrictDelimiterFlags[
            Math.Clamp(input.RoleIndex, 0, ZipperArbitraries.StrictDelimiterFlags.Count - 1)];
        string value = input.Value ?? string.Empty;

        var (built, stderr, config) = BuildDelimiter(flag, value, loadfileOnly: true, productionSet: false);

        bool accepted = IsAcceptedStrictDelimiter(flag, value);
        Assert.Equal(accepted, built);

        if (accepted)
        {
            Assert.NotNull(config);
            return;
        }

        string expectedError = flag == "--quote-delim"
            ? "Error: --quote-delim must use 'ascii:<N>', 'char:<c>', or 'none'."
            : $"Error: {flag} must use 'ascii:<N>' or 'char:<c>' prefix.";

        Assert.Contains(expectedError, stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Legacy delimiter flags accept every single-character value; only the column and quote
    /// delimiters reject a value that expands to more than one character.
    /// </summary>
    [Property]
    public void DelimiterModule_TryBuild_LegacyDelimiterValues_ShouldRejectOnlyMultiCharacterColumnAndQuote(
        LegacyDelimiterPropertyInput input)
    {
        var flag = ZipperArbitraries.LegacyDelimiterFlags[
            Math.Clamp(input.RoleIndex, 0, ZipperArbitraries.LegacyDelimiterFlags.Count - 1)];
        string value = input.Value ?? string.Empty;

        var (built, stderr, _) = BuildDelimiter(flag, value, loadfileOnly: true, productionSet: false);

        bool expandsToMultipleCharacters = LegacyDelimiterExpandsToMultipleCharacters(value);
        bool rejected = expandsToMultipleCharacters && flag != "--delimiter-newline";

        Assert.Equal(!rejected, built);

        if (rejected)
        {
            string expectedError = flag == "--delimiter-quote"
                ? "Error: Quote delimiter must be a single character or empty."
                : "Error: Column delimiter must be exactly one character.";
            Assert.Contains(expectedError, stderr, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The End-of-Line flag is only accepted with Load File Only or Production Set mode, and
    /// only accepts CRLF, LF, or CR.
    /// </summary>
    [Property]
    public void DelimiterModule_TryBuild_EolValues_ShouldRequireLoadFileOnlyOrProductionSet(
        EolDelimiterPropertyInput input)
    {
        var (built, stderr, config) = BuildDelimiter(
            "--eol",
            input.Eol ?? string.Empty,
            input.LoadfileOnly,
            input.ProductionSet);

        string eol = input.Eol ?? string.Empty;
        bool modeAllows = input.LoadfileOnly || input.ProductionSet;
        bool valueAllows = eol.Length == 0 || eol.ToUpperInvariant() is "CRLF" or "LF" or "CR";

        if (!modeAllows && eol.Length > 0)
        {
            Assert.False(built);
            Assert.Contains("Error: --eol requires --loadfile-only or --production-set.", stderr, StringComparison.Ordinal);
            return;
        }

        if (!valueAllows)
        {
            Assert.False(built);
            Assert.Contains("Error: --eol must be CRLF, LF, or CR.", stderr, StringComparison.Ordinal);
            return;
        }

        Assert.True(built);
        Assert.NotNull(config);

        // The raw flag value is stored verbatim; LoadFileEmitter normalizes the case.
        Assert.Equal(eol, config.EndOfLine);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Mutable holder so the built configuration can be captured out of the console-captured
    /// delegate without capturing a local as an <c>out</c> parameter.
    /// </summary>
    private sealed class DelimiterConfigHolder
    {
        public DelimiterConfig? Config;
    }

    /// <summary>
    /// Applies one delimiter flag to a fresh <see cref="DelimiterModule"/> and builds the
    /// configuration, capturing whatever the module reports on the error stream.
    /// </summary>
    private static (bool Built, string Stderr, DelimiterConfig? Config) BuildDelimiter(
        string flag,
        string value,
        bool loadfileOnly,
        bool productionSet)
    {
        var module = new DelimiterModule();
        Assert.True(module.TryApply(flag, value));

        var holder = new DelimiterConfigHolder();
        var (built, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(
            () => module.TryBuild(loadfileOnly, productionSet, out holder.Config));

        return (built, stderr, holder.Config);
    }

    private static FileData BuildFileData(FileDataPropertyInput input)
    {
        var hashes = input.HasHashes
            ? new Dictionary<Zipper.Config.HashAlgorithm, string> { [input.HashAlgorithm] = input.HashValue }
            : null;

        var attachment = input.HasAttachment
            ? ((string, byte[])?)(input.AttachmentFilename, new byte[input.AttachmentLength])
            : null;

        var email = input.HasEmail
            ? new Email
            {
                To = input.EmailRecipient,
                From = input.EmailSender,
                Subject = input.EmailSubject,
                SentDate = new DateTime(input.SentDateTicks, DateTimeKind.Utc),
                Cc = input.HasCc ? input.CcAddress : null,
            }
            : null;

        IReadOnlyDictionary<string, string>? sourceMetadata = null;
        if (input.HasSourceMetadata)
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < input.MetadataKeys.Length; i++)
            {
                metadata[input.MetadataKeys[i]] = input.MetadataValues[i];
            }

            sourceMetadata = metadata;
        }

        return new FileData
        {
            WorkItem = new FileWorkItem
            {
                Index = input.Index,
                FolderNumber = input.FolderNumber,
                FolderName = input.FolderName,
                FileName = input.FileName,
                FilePathInZip = input.FilePathInZip,
                FileType = input.FileType,
                ControlNumberOverride = input.ControlNumberOverride.Length > 0 ? input.ControlNumberOverride : null,
                BatesNumberOverride = input.BatesNumberOverride.Length > 0 ? input.BatesNumberOverride : null,
                SourceMetadata = sourceMetadata,
            },
            DataLength = input.DataLength,
            PageCount = input.PageCount,
            Hash = input.Hash,
            Hashes = hashes,
            Attachment = attachment,
            AttachmentLength = input.HasAttachment ? input.AttachmentLength : 0,
            Email = email,
        };
    }

    private static byte[] ComputeIncrementalHash(Zipper.Config.HashAlgorithm algorithm, byte[] data)
    {
        using var incremental = IncrementalHash.CreateHash(new HashAlgorithmName(algorithm.ToString()));
        incremental.AppendData(data);
        return incremental.GetHashAndReset();
    }

    private static bool IsLowerHex(char character)
        => (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');

    /// <summary>
    /// The rules <see cref="CrossCuttingRules"/> enforces, restated independently of the
    /// production implementation so the property compares two sources of truth.
    /// </summary>
    private static List<string> ComputeViolatedRules(CliFlagCombinationPropertyInput input)
    {
        var violated = new List<string>();

        if (!input.HasFileType && !input.HasFileTypes && !input.LoadfileOnly && !input.ProductionSet && !input.HasSourceInput)
        {
            violated.Add("Error: --type is required.");
        }

        if (!input.HasCount && !input.HasSourceInput)
        {
            violated.Add("Error: --count is required.");
        }

        if (input.ProductionSet && input.LoadfileOnly)
        {
            violated.Add("Error: --production-set conflicts with --loadfile-only.");
        }

        if (input.ProductionSet && !input.HasBatesPrefix)
        {
            violated.Add("Error: --production-set requires --bates-prefix.");
        }

        if (input.RedactedProduction && input.LoadfileOnly)
        {
            violated.Add("Error: --redacted-production conflicts with --loadfile-only.");
        }

        if (input.HasFileTypes && input.LoadfileOnly)
        {
            violated.Add("Error: --types is not supported with --loadfile-only.");
        }

        if (input.HasFileTypes && input.HasColumnProfile)
        {
            violated.Add("Error: --types is not supported with --column-profile. Use --type for profile-driven generation.");
        }

        if (input.HasSourcePathMode && !input.ProductionSet)
        {
            violated.Add("Error: --source-path-mode requires --production-set.");
        }

        if (input.HasSourcePathMode && !input.HasSourceInput)
        {
            violated.Add("Error: --source-path-mode requires --input-csv or --directory-template.");
        }

        if (input.HasSourceInput && input.HasFileType)
        {
            violated.Add("Error: --type cannot be used with --input-csv/--directory-template. File Types come from the Source Records.");
        }

        if (input.HasSourceInput && input.HasFileTypes)
        {
            violated.Add("Error: --types cannot be used with --input-csv/--directory-template. File Types come from the Source Records.");
        }

        if (input.HasColumnProfile && input.ProductionSet)
        {
            violated.Add("Error: --column-profile is not supported with --production-set.");
        }

        return violated;
    }

    private static List<string> ExtractErrorMessages(string stderr)
        => stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim('\r', ' '))
            .Where(line => line.StartsWith("Error: ", StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// Whether a strict-prefix delimiter value is accepted for the given flag. The quote
    /// delimiter additionally accepts the <c>none</c> keyword.
    /// </summary>
    private static bool IsAcceptedStrictDelimiter(string flag, string value)
    {
        if (value.Length == 0)
        {
            return true;
        }

        if (flag == "--quote-delim" && value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.StartsWith("ascii:", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(value.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
                && code is >= 0 and <= 255;
        }

        if (value.StartsWith("char:", StringComparison.OrdinalIgnoreCase))
        {
            return value.Length >= 6;
        }

        return false;
    }

    /// <summary>
    /// Whether a legacy delimiter value expands to more than one character. Only the CRLF
    /// escape expands to two characters; every other value keeps its first character.
    /// </summary>
    private static bool LegacyDelimiterExpandsToMultipleCharacters(string value)
        => value.Length > 0 && value == "\\r\\n";
}
