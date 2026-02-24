namespace Zipper.Profiles;

/// <summary>
/// Provides built-in column profiles for common e-discovery workflows.
/// </summary>
public static class BuiltInProfiles
{
    /// <summary>
    /// Gets the available built-in profile names.
    /// </summary>
    public static readonly string[] ProfileNames = { "minimal", "standard", "litigation", "full" };

    /// <summary>
    /// Gets the minimal profile (5 columns).
    /// </summary>
    public static ColumnProfile Minimal { get; } = new()
    {
        Name = "minimal",
        Description = "Basic fields for minimal load file generation",
        Version = "1.0",
        FieldNamingConvention = "UPPERCASE",
        Settings = new ProfileSettings
        {
            EmptyValuePercentage = 0,
            DateFormat = "yyyy-MM-dd",
        },
        DataSources = new Dictionary<string, DataSourceConfig>
        {
            ["custodians"] = new DataSourceConfig { Count = 10, Distribution = "pareto", Prefix = "Custodian_" },
        },
        Columns = new List<ColumnDefinition>
        {
            new() { Name = "DOCID", Type = "identifier", Required = true },
            new() { Name = "FILEPATH", Type = "text", Required = true },
            new() { Name = "CUSTODIAN", Type = "text", DataSource = "custodians", EmptyPercentage = 0 },
            new() { Name = "DATECREATED", Type = "date", DateRange = new DateRangeConfig { Min = "2020-01-01", Max = "2024-12-31" } },
            new() { Name = "FILESIZE", Type = "number", Range = new RangeConfig { Min = 1024, Max = 10485760 }, Distribution = "exponential" },
        },
    };

    /// <summary>
    /// Gets the standard profile (25 columns).
    /// </summary>
    public static ColumnProfile Standard { get; } = new()
    {
        Name = "standard",
        Description = "Common e-discovery fields for standard workflows",
        Version = "1.0",
        FieldNamingConvention = "UPPERCASE",
        Settings = new ProfileSettings
        {
            EmptyValuePercentage = 10,
            MultiValueDelimiter = ";",
            DateFormat = "yyyy-MM-dd",
        },
        DataSources = new Dictionary<string, DataSourceConfig>
        {
            ["custodians"] = new DataSourceConfig { Count = 25, Distribution = "pareto", Prefix = "Custodian_" },
            ["authors"] = new DataSourceConfig { Count = 50, Distribution = "pareto", Prefix = "Author_" },
            ["departments"] = new DataSourceConfig { Values = new List<string> { "Legal", "Finance", "HR", "Engineering", "Sales", "Marketing", "Operations", "Executive" } },
            ["docTypes"] = new DataSourceConfig { Values = new List<string> { "Email", "Document", "Spreadsheet", "Presentation", "Image", "PDF", "Other" } },
        },
        Columns = new List<ColumnDefinition>
        {
            // Identifiers
            new() { Name = "DOCID", Type = "identifier", Required = true },
            new() { Name = "BEGBATES", Type = "text", Required = true },
            new() { Name = "ENDBATES", Type = "text", Required = true },
            new() { Name = "CONTROLNUMBER", Type = "text", Required = true },

            // File info
            new() { Name = "FILEPATH", Type = "text", Required = true },
            new() { Name = "TEXTPATH", Type = "text", EmptyPercentage = 5 },
            new() { Name = "NATIVEPATH", Type = "text", EmptyPercentage = 0 },
            new() { Name = "FILENAME", Type = "text", Required = true },
            new() { Name = "FILEEXT", Type = "text", Required = true },
            new() { Name = "FILESIZE", Type = "number", Range = new RangeConfig { Min = 1024, Max = 104857600 }, Distribution = "exponential" },

            // Dates
            new() { Name = "DATECREATED", Type = "date", DateRange = new DateRangeConfig { Min = "2020-01-01", Max = "2024-12-31" } },
            new() { Name = "DATEMODIFIED", Type = "date", DateRange = new DateRangeConfig { Min = "2020-01-01", Max = "2024-12-31" } },
            new() { Name = "DATESENT", Type = "datetime", DateRange = new DateRangeConfig { Min = "2020-01-01", Max = "2024-12-31" }, EmptyPercentage = 30 },
            new() { Name = "DATERECEIVED", Type = "datetime", DateRange = new DateRangeConfig { Min = "2020-01-01", Max = "2024-12-31" }, EmptyPercentage = 30 },

            // People
            new() { Name = "CUSTODIAN", Type = "text", DataSource = "custodians", EmptyPercentage = 0 },
            new() { Name = "AUTHOR", Type = "text", DataSource = "authors", EmptyPercentage = 5 },
            new() { Name = "EMAILFROM", Type = "email", EmptyPercentage = 30 },
            new() { Name = "EMAILTO", Type = "email", MultiValue = true, MultiValueCount = new RangeConfig { Min = 1, Max = 5 }, EmptyPercentage = 30 },
            new() { Name = "EMAILCC", Type = "email", MultiValue = true, MultiValueCount = new RangeConfig { Min = 0, Max = 10 }, EmptyPercentage = 50 },

            // Classification
            new() { Name = "DOCTYPE", Type = "coded", DataSource = "docTypes" },
            new() { Name = "DEPARTMENT", Type = "coded", DataSource = "departments", EmptyPercentage = 20 },
            new() { Name = "RESPONSIVE", Type = "boolean", TruePercentage = 60, Format = "YN" },
            new() { Name = "PRIVILEGED", Type = "boolean", TruePercentage = 10, Format = "YN" },
            new() { Name = "CONFIDENTIAL", Type = "boolean", TruePercentage = 25, Format = "YN" },
        },
    };

