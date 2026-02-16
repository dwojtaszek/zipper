using System.Text.Json.Serialization;

namespace Zipper.Profiles;

/// <summary>
/// Represents a column profile configuration for load file generation.
/// </summary>
public class ColumnProfile
{
    /// <summary>
    /// Gets or sets the profile name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the profile description.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the profile version.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Gets or sets the field naming convention (UPPERCASE, PascalCase, lowercase).
    /// </summary>
    [JsonPropertyName("fieldNamingConvention")]
    public string FieldNamingConvention { get; set; } = "UPPERCASE";

    /// <summary>
    /// Gets or sets the profile settings.
    /// </summary>
    [JsonPropertyName("settings")]
    public ProfileSettings Settings { get; set; } = new();

    /// <summary>
    /// Gets or sets the data sources for value generation.
    /// </summary>
    [JsonPropertyName("dataSources")]
    public Dictionary<string, DataSourceConfig> DataSources { get; set; } = new();

    /// <summary>
    /// Gets or sets the column definitions.
    /// </summary>
    [JsonPropertyName("columns")]
    public List<ColumnDefinition> Columns { get; set; } = new();
}

/// <summary>
/// Profile-level settings for data generation.
/// </summary>
public class ProfileSettings
{
    /// <summary>
    /// Gets or sets the default empty value percentage (0-100).
    /// </summary>
    [JsonPropertyName("emptyValuePercentage")]
    public int EmptyValuePercentage { get; set; } = 15;

    /// <summary>
    /// Gets or sets the multi-value delimiter.
    /// </summary>
    [JsonPropertyName("multiValueDelimiter")]
    public string MultiValueDelimiter { get; set; } = ";";

    /// <summary>
    /// Gets or sets the default date format.
    /// </summary>
    [JsonPropertyName("dateFormat")]
    public string DateFormat { get; set; } = "yyyy-MM-dd";

    /// <summary>
    /// Gets or sets the default datetime format.
    /// </summary>
    [JsonPropertyName("dateTimeFormat")]
    public string DateTimeFormat { get; set; } = "yyyy-MM-ddTHH:mm:ssZ";
}

/// <summary>
/// Configuration for a data source used in value generation.
/// </summary>
public class DataSourceConfig
{
    /// <summary>
    /// Gets or sets the number of values to generate (for generated sources).
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; set; } = 10;

    /// <summary>
    /// Gets or sets the distribution pattern.
    /// </summary>
    [JsonPropertyName("distribution")]
    public string Distribution { get; set; } = "uniform";

    /// <summary>
    /// Gets or sets the prefix for generated values.
    /// </summary>
    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets static values (for fixed value lists).
    /// </summary>
    [JsonPropertyName("values")]
    public List<string>? Values { get; set; }

    /// <summary>
    /// Gets or sets weights for weighted distribution.
    /// </summary>
    [JsonPropertyName("weights")]
    public List<int>? Weights { get; set; }
}

/// <summary>
/// Defines a single column in the profile.
/// </summary>
public class ColumnDefinition
{
    /// <summary>
    /// Gets or sets the column name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name (optional).
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the column type.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    /// <summary>
    /// Gets or sets whether this column is required (always populated).
    /// </summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    /// <summary>
    /// Gets or sets the empty value percentage override for this column.
    /// </summary>
    [JsonPropertyName("emptyPercentage")]
    public int? EmptyPercentage { get; set; }

    /// <summary>
    /// Gets or sets whether this column supports multiple values.
    /// </summary>
    [JsonPropertyName("multiValue")]
    public bool MultiValue { get; set; }

    /// <summary>
    /// Gets or sets the multi-value count range.
    /// </summary>
    [JsonPropertyName("multiValueCount")]
    public RangeConfig? MultiValueCount { get; set; }

    /// <summary>
    /// Gets or sets the data source name for this column.
    /// </summary>
    [JsonPropertyName("dataSource")]
    public string? DataSource { get; set; }

    /// <summary>
    /// Gets or sets the value range for numeric/date types.
    /// </summary>
    [JsonPropertyName("range")]
    public RangeConfig? Range { get; set; }

    /// <summary>
    /// Gets or sets a date-specific range.
    /// </summary>
    [JsonPropertyName("dateRange")]
    public DateRangeConfig? DateRange { get; set; }

    /// <summary>
    /// Gets or sets the distribution pattern for this column.
    /// </summary>
    [JsonPropertyName("distribution")]
    public string? Distribution { get; set; }

    /// <summary>
    /// Gets or sets the format string (for dates, booleans).
    /// </summary>
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    /// <summary>
    /// Gets or sets the true percentage for boolean types.
    /// </summary>
    [JsonPropertyName("truePercentage")]
    public int? TruePercentage { get; set; }

    /// <summary>
    /// Gets or sets weights for weighted distribution.
    /// </summary>
    [JsonPropertyName("weights")]
    public List<int>? Weights { get; set; }

    /// <summary>
    /// Gets or sets the generator name for special generators.
    /// </summary>
    [JsonPropertyName("generator")]
    public string? Generator { get; set; }

    /// <summary>
    /// Gets or sets generator-specific parameters.
    /// </summary>
    [JsonPropertyName("generatorParams")]
    public Dictionary<string, object>? GeneratorParams { get; set; }
}

/// <summary>
/// Numeric range configuration.
/// </summary>
public class RangeConfig
{
    /// <summary>
    /// Gets or sets the minimum value.
    /// </summary>
    [JsonPropertyName("min")]
    public int Min { get; set; }

    /// <summary>
    /// Gets or sets the maximum value.
    /// </summary>
    [JsonPropertyName("max")]
    public int Max { get; set; } = 100;
}

/// <summary>
/// Date range configuration.
/// </summary>
public class DateRangeConfig
{
    /// <summary>
    /// Gets or sets the minimum date.
    /// </summary>
    [JsonPropertyName("min")]
    public string Min { get; set; } = "2020-01-01";

    /// <summary>
    /// Gets or sets the maximum date.
    /// </summary>
    [JsonPropertyName("max")]
    public string Max { get; set; } = "2024-12-31";
}
