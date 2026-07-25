using System.Text.Json.Serialization;

namespace Zipper.ManifestComparison;

public class LoadedManifest
{
    public string ProductionId { get; set; } = string.Empty;
    public string BatesNumberStart { get; set; } = string.Empty;
    public string BatesNumberEnd { get; set; } = string.Empty;
    public LoadedBatesRange? BatesRange { get; set; }
    public LoadedLoadFiles? LoadFiles { get; set; }
    public LoadedSettings? Settings { get; set; }
}

public class LoadedBatesRange
{
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public int Digits { get; set; }
}

public class LoadedLoadFiles
{
    public string Dat { get; set; } = string.Empty;
    public string Opt { get; set; } = string.Empty;
}

public class LoadedSettings
{
    public string Encoding { get; set; } = string.Empty;
    public string ColumnDelimiter { get; set; } = string.Empty;
    public string QuoteDelimiter { get; set; } = string.Empty;
}

public class ComparisonRecord
{
    public string BatesNumber { get; set; } = string.Empty;
    public string ControlNumber { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string Volume { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string ProductionId { get; set; } = string.Empty;
    public int SourceLine { get; set; }
}

public class ComparisonResult
{
    [JsonPropertyName("comparisonMode")]
    public string ComparisonMode { get; set; } = string.Empty;

    [JsonPropertyName("manifests")]
    public List<string> Manifests { get; set; } = new();

    [JsonPropertyName("summary")]
    public SummaryResult Summary { get; set; } = new();

    [JsonPropertyName("batesAnalysis")]
    public BatesAnalysis BatesAnalysis { get; set; } = new();

    [JsonPropertyName("volumeAnalysis")]
    public List<VolumeResult> VolumeAnalysis { get; set; } = new();

    [JsonPropertyName("details")]
    public ResultDetails Details { get; set; } = new();
}

public class SummaryResult
{
    [JsonPropertyName("totalPriorRecords")]
    public int TotalPriorRecords { get; set; }

    [JsonPropertyName("totalNewRecords")]
    public int TotalNewRecords { get; set; }

    [JsonPropertyName("addedCount")]
    public int AddedCount { get; set; }

    [JsonPropertyName("removedCount")]
    public int RemovedCount { get; set; }

    [JsonPropertyName("unchangedCount")]
    public int UnchangedCount { get; set; }

    [JsonPropertyName("changedCount")]
    public int ChangedCount { get; set; }

    [JsonPropertyName("replacedCount")]
    public int ReplacedCount { get; set; }

    [JsonPropertyName("duplicateCount")]
    public int DuplicateCount { get; set; }
}

public class BatesAnalysis
{
    [JsonPropertyName("priorRange")]
    public string PriorRange { get; set; } = string.Empty;

    [JsonPropertyName("newRange")]
    public string NewRange { get; set; } = string.Empty;

    [JsonPropertyName("gaps")]
    public List<BatesRangeReport> Gaps { get; set; } = new();

    [JsonPropertyName("overlaps")]
    public List<BatesRangeReport> Overlaps { get; set; } = new();

    [JsonPropertyName("totalSkippedBates")]
    public int TotalSkippedBates { get; set; }

    [JsonPropertyName("priorRangesByProductionSet")]
    public Dictionary<string, string> PriorRangesByProductionSet { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class BatesRangeReport
{
    [JsonPropertyName("start")]
    public string Start { get; set; } = string.Empty;

    [JsonPropertyName("end")]
    public string End { get; set; } = string.Empty;
}

public class VolumeResult
{
    [JsonPropertyName("productionId")]
    public string ProductionId { get; set; } = string.Empty;

    [JsonPropertyName("volumeName")]
    public string VolumeName { get; set; } = string.Empty;

    [JsonPropertyName("priorBatesRange")]
    public string PriorBatesRange { get; set; } = string.Empty;

    [JsonPropertyName("newBatesRange")]
    public string NewBatesRange { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty; // unchanged, added, removed, changed
}

public class ResultDetails
{
    [JsonPropertyName("added")]
    public List<RecordDetail> Added { get; set; } = new();

    [JsonPropertyName("removed")]
    public List<RecordDetail> Removed { get; set; } = new();

    [JsonPropertyName("unchanged")]
    public List<RecordDetail> Unchanged { get; set; } = new();

    [JsonPropertyName("changed")]
    public List<ChangedDetail> Changed { get; set; } = new();

    [JsonPropertyName("replaced")]
    public List<ReplacedDetail> Replaced { get; set; } = new();

    [JsonPropertyName("duplicates")]
    public List<DuplicateDetail> Duplicates { get; set; } = new();

    [JsonPropertyName("skipped")]
    public List<BatesRangeReport> Skipped { get; set; } = new();
}

public class RecordDetail
{
    [JsonPropertyName("batesNumber")]
    public string BatesNumber { get; set; } = string.Empty;

    [JsonPropertyName("controlNumber")]
    public string ControlNumber { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("volume")]
    public string Volume { get; set; } = string.Empty;
}

public class ChangedDetail
{
    [JsonPropertyName("batesNumber")]
    public string BatesNumber { get; set; } = string.Empty;

    [JsonPropertyName("controlNumber")]
    public string ControlNumber { get; set; } = string.Empty;

    [JsonPropertyName("priorPath")]
    public string PriorPath { get; set; } = string.Empty;

    [JsonPropertyName("newPath")]
    public string NewPath { get; set; } = string.Empty;

    [JsonPropertyName("priorHash")]
    public string PriorHash { get; set; } = string.Empty;

    [JsonPropertyName("newHash")]
    public string NewHash { get; set; } = string.Empty;
}

public class ReplacedDetail
{
    [JsonPropertyName("priorBatesNumber")]
    public string PriorBatesNumber { get; set; } = string.Empty;

    [JsonPropertyName("newBatesNumber")]
    public string NewBatesNumber { get; set; } = string.Empty;

    [JsonPropertyName("controlNumber")]
    public string ControlNumber { get; set; } = string.Empty;
}

public class DuplicateDetail
{
    [JsonPropertyName("set")]
    public string Set { get; set; } = string.Empty; // prior, new

    [JsonPropertyName("batesNumber")]
    public string BatesNumber { get; set; } = string.Empty;

    [JsonPropertyName("controlNumber")]
    public string ControlNumber { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