    /// <summary>
    /// Gets the litigation profile (50 columns).
    /// </summary>
    public static ColumnProfile Litigation { get; } = new()
    {
        Name = "litigation",
        Description = "Full litigation support with comprehensive metadata",
        Version = "1.0",
        FieldNamingConvention = "UPPERCASE",
        Settings = new ProfileSettings
        {
            EmptyValuePercentage = 15,
            MultiValueDelimiter = ";",
            DateFormat = "yyyy-MM-dd",
        },
        DataSources = new Dictionary<string, DataSourceConfig>
        {
            ["custodians"] = new DataSourceConfig { Count = 50, Distribution = "pareto", Prefix = "Custodian_" },
            ["authors"] = new DataSourceConfig { Count = 100, Distribution = "pareto", Prefix = "Author_" },
            ["departments"] = new DataSourceConfig { Values = new List<string> { "Legal", "Finance", "HR", "Engineering", "Sales", "Marketing", "Operations", "Executive", "IT", "Compliance" } },
            ["docTypes"] = new DataSourceConfig { Values = new List<string> { "Email", "Document", "Spreadsheet", "Presentation", "Image", "PDF", "Contract", "Invoice", "Memo", "Report" } },
            ["privilegeTypes"] = new DataSourceConfig { Values = new List<string> { "Attorney-Client", "Work Product", "Not Privileged", "Needs Review" }, Weights = new List<int> { 5, 5, 80, 10 } },
            ["responsiveness"] = new DataSourceConfig { Values = new List<string> { "Responsive", "Not Responsive", "Needs Review", "Privileged" }, Weights = new List<int> { 40, 35, 15, 10 } },
            ["confidentiality"] = new DataSourceConfig { Values = new List<string> { "Public", "Internal", "Confidential", "Highly Confidential" }, Weights = new List<int> { 20, 50, 25, 5 } },
            ["languages"] = new DataSourceConfig { Values = new List<string> { "English", "Spanish", "French", "German", "Chinese", "Japanese" }, Weights = new List<int> { 85, 5, 3, 2, 3, 2 } },
        },
        Columns = CreateLitigationColumns(),
    };

