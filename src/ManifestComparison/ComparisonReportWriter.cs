using System.Text;

namespace Zipper.ManifestComparison;

/// <summary>
/// Renders a <see cref="ComparisonResult"/> as a markdown summary file and a
/// console summary string. Pure rendering — no I/O, no comparison logic.
/// Extracted from <see cref="ProductionManifestComparer"/> (#601 PR4).
/// </summary>
internal static class ComparisonReportWriter
{
    public static string GenerateMarkdownSummary(ComparisonResult result, string mode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Production Set Comparison Report");
        sb.AppendLine();
        sb.Append("**Mode:** ").Append(mode.ToUpperInvariant()).AppendLine();
        sb.Append("**Date Generated:** ").Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)).Append(" UTC").AppendLine();
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.Append("| Total Prior Records | ").Append(result.Summary.TotalPriorRecords.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.Append("| Total New Records | ").Append(result.Summary.TotalNewRecords.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.Append("| Added Records | ").Append(result.Summary.AddedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.Append("| Removed Records | ").Append(result.Summary.RemovedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.Append("| Unchanged Records | ").Append(result.Summary.UnchangedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.Append("| Changed Records (Metadata/Hash) | ").Append(result.Summary.ChangedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.Append("| Replaced Records (Different Bates) | ").Append(result.Summary.ReplacedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.Append("| Duplicates/Overlaps | ").Append(result.Summary.DuplicateCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.AppendLine();
        sb.AppendLine("## Bates Number Analysis");
        sb.AppendLine();
        sb.Append("* **Prior Bates Range:** ").Append(result.BatesAnalysis.PriorRange).AppendLine();
        sb.Append("* **New Bates Range:** ").Append(result.BatesAnalysis.NewRange).AppendLine();
        sb.AppendLine();

        if (result.BatesAnalysis.PriorRangesByProductionSet.Count > 0)
        {
            sb.AppendLine("### Prior Bates Ranges by Production Set");
            sb.AppendLine();
            foreach (var kvp in result.BatesAnalysis.PriorRangesByProductionSet.OrderBy(k => k.Key))
            {
                sb.Append("* **").Append(kvp.Key).Append(":** ").Append(kvp.Value).AppendLine();
            }
            sb.AppendLine();
        }

        if (result.BatesAnalysis.Gaps.Count > 0)
        {
            sb.AppendLine("### Skipped Bates Ranges (Gaps)");
            sb.AppendLine();
            foreach (var gap in result.BatesAnalysis.Gaps)
            {
                sb.Append("- `").Append(gap.Start).Append("` to `").Append(gap.End).Append('`').AppendLine();
            }
            sb.AppendLine();
        }

        if (result.BatesAnalysis.Overlaps.Count > 0)
        {
            sb.AppendLine("### Overlapping Bates Ranges");
            sb.AppendLine();
            foreach (var overlap in result.BatesAnalysis.Overlaps)
            {
                sb.Append("- `").Append(overlap.Start).Append("` to `").Append(overlap.End).Append('`').AppendLine();
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Volume Analysis");
        sb.AppendLine();
        sb.AppendLine("| Production Set | Volume | Prior Bates Range | New Bates Range | Status |");
        sb.AppendLine("|----------------|--------|-------------------|-----------------|--------|");
        foreach (var vol in result.VolumeAnalysis)
        {
            sb.Append("| ").Append(vol.ProductionId).Append(" | ").Append(vol.VolumeName).Append(" | ").Append(vol.PriorBatesRange).Append(" | ").Append(vol.NewBatesRange).Append(" | ").Append(vol.Status).Append(" |").AppendLine();
        }
        sb.AppendLine();

        return sb.ToString();
    }

    public static string GenerateConsoleSummary(ComparisonResult result, string mode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("======================================================================");
        sb.AppendLine("                   PRODUCTION MANIFEST COMPARISON SUMMARY             ");
        sb.AppendLine("======================================================================");
        sb.Append("Mode: ").Append(mode.ToUpperInvariant()).AppendLine();
        sb.AppendLine();
        sb.Append("Total Prior Records: ").Append(result.Summary.TotalPriorRecords.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Total New Records:   ").Append(result.Summary.TotalNewRecords.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Added Records:       ").Append(result.Summary.AddedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Removed Records:     ").Append(result.Summary.RemovedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Unchanged Records:   ").Append(result.Summary.UnchangedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Changed Records:     ").Append(result.Summary.ChangedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Replaced Records:    ").Append(result.Summary.ReplacedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Duplicate Warnings:  ").Append(result.Summary.DuplicateCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.AppendLine();
        sb.Append("Prior Bates Range:   ").Append(result.BatesAnalysis.PriorRange).AppendLine();
        sb.Append("New Bates Range:     ").Append(result.BatesAnalysis.NewRange).AppendLine();
        if (result.BatesAnalysis.Gaps.Count > 0)
        {
            sb.Append("Gaps Detected:       ").Append(result.BatesAnalysis.Gaps.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine(" range(s) skipped.");
        }
        sb.AppendLine("======================================================================");
        return sb.ToString();
    }
}
