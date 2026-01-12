// <copyright file="FileGenerationRequest.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Zipper
{
    public class FileGenerationRequest
    {
        public string OutputPath { get; set; } = string.Empty;

        public long FileCount { get; set; }

        public string FileType { get; set; } = string.Empty;

        public int Folders { get; set; } = 1;

        public int Concurrency { get; set; } = PerformanceConstants.DefaultConcurrency;

        public bool WithMetadata { get; set; }

        public bool WithText { get; set; }

        public long? TargetZipSize { get; set; }

        public bool IncludeLoadFile { get; set; }

        public DistributionType Distribution { get; set; } = DistributionType.Proportional;

        public string Encoding { get; set; } = "UTF-8";

        public int AttachmentRate { get; set; } = 0;

        // New properties for 4-feature implementation
        public LoadFileFormat LoadFileFormat { get; set; } = LoadFileFormat.Dat;

        public BatesNumberConfig? BatesConfig { get; set; }

        public (int Min, int Max)? TiffPageRange { get; set; }
    }

    public class FileGenerationResult
    {
        public string ZipFilePath { get; set; } = string.Empty;

        public string LoadFilePath { get; set; } = string.Empty;

        public long FilesGenerated { get; set; }

        public TimeSpan GenerationTime { get; set; }

        public double FilesPerSecond { get; set; }
    }
}
