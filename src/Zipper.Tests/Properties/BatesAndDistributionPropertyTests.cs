using System.Globalization;
using FsCheck.Xunit;
using Xunit;
using Zipper.Config;
using Zipper.ManifestComparison;

namespace Zipper.Tests.Properties;

[Trait("Category", "Property")]
[Properties(Arbitrary = [typeof(ZipperArbitraries)], MaxTest = 100)]
public sealed class BatesAndDistributionPropertyTests
{
    private static readonly string[] OrderedMixTypes = ["pdf", "jpg", "eml", "tiff"];

    [Property]
    public void FormatPageBates_ForEveryDocumentAndPage_ShouldStripBackToItsDocumentAndStayUnique(
        BatesPropertyInput input,
        byte pageSeed)
    {
        var suffixed = BatesSequence.FromConfig(new BatesNumberConfig
        {
            Prefix = input.Prefix,
            Start = input.Start,
            Digits = input.Digits,
            PageSuffix = true,
        });
        var unsuffixed = BatesSequence.FromConfig(new BatesNumberConfig
        {
            Prefix = input.Prefix,
            Start = input.Start,
            Digits = input.Digits,
        });

        var pageKeys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < input.Count; i++)
        {
            var document = suffixed.Format(i).ToString();
            Assert.Equal(unsuffixed.Format(i).ToString(), document);

            var pageCount = 1 + ((i + pageSeed) % 4);
            for (int page = 1; page <= pageCount; page++)
            {
                var pageBates = BatesSequence.FormatPageBates(document, page);

                Assert.Equal(document, pageBates[..^4]);
                Assert.Matches(@"^.+_\d{3}$", pageBates);
                Assert.True(pageKeys.Add(pageBates), $"Page Bates '{pageBates}' is not unique across documents and pages.");
            }
        }
    }

    [Property]
    public void BatesSequence_NextAndFormat_ShouldBeStrictlyMonotonicAndPreservePrefix(
        BatesPropertyInput input,
        byte rawIncrement)
    {
        var increment = (rawIncrement % 10) + 1L;
        var config = new BatesNumberConfig
        {
            Prefix = input.Prefix,
            Start = input.Start,
            Digits = input.Digits,
            Increment = increment,
        };

        var created = BatesSequence.TryCreate(config, out var sequence, out var errorMessage);
        Assert.True(created, errorMessage);
        Assert.Equal(string.Empty, errorMessage);

        long previousNumeric = -1;
        for (int i = 0; i < input.Count; i++)
        {
            var next = sequence.Next();
            var formattedAtIndex = sequence.Format(i);

            Assert.Equal(formattedAtIndex, next);
            Assert.Equal(input.Prefix, next.Prefix);
            Assert.StartsWith(input.Prefix, next.ToString(), StringComparison.Ordinal);
            Assert.Equal($"{input.Prefix}{next.WithoutPrefix()}", next.ToString());
            Assert.True(next.WithoutPrefix().Length >= input.Digits);

            var numericValue = long.Parse(next.WithoutPrefix(), CultureInfo.InvariantCulture);
            var expectedNumeric = input.Start + (i * increment);
            Assert.Equal(expectedNumeric, numericValue);

            if (i > 0)
            {
                Assert.True(numericValue > previousNumeric);
            }

            previousNumeric = numericValue;
        }
    }

    [Property]
    public void BatesSequence_GetRange_ShouldMatchFirstAndLastGeneratedBatesNumbers(BatesPropertyInput input)
    {
        var config = new BatesNumberConfig
        {
            Prefix = input.Prefix,
            Start = input.Start,
            Digits = input.Digits,
            Increment = 1,
        };

        var sequence = BatesSequence.FromConfig(config);
        var generated = new List<string>(input.Count);
        for (int i = 0; i < input.Count; i++)
        {
            generated.Add(sequence.Next().ToString());
        }

        Assert.Equal(sequence.Format(0).ToString(), generated[0]);
        Assert.Equal(sequence.Format(input.Count - 1).ToString(), generated[^1]);

        var merged = BatesRangeAnalyzer.MergeConsecutiveBates(generated);
        var singleRange = Assert.Single(merged);
        Assert.Equal(generated[0], singleRange.Start);
        Assert.Equal(generated[^1], singleRange.End);

        var gaps = BatesRangeAnalyzer.FindGapsInSequence(generated);
        Assert.Empty(gaps);

        // Reflexive overlap: comparing a sequence with itself yields the full range as overlap.
        var recordsA = generated.Select(b => new ComparisonRecord { BatesNumber = b }).ToList();
        var reflexiveAnalysis = new BatesAnalysis();
        BatesRangeAnalyzer.AnalyzeBatesRanges(recordsA, recordsA, "full", reflexiveAnalysis, new ResultDetails());
        var reflexiveOverlap = Assert.Single(reflexiveAnalysis.Overlaps);
        Assert.Equal(generated[0], reflexiveOverlap.Start);
        Assert.Equal(generated[^1], reflexiveOverlap.End);

        // Symmetric overlap: comparing a subrange against the full range in either order yields the same overlap.
        var subCount = Math.Max(1, input.Count / 2);
        var subRecords = recordsA.Take(subCount).ToList();
        var forwardAnalysis = new BatesAnalysis();
        var reverseAnalysis = new BatesAnalysis();
        BatesRangeAnalyzer.AnalyzeBatesRanges(recordsA, subRecords, "full", forwardAnalysis, new ResultDetails());
        BatesRangeAnalyzer.AnalyzeBatesRanges(subRecords, recordsA, "full", reverseAnalysis, new ResultDetails());
        Assert.Equal(forwardAnalysis.Overlaps.Count, reverseAnalysis.Overlaps.Count);
        Assert.Equal(forwardAnalysis.Overlaps[0].Start, reverseAnalysis.Overlaps[0].Start);
        Assert.Equal(forwardAnalysis.Overlaps[0].End, reverseAnalysis.Overlaps[0].End);

        // Disjoint shifted range: a sequence starting after a 5-number gap has no overlaps and reports 5 skipped Bates numbers.
        const int gapSize = 5;
        var shiftedSequence = BatesSequence.FromConfig(config with { Start = input.Start + input.Count + gapSize });
        var shiftedRecords = new List<ComparisonRecord>(input.Count);
        for (int i = 0; i < input.Count; i++)
        {
            shiftedRecords.Add(new ComparisonRecord { BatesNumber = shiftedSequence.Next().ToString() });
        }

        var disjointAnalysis = new BatesAnalysis();
        BatesRangeAnalyzer.AnalyzeBatesRanges(recordsA, shiftedRecords, "supplemental", disjointAnalysis, new ResultDetails());
        Assert.Empty(disjointAnalysis.Overlaps);
        var boundaryGap = Assert.Single(disjointAnalysis.Gaps);
        var shiftedStartValue = input.Start + input.Count + gapSize;
        var effectiveDigits = Math.Max(input.Digits, shiftedStartValue.ToString(CultureInfo.InvariantCulture).Length);
        Assert.Equal(BatesRangeAnalyzer.FormatBates(input.Start + input.Count, input.Prefix, effectiveDigits), boundaryGap.Start);
        Assert.Equal(BatesRangeAnalyzer.FormatBates((input.Start + input.Count + gapSize) - 1, input.Prefix, effectiveDigits), boundaryGap.End);
        Assert.Equal(gapSize, disjointAnalysis.TotalSkippedBates);
    }

    [Property]
    public void BatesNumber_ParseAndFormat_ShouldRoundTrip(BatesPropertyInput input)
    {
        var nonNumericEndedPrefix = input.Prefix.EndsWith('_') || input.Prefix.EndsWith('-')
            ? input.Prefix
            : $"{input.Prefix}_";

        var formatted = BatesRangeAnalyzer.FormatBates(input.Start, nonNumericEndedPrefix, input.Digits);
        var parsed = BatesRangeAnalyzer.TryParseBates(formatted, out var parsedPrefix, out var parsedNumber, out var parsedDigits);

        Assert.True(parsed);
        Assert.Equal(nonNumericEndedPrefix, parsedPrefix);
        Assert.Equal(input.Start, parsedNumber);

        var expectedWidth = Math.Max(input.Digits, input.Start.ToString(CultureInfo.InvariantCulture).Length);
        Assert.Equal(expectedWidth, parsedDigits);

        var reFormatted = BatesRangeAnalyzer.FormatBates(parsedNumber, parsedPrefix, input.Digits);
        Assert.Equal(formatted, reFormatted);

        var sequence = BatesSequence.FromConfig(new BatesNumberConfig
        {
            Prefix = nonNumericEndedPrefix,
            Start = input.Start,
            Digits = input.Digits,
            Increment = 1,
        });
        var batesNumber = sequence.Format(0);
        Assert.Equal(formatted, batesNumber.ToString());
        Assert.Equal(nonNumericEndedPrefix, batesNumber.Prefix);
        Assert.Equal(formatted[nonNumericEndedPrefix.Length..], batesNumber.WithoutPrefix());
    }

    [Property]
    public void BatesSequence_TryCreate_ShouldValidateConfigBoundaries(BatesPropertyInput input)
    {
        var validConfig = new BatesNumberConfig
        {
            Prefix = input.Prefix,
            Start = input.Start,
            Digits = input.Digits,
            Increment = 1,
        };
        Assert.True(BatesSequence.TryCreate(validConfig, out _, out var validError));
        Assert.Equal(string.Empty, validError);

        Assert.False(BatesSequence.TryCreate(validConfig with { Start = -input.Start - 1 }, out _, out var negStartErr));
        Assert.NotEmpty(negStartErr);

        Assert.False(BatesSequence.TryCreate(validConfig with { Digits = 1 - input.Digits }, out _, out var nonPositiveDigitsErr));
        Assert.NotEmpty(nonPositiveDigitsErr);

        Assert.False(BatesSequence.TryCreate(validConfig with { Digits = 20 + input.Digits }, out _, out var highDigitsErr));
        Assert.NotEmpty(highDigitsErr);

        Assert.False(BatesSequence.TryCreate(validConfig with { Increment = 1L - input.Count }, out _, out var nonPositiveIncErr));
        Assert.NotEmpty(nonPositiveIncErr);

        Assert.False(BatesSequence.TryCreate(validConfig with { Prefix = $"{input.Prefix}/bad" }, out _, out var slashErr));
        Assert.NotEmpty(slashErr);
    }

    [Property]
    public void Distributions_Proportional_ShouldStayWithinFolderBoundsAndConserveCount(DistributionPropertyInput input)
    {
        Assert.Equal(1, Distributions.Proportional(1, 1));
        Assert.Equal(1, Distributions.Proportional(input.TotalFiles, 1));

        var folderCounts = new int[input.FolderCount];

        for (int fileIndex = 1; fileIndex <= input.TotalFiles; fileIndex++)
        {
            var folder = Distributions.Proportional(fileIndex, input.FolderCount);
            var dispatched = Distributions.GetFolderNumber(
                fileIndex,
                input.TotalFiles,
                input.FolderCount,
                DistributionType.Proportional);

            Assert.Equal(folder, dispatched);
            Assert.InRange(folder, 1, input.FolderCount);
            folderCounts[folder - 1]++;
        }

        Assert.Equal(input.TotalFiles, folderCounts.Sum());
        var maxCount = folderCounts.Max();
        var minCount = folderCounts.Min();
        Assert.True((maxCount - minCount) <= 1);
    }

    [Property]
    public void Distributions_GaussianAndExponential_ShouldBeDeterministicAndWithinBounds(DistributionPropertyInput input)
    {
        Assert.InRange(Distributions.Gaussian(1, 1, input.FolderCount), 1, input.FolderCount);
        Assert.Equal(1, Distributions.Gaussian(1, input.TotalFiles, 1));
        Assert.Equal(1, Distributions.Exponential(1, 1, input.FolderCount));
        Assert.Equal(1, Distributions.Exponential(1, input.TotalFiles, 1));

        int previousGaussian = 1;
        int previousExponential = 1;

        for (int fileIndex = 1; fileIndex <= input.TotalFiles; fileIndex++)
        {
            var gaussian = Distributions.Gaussian(fileIndex, input.TotalFiles, input.FolderCount);
            var gaussianDispatched = Distributions.GetFolderNumber(
                fileIndex,
                input.TotalFiles,
                input.FolderCount,
                DistributionType.Gaussian);

            var exponential = Distributions.Exponential(fileIndex, input.TotalFiles, input.FolderCount);
            var exponentialDispatched = Distributions.GetFolderNumber(
                fileIndex,
                input.TotalFiles,
                input.FolderCount,
                DistributionType.Exponential);

            Assert.Equal(gaussian, gaussianDispatched);
            Assert.Equal(exponential, exponentialDispatched);
            Assert.InRange(gaussian, 1, input.FolderCount);
            Assert.InRange(exponential, 1, input.FolderCount);

            Assert.True(gaussian >= previousGaussian);
            Assert.True(exponential >= previousExponential);

            previousGaussian = gaussian;
            previousExponential = exponential;
        }
    }

    [Property]
    public void FileTypePlan_Build_ShouldConserveTotalFilesAndAssignContiguousRanges(
        DistributionPropertyInput input,
        byte rawWeight1,
        byte rawWeight2,
        byte rawWeight3,
        byte rawWeight4)
    {
        var ratios = new FileTypeRatio[]
        {
            new() { Type = "pdf", Weight = rawWeight1 + 1L },
            new() { Type = "jpg", Weight = rawWeight2 + 1L },
            new() { Type = "eml", Weight = rawWeight3 + 1L },
            new() { Type = "tiff", Weight = rawWeight4 + 1L },
        };

        var plan = new FileTypePlan(ratios, input.TotalFiles);
        Assert.Equal(ratios.Select(r => r.Type), plan.Types);

        long totalWeight = ratios.Sum(r => r.Weight);
        long sumAllocated = 0;

        foreach (var ratio in ratios)
        {
            Assert.True(plan.ContainsType(ratio.Type));
            var count = plan.GetTypeCount(ratio.Type);
            sumAllocated += count;

            double exactQuota = (double)(input.TotalFiles * ratio.Weight) / totalWeight;
            Assert.True(Math.Abs(count - exactQuota) < 1.0);
        }

        Assert.Equal(input.TotalFiles, sumAllocated);
        Assert.False(plan.ContainsType("unknown"));
        Assert.Equal(0, plan.GetTypeCount("unknown"));

        var observedCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        int currentTypeIndex = 0;

        for (long index = 1; index <= input.TotalFiles; index++)
        {
            var assignedType = plan.GetFileType(index);
            Assert.True(FileGeneratorFactory.IsKnownType(assignedType));

            observedCounts.TryGetValue(assignedType, out var existing);
            observedCounts[assignedType] = existing + 1;

            var rank = Array.IndexOf(OrderedMixTypes, assignedType);
            Assert.True(rank >= currentTypeIndex);
            currentTypeIndex = rank;
        }

        foreach (var ratio in ratios)
        {
            observedCounts.TryGetValue(ratio.Type, out var observed);
            Assert.Equal(plan.GetTypeCount(ratio.Type), observed);
        }

        // Single-type plan allocates 100% of files to that type and enforces index bounds.
        var singleTypePlan = new FileTypePlan([new FileTypeRatio { Type = "pdf", Weight = rawWeight1 + 1L }], input.TotalFiles);
        Assert.Equal(input.TotalFiles, singleTypePlan.GetTypeCount("pdf"));
        Assert.Equal("pdf", singleTypePlan.GetFileType(1));
        Assert.Equal("pdf", singleTypePlan.GetFileType(input.TotalFiles));
        Assert.Throws<ArgumentOutOfRangeException>(() => singleTypePlan.GetFileType(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => singleTypePlan.GetFileType(input.TotalFiles + 1L));
    }

    [Property]
    public void FileTypeRatioParser_Parse_ShouldRoundTripFormattedSpec(
        byte rawW1,
        byte rawW2,
        byte rawW3,
        byte rawW4)
    {
        long w1 = (rawW1 * 100L) + 1L;
        long w2 = (rawW2 * 100L) + 2L;
        long w3 = (rawW3 * 100L) + 3L;
        long w4 = (rawW4 * 100L) + 4L;

        var spec = string.Create(
            CultureInfo.InvariantCulture,
            $"pdf:{w1},jpg:{w2},eml:{w3},docx:{w4}");

        var ok = FileTypeRatioParser.TryParse(spec, out var ratios, out var error);
        Assert.True(ok, error);
        Assert.Null(error);
        Assert.Equal(4, ratios.Count);

        Assert.Equal("pdf", ratios[0].Type);
        Assert.Equal(w1, ratios[0].Weight);
        Assert.Equal("jpg", ratios[1].Type);
        Assert.Equal(w2, ratios[1].Weight);
        Assert.Equal("eml", ratios[2].Type);
        Assert.Equal(w3, ratios[2].Weight);
        Assert.Equal("docx", ratios[3].Type);
        Assert.Equal(w4, ratios[3].Weight);

        var spacedUpperSpec = string.Create(
            CultureInfo.InvariantCulture,
            $" PDF : {w1} , JPG : {w2} , EML : {w3} , DOCX : {w4} ");
        Assert.True(FileTypeRatioParser.TryParse(spacedUpperSpec, out var normalizedRatios, out var normalizedError));
        Assert.Null(normalizedError);
        Assert.Equal(ratios, normalizedRatios);
    }

    [Property]
    public void FileTypeRatioParser_TryParse_ShouldRejectInvalidSpecs(byte rawWeight)
    {
        long validWeight = rawWeight + 1L;

        var duplicateSpec = string.Create(CultureInfo.InvariantCulture, $"pdf:{validWeight},PDF:{validWeight}");
        Assert.False(FileTypeRatioParser.TryParse(duplicateSpec, out _, out var dupError));
        Assert.NotNull(dupError);

        var overMaxSpec = string.Create(
            CultureInfo.InvariantCulture,
            $"pdf:{FileTypeRatioParser.MaxWeight + validWeight}");
        Assert.False(FileTypeRatioParser.TryParse(overMaxSpec, out _, out var maxError));
        Assert.NotNull(maxError);

        var negativeSpec = string.Create(CultureInfo.InvariantCulture, $"pdf:-{validWeight}");
        Assert.False(FileTypeRatioParser.TryParse(negativeSpec, out _, out var negError));
        Assert.NotNull(negError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pdf")]
    [InlineData("pdf:0")]
    [InlineData("pdf:abc")]
    [InlineData("unknown:10")]
    [InlineData("pdf:10,")]
    public void FileTypeRatioParser_TryParse_ShouldRejectConstantInvalidSpecs(string invalidSpec)
    {
        Assert.False(FileTypeRatioParser.TryParse(invalidSpec, out var ratios, out var error));
        Assert.Empty(ratios);
        Assert.NotNull(error);
    }

    [Property]
    public void ProductionSetPlanner_Plan_ShouldRespectVolumeSizeAndContiguousBatesRanges(
        BatesPropertyInput batesInput,
        byte rawDocCount,
        byte rawVolumeSize)
    {
        int docCount = (rawDocCount % 120) + 1;
        int volumeSize = (rawVolumeSize % 50) + 1;

        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileCount = docCount,
                FileType = "pdf",
            },
            Bates = new BatesNumberConfig
            {
                Prefix = batesInput.Prefix,
                Start = batesInput.Start,
                Digits = batesInput.Digits,
                Increment = 1,
            },
            Production = new ProductionConfig
            {
                ProductionSet = true,
                VolumeSize = volumeSize,
                RedactedProduction = (rawDocCount % 2) == 0,
            },
        };

        var plans = ProductionSetPlanner.Plan(request);
        Assert.Equal(docCount, plans.Count);

        var expectedVolumeCount = ((docCount - 1) / volumeSize) + 1;
        var volumeGroups = plans.GroupBy(p => p.VolumeIndex).OrderBy(g => g.Key).ToList();
        Assert.Equal(expectedVolumeCount, volumeGroups.Count);

        for (int v = 0; v < volumeGroups.Count; v++)
        {
            var group = volumeGroups[v];
            var expectedVolumeIndex = v + 1;
            var expectedVolumeName = string.Create(CultureInfo.InvariantCulture, $"VOL{expectedVolumeIndex:D3}");

            Assert.Equal(expectedVolumeIndex, group.Key);
            Assert.All(group, p => Assert.Equal(expectedVolumeName, p.VolumeName));

            var groupCount = group.Count();
            Assert.InRange(groupCount, 1, volumeSize);
            if (v < (volumeGroups.Count - 1))
            {
                Assert.Equal(volumeSize, groupCount);
            }
        }

        var batesList = plans.Select(p => p.BatesNumber).ToList();
        Assert.Empty(BatesRangeAnalyzer.FindGapsInSequence(batesList));

        var merged = BatesRangeAnalyzer.MergeConsecutiveBates(batesList);
        var singleRange = Assert.Single(merged);
        Assert.Equal(plans[0].BatesNumber, singleRange.Start);
        Assert.Equal(plans[^1].BatesNumber, singleRange.End);

        foreach (var plan in plans)
        {
            Assert.Contains(plan.VolumeName, plan.NativeRelPath, StringComparison.Ordinal);
            Assert.Contains(plan.BatesNumber, plan.NativeRelPath, StringComparison.Ordinal);
            Assert.Contains(plan.VolumeName, plan.TextRelPath, StringComparison.Ordinal);
            Assert.Contains(plan.VolumeName, plan.ImageRelPath, StringComparison.Ordinal);

            if (request.Production.RedactedProduction)
            {
                Assert.NotNull(plan.RedactedImageRelPath);
                Assert.NotNull(plan.RedactedTextRelPath);
            }
            else
            {
                Assert.Null(plan.RedactedImageRelPath);
                Assert.Null(plan.RedactedTextRelPath);
            }
        }
    }

    [Property]
    public void ProductionSetPlanner_PlanRollingSets_ShouldMaintainContinuousAndRestartBatesInvariants(
        BatesPropertyInput batesInput,
        byte rawDocCount,
        byte rawRollingIndex)
    {
        int docCount = (rawDocCount % 60) + 1;
        int rollingIndex = (rawRollingIndex % 5) + 1;

        var continuousRequest = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                FileCount = docCount,
                FileType = "pdf",
            },
            Bates = new BatesNumberConfig
            {
                Prefix = batesInput.Prefix,
                Start = batesInput.Start,
                Digits = batesInput.Digits,
                Increment = 1,
            },
            Production = new ProductionConfig
            {
                ProductionSet = true,
                VolumeSize = 25,
                RollingBatesMode = RollingBatesMode.Continuous,
            },
        };

        var set0 = ProductionSetPlanner.Plan(continuousRequest, rollingIndex: 0);
        var set1 = ProductionSetPlanner.Plan(continuousRequest, rollingIndex: 1);

        var combinedBates = set0.Select(p => p.BatesNumber)
            .Concat(set1.Select(p => p.BatesNumber))
            .ToList();
        Assert.Empty(BatesRangeAnalyzer.FindGapsInSequence(combinedBates));
        var mergedContinuous = Assert.Single(BatesRangeAnalyzer.MergeConsecutiveBates(combinedBates));
        Assert.Equal(set0[0].BatesNumber, mergedContinuous.Start);
        Assert.Equal(set1[^1].BatesNumber, mergedContinuous.End);

        var restartRequest = continuousRequest.Clone();
        restartRequest.Production = continuousRequest.Production with
        {
            RollingBatesMode = RollingBatesMode.Restart,
        };

        var restartSet0 = ProductionSetPlanner.Plan(restartRequest, rollingIndex: 0);
        var restartSetN = ProductionSetPlanner.Plan(restartRequest, rollingIndex: rollingIndex);
        Assert.Equal(restartSet0[0].BatesNumber, restartSetN[0].BatesNumber);
        Assert.Equal(restartSet0[^1].BatesNumber, restartSetN[^1].BatesNumber);
    }
}
