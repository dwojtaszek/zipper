// <copyright file="DataGenerator.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Text;

namespace Zipper.Profiles;

/// <summary>
/// Generates data values based on column profile definitions.
/// </summary>
internal class DataGenerator
{
    private readonly ColumnProfile profile;
    private readonly Random random;
    private readonly Dictionary<string, List<string>> generatedDataSources;
    private readonly Dictionary<string, int[]> distributionIndices;
    private int documentIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataGenerator"/> class.
    /// </summary>
    /// <param name="profile">The column profile to use for generation.</param>
    /// <param name="seed">Optional random seed for reproducible output.</param>
    public DataGenerator(ColumnProfile profile, int? seed = null)
    {
        this.profile = profile;
        this.random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        this.generatedDataSources = new Dictionary<string, List<string>>();
        this.distributionIndices = new Dictionary<string, int[]>();

        this.InitializeDataSources();
    }

    /// <summary>
    /// Generates column values for a document.
    /// </summary>
    /// <param name="workItem">The file work item.</param>
    /// <param name="fileData">The file data.</param>
    /// <returns>Dictionary of column names to values.</returns>
    public Dictionary<string, string> GenerateRow(FileWorkItem workItem, FileData fileData)
    {
        this.documentIndex++;
        var row = new Dictionary<string, string>();

        foreach (var column in this.profile.Columns)
        {
            var value = this.GenerateColumnValue(column, workItem, fileData);
            row[column.Name] = value;
        }

        return row;
    }

    /// <summary>
    /// Gets the column names in order.
    /// </summary>
    /// <returns>Enumerable of column names.</returns>
    public IEnumerable<string> GetColumnNames() => this.profile.Columns.Select(c => c.Name);

    private void InitializeDataSources()
    {
        foreach (var (sourceName, config) in this.profile.DataSources)
        {
            var values = new List<string>();

            if (config.Values != null && config.Values.Count > 0)
            {
                values.AddRange(config.Values);
            }
            else
            {
                for (int i = 1; i <= config.Count; i++)
                {
                    values.Add($"{config.Prefix}{i}");
                }
            }

            this.generatedDataSources[sourceName] = values;

            if (config.Distribution == "pareto" || config.Distribution == "weighted")
            {
                this.distributionIndices[sourceName] = this.PrecomputeDistributionIndices(values.Count, config);
            }
        }
    }

    private int[] PrecomputeDistributionIndices(int count, DataSourceConfig config)
    {
        var indices = new int[1000];

        if (config.Distribution == "weighted" && config.Weights != null && config.Weights.Count > 0)
        {
            var totalWeight = config.Weights.Sum();

            // Guard against zero/negative total weight
            if (totalWeight <= 0)
            {
                // Fallback to uniform distribution
                for (int i = 0; i < indices.Length; i++)
                {
                    indices[i] = this.random.Next(count);
                }

                return indices;
            }

            for (int i = 0; i < indices.Length; i++)
            {
                var r = this.random.Next(totalWeight);
                var cumulative = 0;
                for (int j = 0; j < config.Weights.Count && j < count; j++)
                {
                    cumulative += config.Weights[j];
                    if (r < cumulative)
                    {
                        indices[i] = j;
                        break;
                    }
                }
            }
        }
        else
        {
            var topCount = Math.Max(1, count / 5);
            for (int i = 0; i < indices.Length; i++)
            {
                if (this.random.NextDouble() < 0.8)
                {
                    indices[i] = this.random.Next(topCount);
                }
                else
                {
                    indices[i] = this.random.Next(count);
                }
            }
        }

        return indices;
    }

    private string GenerateColumnValue(ColumnDefinition column, FileWorkItem workItem, FileData fileData)
    {
        var emptyPct = column.EmptyPercentage ?? this.profile.Settings.EmptyValuePercentage;
        if (!column.Required && this.random.Next(100) < emptyPct)
        {
            return string.Empty;
        }

        return column.Type.ToLowerInvariant() switch
        {
            "identifier" => this.GenerateIdentifier(workItem),
            "text" => this.GenerateText(column, workItem, fileData),
            "longtext" => this.GenerateLongText(column),
            "date" => this.GenerateDate(column),
            "datetime" => this.GenerateDateTime(column),
            "number" => this.GenerateNumber(column, fileData),
            "boolean" => this.GenerateBoolean(column),
            "coded" => this.GenerateCoded(column),
            "email" => this.GenerateEmail(column),
            _ => string.Empty,
        };
    }

    private string GenerateIdentifier(FileWorkItem workItem)
    {
        return $"DOC{workItem.Index:D8}";
    }