    /// <summary>
    /// Gets the full profile (150 columns).
    /// </summary>
    public static ColumnProfile Full { get; } = new()
    {
        Name = "full",
        Description = "Maximum field coverage for comprehensive data generation",
        Version = "1.0",
        FieldNamingConvention = "UPPERCASE",
        Settings = new ProfileSettings
        {
            EmptyValuePercentage = 20,
            MultiValueDelimiter = ";",
            DateFormat = "yyyy-MM-dd",
        },
        DataSources = new Dictionary<string, DataSourceConfig>
        {
            ["custodians"] = new DataSourceConfig { Count = 100, Distribution = "pareto", Prefix = "Custodian_" },
            ["authors"] = new DataSourceConfig { Count = 200, Distribution = "pareto", Prefix = "Author_" },
            ["departments"] = new DataSourceConfig { Values = new List<string> { "Legal", "Finance", "HR", "Engineering", "Sales", "Marketing", "Operations", "Executive", "IT", "Compliance", "Research", "Support" } },
            ["docTypes"] = new DataSourceConfig { Values = new List<string> { "Email", "Document", "Spreadsheet", "Presentation", "Image", "PDF", "Contract", "Invoice", "Memo", "Report", "Letter", "Fax", "Note" } },
            ["privilegeTypes"] = new DataSourceConfig { Values = new List<string> { "Attorney-Client", "Work Product", "Not Privileged", "Needs Review" }, Weights = new List<int> { 5, 5, 80, 10 } },
            ["responsiveness"] = new DataSourceConfig { Values = new List<string> { "Responsive", "Not Responsive", "Needs Review", "Privileged" }, Weights = new List<int> { 40, 35, 15, 10 } },
            ["confidentiality"] = new DataSourceConfig { Values = new List<string> { "Public", "Internal", "Confidential", "Highly Confidential" }, Weights = new List<int> { 20, 50, 25, 5 } },
            ["tags"] = new DataSourceConfig { Values = new List<string> { "Key Document", "Hot Document", "Duplicate", "Near-Duplicate", "Foreign Language", "Privileged Review", "Redaction Required" } },
            ["issues"] = new DataSourceConfig { Values = new List<string> { "Issue 1", "Issue 2", "Issue 3", "Issue 4", "Issue 5", "Issue 6", "Issue 7", "Issue 8" } },
            ["languages"] = new DataSourceConfig { Values = new List<string> { "English", "Spanish", "French", "German", "Chinese", "Japanese", "Korean", "Portuguese", "Italian", "Russian" }, Weights = new List<int> { 80, 5, 3, 2, 2, 2, 1, 2, 2, 1 } },
        },
        Columns = CreateFullColumns(),
    };

