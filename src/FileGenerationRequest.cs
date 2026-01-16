// <copyright file="FileGenerationRequest.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using Zipper.Profiles;

namespace Zipper
{
    /// <summary>
    /// Represents a file generation request with all configuration options.
    /// </summary>
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
        /// Gets or sets a value indicating whether to use standard DAT delimiters.
        /// </summary>
        public bool UseStandardDatDelimiters { get; set; } = true;

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
