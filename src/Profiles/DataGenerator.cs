using Zipper.Profiles.Generation;

namespace Zipper.Profiles;

/// <summary>
/// Generates column values for a document using a ColumnProfile and registered IColumnValueGenerators.
/// </summary>
internal class DataGenerator
{
    private const string CustodianDataSourceName = "custodians";

    private readonly ColumnProfile profile;
    private readonly Random random;
    private readonly Dictionary<string, string[]> dataSources;
    private readonly Dictionary<string, int[]> distributionIndices;
    private readonly Dictionary<string, IColumnValueGenerator> columnGenerators;
    private readonly DateTime now;
    private int documentIndex;

    public DataGenerator(ColumnProfile profile, int? seed = null, DateTime? now = null, int? custodianCountOverride = null)
    {
        this.profile = custodianCountOverride.HasValue
            ? ApplyCustodianCountOverride(profile, custodianCountOverride.Value)
            : profile;
#pragma warning disable S2245
        this.random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
#pragma warning restore S2245
        this.dataSources = new Dictionary<string, string[]>();
        this.distributionIndices = new Dictionary<string, int[]>();
        this.columnGenerators = new Dictionary<string, IColumnValueGenerator>();
        this.now = now ?? DateTime.UtcNow;
        this.InitializeDataSources();
        this.InitializeColumnGenerators();
    }

    /// <summary>
    /// Returns a copy of <paramref name="profile"/> whose <c>custodians</c> data source is
    /// adjusted to produce at most <paramref name="count"/> distinct custodian values.
    /// For generated (Count+Prefix) sources the count is replaced directly.
    /// For static Values lists both the values and any associated weights are truncated.
    /// If the profile has no <c>custodians</c> data source the original profile is returned unchanged.
    /// </summary>
    private static ColumnProfile ApplyCustodianCountOverride(ColumnProfile profile, int count)
    {
        if (!profile.DataSources.TryGetValue(CustodianDataSourceName, out var custodianSource))
        {
            return profile;
        }

        var effectiveCount = Math.Max(1, count);
        DataSourceConfig overriddenSource;

        if (custodianSource.Values?.Count > 0)
        {
            // Static value list: truncate values and corresponding weights to the requested count
            overriddenSource = new DataSourceConfig
            {
                Count = effectiveCount,
                Distribution = custodianSource.Distribution,
                Prefix = custodianSource.Prefix,
                Values = custodianSource.Values.Take(effectiveCount).ToList(),
                Weights = custodianSource.Weights?.Take(effectiveCount).ToList(),
            };
        }
        else
        {
            // Generated names (Count + Prefix): replace the count and truncate weights to match
            overriddenSource = new DataSourceConfig
            {
                Count = effectiveCount,
                Distribution = custodianSource.Distribution,
                Prefix = custodianSource.Prefix,
                Values = null,
                Weights = custodianSource.Weights?.Take(effectiveCount).ToList(),
            };
        }

        var newDataSources = new Dictionary<string, DataSourceConfig>(profile.DataSources)
        {
            [CustodianDataSourceName] = overriddenSource,
        };

        return new ColumnProfile
        {
            Name = profile.Name,
            Description = profile.Description,
            Version = profile.Version,
            FieldNamingConvention = profile.FieldNamingConvention,
            Settings = profile.Settings,
            DataSources = newDataSources,
            Columns = profile.Columns,
        };
    }

    public Dictionary<string, string> GenerateRow(FileWorkItem workItem, FileData fileData)
    {
        this.documentIndex++;
        var ctx = new ColumnGenerationContext
        {
            NativeFileIndex = workItem.Index,
            FolderNumber = workItem.FolderNumber,
            DocumentIndex = this.documentIndex,
            Seeded = this.random,
            Now = this.now,
            FileData = fileData,
        };
        var row = new Dictionary<string, string>(this.profile.Columns.Count);
        foreach (var col in this.profile.Columns)
        {
            var emptyPct = col.EmptyPercentage ?? this.profile.Settings.EmptyValuePercentage;
            if (!col.Required && this.random.Next(100) < emptyPct)
            {
                row[col.Name] = string.Empty;
                continue;
            }

            row[col.Name] = this.columnGenerators.TryGetValue(col.Name, out var gen)
                ? gen.Generate(ctx with { ProfileColumn = col })
                : string.Empty;
        }

        return row;
    }