    /// <summary>
    /// Gets a built-in profile by name.
    /// </summary>
    public static ColumnProfile? GetProfile(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "minimal" => Minimal,
            "standard" => Standard,
            "litigation" => Litigation,
            "full" => Full,
            _ => null,
        };
    }

    private static List<ColumnDefinition> CreateLitigationColumns()
    {
        var columns = new List<ColumnDefinition>
        {
            // Core identifiers
            new() { Name = "DOCID", Type = "identifier", Required = true },
            new() { Name = "BEGBATES", Type = "text", Required = true },
            new() { Name = "ENDBATES", Type = "text", Required = true },
            new() { Name = "CONTROLNUMBER", Type = "text", Required = true },
            new() { Name = "BEGATTACH", Type = "text", EmptyPercentage = 70 },
            new() { Name = "ENDATTACH", Type = "text", EmptyPercentage = 70 },
            new() { Name = "PARENTDOCID", Type = "text", EmptyPercentage = 70 },
            new() { Name = "FAMILYID", Type = "text", EmptyPercentage = 50 },

            // File info
            new() { Name = "FILEPATH", Type = "text", Required = true },
            new() { Name = "TEXTPATH", Type = "text", EmptyPercentage = 5 },
            new() { Name = "NATIVEPATH", Type = "text", EmptyPercentage = 0 },
            new() { Name = "FILENAME", Type = "text", Required = true },
            new() { Name = "FILEEXT", Type = "text", Required = true },
            new() { Name = "FILESIZE", Type = "number", Range = new RangeConfig { Min = 1024, Max = 104857600 }, Distribution = "exponential" },
            new() { Name = "PAGECOUNT", Type = "number", Range = new RangeConfig { Min = 1, Max = 500 }, Distribution = "exponential" },
            new() { Name = "WORDCOUNT", Type = "number", Range = new RangeConfig { Min = 0, Max = 50000 }, Distribution = "exponential" },
            new() { Name = "CHARCOUNT", Type = "number", Range = new RangeConfig { Min = 0, Max = 250000 }, Distribution = "exponential" },

            // Dates
            new() { Name = "DATECREATED", Type = "date", DateRange = new DateRangeConfig { Min = "2018-01-01", Max = "2024-12-31" } },
            new() { Name = "DATEMODIFIED", Type = "date", DateRange = new DateRangeConfig { Min = "2018-01-01", Max = "2024-12-31" } },
            new() { Name = "DATESENT", Type = "datetime", DateRange = new DateRangeConfig { Min = "2018-01-01", Max = "2024-12-31" }, EmptyPercentage = 30 },
            new() { Name = "DATERECEIVED", Type = "datetime", DateRange = new DateRangeConfig { Min = "2018-01-01", Max = "2024-12-31" }, EmptyPercentage = 30 },
            new() { Name = "DATEPRODUCED", Type = "date", DateRange = new DateRangeConfig { Min = "2024-01-01", Max = "2024-12-31" }, EmptyPercentage = 50 },

            // People
            new() { Name = "CUSTODIAN", Type = "text", DataSource = "custodians", EmptyPercentage = 0 },
            new() { Name = "AUTHOR", Type = "text", DataSource = "authors", EmptyPercentage = 5 },
            new() { Name = "EMAILFROM", Type = "email", EmptyPercentage = 30 },
            new() { Name = "EMAILTO", Type = "email", MultiValue = true, MultiValueCount = new RangeConfig { Min = 1, Max = 10 }, EmptyPercentage = 30 },
            new() { Name = "EMAILCC", Type = "email", MultiValue = true, MultiValueCount = new RangeConfig { Min = 0, Max = 15 }, EmptyPercentage = 50 },
            new() { Name = "EMAILBCC", Type = "email", MultiValue = true, MultiValueCount = new RangeConfig { Min = 0, Max = 3 }, EmptyPercentage = 90 },
            new() { Name = "EMAILSUBJECT", Type = "text", Generator = "emailSubject", EmptyPercentage = 30 },
            new() { Name = "CONVERSATIONID", Type = "text", EmptyPercentage = 50 },
            new() { Name = "MESSAGEID", Type = "text", EmptyPercentage = 50 },

            // Classification
            new() { Name = "DOCTYPE", Type = "coded", DataSource = "docTypes" },
            new() { Name = "DEPARTMENT", Type = "coded", DataSource = "departments", EmptyPercentage = 20 },
            new() { Name = "PRIVILEGE", Type = "coded", DataSource = "privilegeTypes" },
            new() { Name = "RESPONSIVE", Type = "coded", DataSource = "responsiveness" },
            new() { Name = "CONFIDENTIALITY", Type = "coded", DataSource = "confidentiality" },
            new() { Name = "LANGUAGE", Type = "coded", DataSource = "languages" },

            // Boolean flags
            new() { Name = "ISRESPONSIVE", Type = "boolean", TruePercentage = 60, Format = "YN" },
            new() { Name = "ISPRIVILEGED", Type = "boolean", TruePercentage = 10, Format = "YN" },
            new() { Name = "ISCONFIDENTIAL", Type = "boolean", TruePercentage = 25, Format = "YN" },
            new() { Name = "HASATTACHMENTS", Type = "boolean", TruePercentage = 30, Format = "YN" },
            new() { Name = "ISNATIVE", Type = "boolean", TruePercentage = 70, Format = "YN" },
            new() { Name = "NEEDSREDACTION", Type = "boolean", TruePercentage = 15, Format = "YN" },

            // Hashes
            new() { Name = "MD5HASH", Type = "text", Generator = "md5Hash" },
            new() { Name = "SHA1HASH", Type = "text", Generator = "sha1Hash" },
            new() { Name = "SHA256HASH", Type = "text", Generator = "sha256Hash" },

            // Notes and text
            new() { Name = "REVIEWNOTES", Type = "longtext", Generator = "reviewNote", EmptyPercentage = 80 },
            new() { Name = "REDACTIONREASON", Type = "text", EmptyPercentage = 90 },
        };

        return columns;
    }

    private static List<ColumnDefinition> CreateFullColumns()
    {
        // Start with litigation columns
        var columns = CreateLitigationColumns();

        // Add many more columns for the full profile
        var extraColumns = new List<ColumnDefinition>();

        // Additional metadata fields
        for (int i = 1; i <= 20; i++)
        {
            extraColumns.Add(new ColumnDefinition
            {
                Name = $"CUSTOMTEXT{i:D2}",
                Type = "text",
                EmptyPercentage = 50 + (i * 2),
            });
        }

        for (int i = 1; i <= 15; i++)
        {
            extraColumns.Add(new ColumnDefinition
            {
                Name = $"CUSTOMDATE{i:D2}",
                Type = "date",
                DateRange = new DateRangeConfig { Min = "2015-01-01", Max = "2024-12-31" },
                EmptyPercentage = 40 + (i * 3),
            });
        }

        for (int i = 1; i <= 15; i++)
        {
            extraColumns.Add(new ColumnDefinition
            {
                Name = $"CUSTOMNUMBER{i:D2}",
                Type = "number",
                Range = new RangeConfig { Min = 0, Max = 10000 },
                EmptyPercentage = 30 + (i * 3),
            });
        }

        for (int i = 1; i <= 10; i++)
        {
            extraColumns.Add(new ColumnDefinition
            {
                Name = $"CUSTOMBOOL{i:D2}",
                Type = "boolean",
                TruePercentage = 30 + (i * 4),
                Format = "YN",
            });
        }

        // Issue tags (multi-value)
        for (int i = 1; i <= 5; i++)
        {
            extraColumns.Add(new ColumnDefinition
            {
                Name = $"ISSUETAG{i:D2}",
                Type = "coded",
                DataSource = "issues",
                MultiValue = true,
                MultiValueCount = new RangeConfig { Min = 0, Max = 3 },
                EmptyPercentage = 60,
            });
        }

        // Long text fields
        for (int i = 1; i <= 10; i++)
        {
            extraColumns.Add(new ColumnDefinition
            {
                Name = $"NOTES{i:D2}",
                Type = "longtext",
                Generator = "loremParagraphs",
                GeneratorParams = new Dictionary<string, object> { ["min"] = 1, ["max"] = 3 },
                EmptyPercentage = 70 + (i * 2),
            });
        }

        // Additional identifiers
        extraColumns.Add(new ColumnDefinition { Name = "PRODUCTIONSTATUS", Type = "coded", DataSource = "responsiveness", EmptyPercentage = 30 });
        extraColumns.Add(new ColumnDefinition { Name = "REVIEWSTATUS", Type = "coded", DataSource = "responsiveness", EmptyPercentage = 20 });
        extraColumns.Add(new ColumnDefinition { Name = "QCSTATUS", Type = "coded", DataSource = "responsiveness", EmptyPercentage = 60 });
        extraColumns.Add(new ColumnDefinition { Name = "ENCODING", Type = "text", Generator = "encoding", EmptyPercentage = 10 });
        extraColumns.Add(new ColumnDefinition { Name = "TIMEZONE", Type = "text", Generator = "timezone", EmptyPercentage = 20 });

        // More classification
        for (int i = 1; i <= 10; i++)
        {
            extraColumns.Add(new ColumnDefinition
            {
                Name = $"TAG{i:D2}",
                Type = "coded",
                DataSource = "tags",
                EmptyPercentage = 70,
            });
        }

        columns.AddRange(extraColumns);
        return columns;
    }
}
