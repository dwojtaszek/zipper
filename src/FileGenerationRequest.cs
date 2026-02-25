using Zipper.Profiles;

namespace Zipper
{
    /// <summary>
    /// Represents a file generation request with all configuration options.
    /// This object should not be mutated after it has been passed to the generator (e.g., `GenerateFilesAsync`)
    /// as it is shared across multiple concurrent tasks.
    public class FileGenerationRequest
    {
        /// <summary>
        /// Gets or sets the output path.
        /// </summary>
        public string OutputPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the file count.
        /// </summary>
        public long FileCount { get; set; }

        /// <summary>
        /// Gets or sets the file type.
        /// </summary>
        public string FileType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the number of folders.
        /// </summary>
        public int Folders { get; set; } = 1;

        /// <summary>
        /// Gets or sets the concurrency level.
        /// </summary>
        public int Concurrency { get; set; } = PerformanceConstants.DefaultConcurrency;

        /// <summary>
        /// Gets or sets a value indicating whether to include metadata.
        /// </summary>
        public bool WithMetadata { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to include text files.
        /// </summary>
        public bool WithText { get; set; }

        /// <summary>
        /// Gets or sets the target zip size.
        /// </summary>
        public long? TargetZipSize { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to include load file in archive.
        /// </summary>
        public bool IncludeLoadFile { get; set; }

        /// <summary>
        /// Gets or sets the distribution type.
        /// </summary>
        public DistributionType Distribution { get; set; } = DistributionType.Proportional;

        /// <summary>
        /// Gets or sets the encoding.
        /// </summary>
        public string Encoding { get; set; } = "UTF-8";

        /// <summary>
        /// Gets or sets the attachment rate percentage.
        /// </summary>
        public int AttachmentRate { get; set; } = 0;

        /// <summary>
        /// Gets or sets the load file format.
        /// </summary>
        public LoadFileFormat LoadFileFormat { get; set; } = LoadFileFormat.Dat;

        /// <summary>
        /// Gets or sets multiple load file formats to generate simultaneously.
        /// </summary>
        public List<LoadFileFormat>? LoadFileFormats { get; set; }

        /// <summary>
        /// Gets or sets the column delimiter character for DAT files.
        /// </summary>
        public string ColumnDelimiter { get; set; } = "\u0014"; // ASCII 20

        /// <summary>
        /// Gets or sets the quote delimiter character for DAT files.
        /// </summary>
        public string QuoteDelimiter { get; set; } = "\u00fe"; // ASCII 254

        /// <summary>
        /// Gets or sets the newline replacement character for DAT files.
        /// </summary>
        public string NewlineDelimiter { get; set; } = "\u00ae"; // ASCII 174

        /// <summary>
        /// Gets or sets the Bates number configuration.
        /// </summary>
        public BatesNumberConfig? BatesConfig { get; set; }

        /// <summary>
        /// Gets or sets the TIFF page range.
        /// </summary>
        public (int Min, int Max)? TiffPageRange { get; set; }

        /// <summary>
        /// Gets or sets the column profile for metadata generation.
        /// </summary>
        public ColumnProfile? ColumnProfile { get; set; }

        /// <summary>
        /// Gets or sets the random seed for reproducible output.
        /// </summary>
        public int? Seed { get; set; }

        /// <summary>
        /// Gets or sets the date format override.
        /// </summary>
        public string? DateFormatOverride { get; set; }

        /// <summary>
        /// Gets or sets the empty percentage override.
        /// </summary>
        public int? EmptyPercentageOverride { get; set; }

        /// <summary>
        /// Gets or sets the custodian count override.
        /// </summary>
        public int? CustodianCountOverride { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to generate family relationships.
        /// </summary>
        public bool WithFamilies { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to run in loadfile-only mode (no ZIP/native files).
        /// </summary>
        public bool LoadfileOnly { get; set; }

        /// <summary>
        /// Gets or sets the end-of-line format (CRLF, LF, CR).
        /// </summary>
        public string EndOfLine { get; set; } = "CRLF";

        /// <summary>
        /// Gets or sets the multi-value delimiter character.
        /// </summary>
        public string MultiValueDelimiter { get; set; } = ";"; // ASCII 59

        /// <summary>
        /// Gets or sets the nested-value delimiter character.
        /// </summary>
        public string NestedValueDelimiter { get; set; } = "\\"; // ASCII 92

        /// <summary>
        /// Gets or sets a value indicating whether chaos mode is enabled.
        /// </summary>
        public bool ChaosMode { get; set; }

        /// <summary>
        /// Gets or sets the chaos amount (percentage like "1%" or exact count like "500").
        /// </summary>
        public string? ChaosAmount { get; set; }

        /// <summary>
        /// Gets or sets the comma-separated list of chaos types to inject.
        /// </summary>
        public string? ChaosTypes { get; set; }
    }

    /// <summary>
    /// Represents the result of a file generation operation.
    /// </summary>
    public class FileGenerationResult
    {
        /// <summary>
        /// Gets or sets the zip file path.
        /// </summary>
        public string ZipFilePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the load file path.
        /// </summary>
        public string LoadFilePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the number of files generated.
        /// </summary>
        public long FilesGenerated { get; set; }

        /// <summary>
        /// Gets or sets the generation time.
        /// </summary>
        public TimeSpan GenerationTime { get; set; }

        /// <summary>
        /// Gets or sets the files per second rate.
        /// </summary>
        public double FilesPerSecond { get; set; }
    }
}