    private string GenerateText(ColumnDefinition column, FileWorkItem workItem, FileData fileData)
    {
        if (column.Name.Equals("FILEPATH", StringComparison.OrdinalIgnoreCase))
        {
            return workItem.FilePathInZip;
        }

        if (column.Name.Equals("FILENAME", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(workItem.FilePathInZip);
        }

        if (column.Name.Equals("FILEEXT", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetExtension(workItem.FilePathInZip).TrimStart('.');
        }

        if (!string.IsNullOrEmpty(column.DataSource) && this.generatedDataSources.TryGetValue(column.DataSource, out var values))
        {
            return this.GetValueFromDataSource(column.DataSource, values);
        }

        if (!string.IsNullOrEmpty(column.Generator))
        {
            return this.GenerateFromGenerator(column.Generator, column);
        }

        return $"Value_{this.documentIndex}_{column.Name}";
    }

    private string GenerateLongText(ColumnDefinition column)
    {
        if (column.Generator == "loremParagraphs")
        {
            var minParagraphs = 1;
            var maxParagraphs = 3;

            if (column.GeneratorParams != null)
            {
                if (column.GeneratorParams.TryGetValue("min", out var minObj))
                {
                    minParagraphs = Convert.ToInt32(minObj);
                }

                if (column.GeneratorParams.TryGetValue("max", out var maxObj))
                {
                    maxParagraphs = Convert.ToInt32(maxObj);
                }
            }

            var paragraphCount = this.random.Next(minParagraphs, maxParagraphs + 1);
            return string.Join(" ", Enumerable.Range(0, paragraphCount).Select(_ => LoremIpsum.GetParagraph(this.random)));
        }

        if (column.Generator == "reviewNote")
        {
            return ReviewNotes.GetRandomNote(this.random);
        }

        return LoremIpsum.GetParagraph(this.random);
    }

    private string GenerateDate(ColumnDefinition column)
    {
        var minDate = DateTime.Parse(column.DateRange?.Min ?? "2020-01-01");
        var maxDate = DateTime.Parse(column.DateRange?.Max ?? "2024-12-31");
        var range = (maxDate - minDate).Days;

        // Handle edge case where min == max (range is 0)
        var date = range <= 0 ? minDate : minDate.AddDays(this.random.Next(range + 1));
        var format = column.Format ?? this.profile.Settings.DateFormat;
        return date.ToString(format);
    }

    private string GenerateDateTime(ColumnDefinition column)
    {
        var minDate = DateTime.Parse(column.DateRange?.Min ?? "2020-01-01");
        var maxDate = DateTime.Parse(column.DateRange?.Max ?? "2024-12-31");
        var range = (maxDate - minDate).Days;

        // Handle edge case where min == max (range is 0)
        var date = range <= 0
            ? minDate.AddHours(this.random.Next(24)).AddMinutes(this.random.Next(60))
            : minDate.AddDays(this.random.Next(range + 1)).AddHours(this.random.Next(24)).AddMinutes(this.random.Next(60));
        var format = column.Format ?? this.profile.Settings.DateTimeFormat;
        return date.ToString(format);
    }

    private string GenerateNumber(ColumnDefinition column, FileData fileData)
    {
        if (column.Name.Equals("FILESIZE", StringComparison.OrdinalIgnoreCase))
        {
            return fileData.Data.Length.ToString();
        }

        if (column.Name.Equals("PAGECOUNT", StringComparison.OrdinalIgnoreCase))
        {
            return fileData.PageCount.ToString();
        }

        var min = column.Range?.Min ?? 0;
        var max = column.Range?.Max ?? 1000;

        // Handle invalid range where max < min
        if (max < min)
        {
            return min.ToString();
        }

        // Handle case where min == max
        if (max == min)
        {
            return min.ToString();
        }

        if (column.Distribution == "exponential")
        {
            var lambda = 3.0 / (max - min);
            var value = min + (int)(-Math.Log(1 - this.random.NextDouble()) / lambda);
            return Math.Min(value, max).ToString();
        }

        // Guard against overflow when max is int.MaxValue
        if (max == int.MaxValue)
        {
            return (min + (long)(this.random.NextDouble() * ((long)max - min + 1))).ToString();
        }

        return this.random.Next(min, max + 1).ToString();
    }

    private string GenerateBoolean(ColumnDefinition column)
    {
        var truePct = column.TruePercentage ?? 50;
        var value = this.random.Next(100) < truePct;

        return column.Format?.ToLowerInvariant() switch
        {
            "yn" => value ? "Y" : "N",
            "truefalse" => value ? "True" : "False",
            "10" => value ? "1" : "0",
            _ => value ? "Y" : "N",
        };
    }

    private string GenerateCoded(ColumnDefinition column)
    {
        if (string.IsNullOrEmpty(column.DataSource) || !this.generatedDataSources.TryGetValue(column.DataSource, out var values))
        {
            return string.Empty;
        }

        if (column.MultiValue)
        {
            var min = column.MultiValueCount?.Min ?? 1;
            var max = column.MultiValueCount?.Max ?? 3;
            var count = this.random.Next(min, max + 1);

            if (count == 0)
            {
                return string.Empty;
            }

            var selected = new HashSet<string>();
            while (selected.Count < count && selected.Count < values.Count)
            {
                selected.Add(this.GetValueFromDataSource(column.DataSource, values));
            }

            return string.Join(this.profile.Settings.MultiValueDelimiter, selected);
        }

        return this.GetValueFromDataSource(column.DataSource, values);
    }

    private string GenerateEmail(ColumnDefinition column)
    {
        if (column.MultiValue)
        {
            var min = column.MultiValueCount?.Min ?? 1;
            var max = column.MultiValueCount?.Max ?? 3;
            var count = this.random.Next(min, max + 1);

            if (count == 0)
            {
                return string.Empty;
            }

            var emails = Enumerable.Range(0, count).Select(_ => this.GenerateSingleEmail()).ToList();
            return string.Join(this.profile.Settings.MultiValueDelimiter, emails);
        }

        return this.GenerateSingleEmail();
    }

    private string GenerateSingleEmail()
    {
        var domains = new[] { "example.com", "company.org", "corp.net", "business.io", "enterprise.co" };
        var firstName = Names.FirstNames[this.random.Next(Names.FirstNames.Length)];
        var lastName = Names.LastNames[this.random.Next(Names.LastNames.Length)];
        var domain = domains[this.random.Next(domains.Length)];
        return $"{firstName.ToLower()}.{lastName.ToLower()}@{domain}";
    }

    private string GetValueFromDataSource(string sourceName, List<string> values)
    {
        if (this.distributionIndices.TryGetValue(sourceName, out var indices))
        {
            var idx = indices[this.documentIndex % indices.Length];
            return values[idx % values.Count];
        }

        return values[this.random.Next(values.Count)];
    }

    private string GenerateFromGenerator(string generator, ColumnDefinition column)
    {
        return generator.ToLowerInvariant() switch
        {
            "emailsubject" => EmailSubjects.GetRandom(this.random),
            "md5hash" => this.GenerateHash(32),
            "sha1hash" => this.GenerateHash(40),
            "sha256hash" => this.GenerateHash(64),
            "reviewnote" => ReviewNotes.GetRandomNote(this.random),
            "encoding" => Encodings.GetRandom(this.random),
            "timezone" => TimeZones.GetRandom(this.random),
            _ => $"Generated_{generator}_{this.documentIndex}",
        };
    }

    private string GenerateHash(int length)
    {
        const string chars = "0123456789abcdef";
        return new string(Enumerable.Range(0, length).Select(_ => chars[this.random.Next(chars.Length)]).ToArray());
    }
}

/// <summary>
/// Lorem ipsum text generator.
/// </summary>
internal static class LoremIpsum
{
    private static readonly string[] Words =
    {
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit",
        "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore",
        "magna", "aliqua", "enim", "ad", "minim", "veniam", "quis", "nostrud",
        "exercitation", "ullamco", "laboris", "nisi", "aliquip", "ex", "ea", "commodo",
        "consequat", "duis", "aute", "irure", "in", "reprehenderit", "voluptate",
        "velit", "esse", "cillum", "fugiat", "nulla", "pariatur", "excepteur", "sint",
        "occaecat", "cupidatat", "non", "proident", "sunt", "culpa", "qui", "officia",
        "deserunt", "mollit", "anim", "id", "est", "laborum",
    };

    /// <summary>
    /// Gets a paragraph of Lorem ipsum text.
    /// </summary>
    /// <param name="random">Random instance.</param>
    /// <returns>A paragraph.</returns>
    public static string GetParagraph(Random random)
    {
        var sentenceCount = random.Next(3, 7);
        var sb = new StringBuilder();

        for (int i = 0; i < sentenceCount; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            AppendSentence(sb, random);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets a sentence of Lorem ipsum text.
    /// </summary>
    /// <param name="random">Random instance.</param>
    /// <returns>A sentence.</returns>
    public static string GetSentence(Random random)
    {
        var sb = new StringBuilder();
        AppendSentence(sb, random);
        return sb.ToString();
    }

    private static void AppendSentence(StringBuilder sb, Random random)
    {
        var wordCount = random.Next(8, 20);
        for (int i = 0; i < wordCount; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            var word = Words[random.Next(Words.Length)];
            if (i == 0)
            {
                sb.Append(char.ToUpper(word[0]));
                if (word.Length > 1)
                {
                    sb.Append(word, 1, word.Length - 1);
                }
            }
            else
            {
                sb.Append(word);
            }
        }

        sb.Append('.');
    }
}

/// <summary>
/// Name data for generation.
/// </summary>
internal static class Names
{
    /// <summary>
    /// First names for generation.
    /// </summary>
    public static readonly string[] FirstNames =
    {
        "James", "Mary", "John", "Patricia", "Robert", "Jennifer", "Michael", "Linda",
        "William", "Elizabeth", "David", "Barbara", "Richard", "Susan", "Joseph", "Jessica",
        "Thomas", "Sarah", "Charles", "Karen", "Christopher", "Nancy", "Daniel", "Lisa",
        "Matthew", "Betty", "Anthony", "Margaret", "Mark", "Sandra", "Donald", "Ashley",
    };

    /// <summary>
    /// Last names for generation.
    /// </summary>
    public static readonly string[] LastNames =
    {
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis",
        "Rodriguez", "Martinez", "Hernandez", "Lopez", "Gonzalez", "Wilson", "Anderson",
        "Thomas", "Taylor", "Moore", "Jackson", "Martin", "Lee", "Perez", "Thompson",
        "White", "Harris", "Sanchez", "Clark", "Ramirez", "Lewis", "Robinson", "Walker",
    };
}

/// <summary>
/// Email subject generator.
/// </summary>
internal static class EmailSubjects
{
    private static readonly string[] Prefixes = { string.Empty, "Re: ", "Fwd: ", "RE: ", "FW: " };

    private static readonly string[] Subjects =
    {
        "Meeting Tomorrow", "Project Update", "Quick Question", "Follow Up",
        "Action Required", "Review Needed", "Status Report", "Weekly Summary",
        "Important Notice", "Reminder", "Schedule Change", "Document Review",
        "Contract Discussion", "Budget Update", "Team Meeting", "Client Call",
    };

    /// <summary>
    /// Gets a random email subject.
    /// </summary>
    /// <param name="random">Random instance.</param>
    /// <returns>Email subject.</returns>
    public static string GetRandom(Random random)
    {
        var prefix = Prefixes[random.Next(Prefixes.Length)];
        var subject = Subjects[random.Next(Subjects.Length)];
        return prefix + subject;
    }
}

/// <summary>
/// Review notes generator.
/// </summary>
internal static class ReviewNotes
{
    private static readonly string[] Notes =
    {
        "Reviewed and marked responsive.",
        "Contains privileged attorney-client communication.",
        "Not relevant to the matter.",
        "Contains trade secret information - needs redaction.",
        "Duplicate of document DOC000001.",
        "Foreign language content detected - needs translation.",
        "Reviewed by reviewer on date.",
        "Escalated for senior review.",
        "Contains PII - redaction required.",
        "Key document - flag for production.",
    };

    /// <summary>
    /// Gets a random review note.
    /// </summary>
    /// <param name="random">Random instance.</param>
    /// <returns>Review note.</returns>
    public static string GetRandomNote(Random random)
    {
        return Notes[random.Next(Notes.Length)];
    }
}

/// <summary>
/// Encoding names.
/// </summary>
internal static class Encodings
{
    private static readonly string[] Values = { "UTF-8", "ASCII", "UTF-16", "ISO-8859-1", "Windows-1252" };

    /// <summary>
    /// Gets a random encoding name.
    /// </summary>
    /// <param name="random">Random instance.</param>
    /// <returns>Encoding name.</returns>
    public static string GetRandom(Random random) => Values[random.Next(Values.Length)];
}

/// <summary>
/// Time zone names.
/// </summary>
internal static class TimeZones
{
    private static readonly string[] Values =
    {
        "UTC", "America/New_York", "America/Los_Angeles", "America/Chicago",
        "Europe/London", "Europe/Paris", "Asia/Tokyo", "Asia/Shanghai",
    };

    /// <summary>
    /// Gets a random time zone.
    /// </summary>
    /// <param name="random">Random instance.</param>
    /// <returns>Time zone name.</returns>
    public static string GetRandom(Random random) => Values[random.Next(Values.Length)];
}