    public IEnumerable<string> GetColumnNames() => this.profile.Columns.Select(c => c.Name);

    private void InitializeDataSources()
    {
        foreach (var (name, cfg) in this.profile.DataSources)
        {
            var vals = (cfg.Values?.Count > 0
                ? cfg.Values
                : Enumerable.Range(1, cfg.Count).Select(i => $"{cfg.Prefix}{i}")).ToArray();
            this.dataSources[name] = vals;
            if (cfg.Distribution == "pareto" || cfg.Distribution == "weighted")
            {
                this.distributionIndices[name] = this.PrecomputeIndices(vals.Length, cfg);
            }
        }
    }

    private void InitializeColumnGenerators()
    {
        foreach (var col in this.profile.Columns)
        {
            this.columnGenerators[col.Name] = this.CreateGenerator(col);
        }
    }

    private IColumnValueGenerator CreateGenerator(ColumnDefinition col)
    {
        var ds = !string.IsNullOrEmpty(col.DataSource) && this.dataSources.TryGetValue(col.DataSource, out var v) ? v : null;
        this.distributionIndices.TryGetValue(col.DataSource ?? string.Empty, out var di);
        return col.Type.ToLowerInvariant() switch
        {
            "identifier" => new IdentifierGenerator(),
            "text" => new TextGenerator(col, ds, di),
            "longtext" => new LongTextGenerator(col),
            "date" => new DateGenerator(col, this.profile.Settings),
            "datetime" => new DateTimeColumnGenerator(col, this.profile.Settings),
            "number" => new NumberGenerator(col),
            "boolean" => new BooleanGenerator(col),
            "coded" => new CodedGenerator(ds ?? Array.Empty<string>(), di, col, this.profile.Settings),
            "email" => new EmailAddressGenerator(col, this.profile.Settings),
            "foldercustodian" => new LegacyFolderCustodianGenerator(),
            "indexcustodian" => new LegacyIndexCustodianGenerator(),
            "legacydatesent" => new LegacyDateSentGenerator(),
            "legacydatecreated" => new LegacyDateCreatedGenerator(),
            "legacyauthor" => new LegacyAuthorGenerator(),
            "filedatasize" => new LegacyFileSizeFromDataGenerator(),
            "randomfilesize" => new LegacyRandomFileSizeGenerator(),
            "emailto" => new LegacyEmailToGenerator(),
            "emailfrom" => new LegacyEmailFromGenerator(),
            "emailsubject" => new LegacyEmailSubjectGenerator(),
            "emailsentdate" => new LegacyEmailSentDateGenerator(),
            "emailattachment" => new LegacyEmailAttachmentGenerator(),
            "syntheticemailto" => new LegacySyntheticEmailToGenerator(),
            "syntheticemailfrom" => new LegacySyntheticEmailFromGenerator(),
            "syntheticemailsubject" => new LegacySyntheticEmailSubjectGenerator(),
            "syntheticemailsentdate" => new LegacySyntheticEmailSentDateGenerator(),
            _ => new TextGenerator(col, ds, di),
        };
    }

    private int[] PrecomputeIndices(int count, DataSourceConfig cfg)
    {
        var indices = new int[1000];
        if (cfg.Distribution == "weighted" && cfg.Weights?.Count > 0)
        {
            var total = cfg.Weights.Take(count).Sum();
            if (total <= 0)
            {
                for (int i = 0; i < 1000; i++)
                {
                    indices[i] = this.random.Next(count);
                }

                return indices;
            }

            for (int i = 0; i < 1000; i++)
            {
                var r = this.random.Next(total);
                var cum = 0;
                var assigned = false;
                for (int j = 0; j < cfg.Weights.Count && j < count; j++)
                {
                    cum += cfg.Weights[j];
                    if (r < cum)
                    {
                        indices[i] = j;
                        assigned = true;
                        break;
                    }
                }

                if (!assigned && count > 0)
                {
                    indices[i] = count - 1;
                }
            }
        }
        else
        {
            var top = Math.Max(1, count / 5);
            for (int i = 0; i < 1000; i++)
            {
                indices[i] = this.random.NextDouble() < 0.8 ? this.random.Next(top) : this.random.Next(count);
            }
        }

        return indices;
    }
}
