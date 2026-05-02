using Zipper.Config;
using Zipper.Profiles;

namespace Zipper
{
    public class FileGenerationRequest
    {
        public OutputConfig Output { get; set; } = new();

        public MetadataConfig Metadata { get; set; } = new();

        public LoadFileConfig LoadFile { get; set; } = new();

        public DelimiterConfig Delimiters { get; set; } = new();

        public BatesNumberConfig? Bates { get; set; }

        public TiffConfig Tiff { get; set; } = new();

        public ChaosConfig Chaos { get; set; } = new();

        public ProductionConfig Production { get; set; } = new();

        public string OutputPath
        {
            get => this.Output.OutputPath;
            set => this.Output = this.Output with { OutputPath = value };
        }

        public long FileCount
        {
            get => this.Output.FileCount;
            set => this.Output = this.Output with { FileCount = value };
        }

        public string FileType
        {
            get => this.Output.FileType;
            set => this.Output = this.Output with { FileType = value };
        }

        public int Folders
        {
            get => this.Output.Folders;
            set => this.Output = this.Output with { Folders = value };
        }

        public int Concurrency
        {
            get => this.Output.Concurrency;
            set => this.Output = this.Output with { Concurrency = value };
        }

        public bool WithText
        {
            get => this.Output.WithText;
            set => this.Output = this.Output with { WithText = value };
        }

        public long? TargetZipSize
        {
            get => this.Output.TargetZipSize;
            set => this.Output = this.Output with { TargetZipSize = value };
        }

        public bool IncludeLoadFile
        {
            get => this.Output.IncludeLoadFile;
            set => this.Output = this.Output with { IncludeLoadFile = value };
        }

        public bool WithMetadata
        {
            get => this.Metadata.WithMetadata;
            set => this.Metadata = this.Metadata with { WithMetadata = value };
        }

        public ColumnProfile? ColumnProfile
        {
            get => this.Metadata.ColumnProfile;
            set => this.Metadata = this.Metadata with { ColumnProfile = value };
        }

        public int? Seed
        {
            get => this.Metadata.Seed;
            set => this.Metadata = this.Metadata with { Seed = value };
        }

        public string? DateFormatOverride
        {
            get => this.Metadata.DateFormatOverride;
            set => this.Metadata = this.Metadata with { DateFormatOverride = value };
        }

        public int? EmptyPercentageOverride
        {
            get => this.Metadata.EmptyPercentageOverride;
            set => this.Metadata = this.Metadata with { EmptyPercentageOverride = value };
        }

        public int? CustodianCountOverride
        {
            get => this.Metadata.CustodianCountOverride;
            set => this.Metadata = this.Metadata with { CustodianCountOverride = value };
        }

        public bool WithFamilies
        {
            get => this.Metadata.WithFamilies;
            set => this.Metadata = this.Metadata with { WithFamilies = value };
        }

        public LoadFileFormat LoadFileFormat
        {
            get => this.LoadFile.LoadFileFormat;
            set => this.LoadFile = this.LoadFile with { LoadFileFormat = value };
        }

        public List<LoadFileFormat>? LoadFileFormats
        {
            get => this.LoadFile.LoadFileFormats;
            set => this.LoadFile = this.LoadFile with { LoadFileFormats = value };
        }

        public string Encoding
        {
            get => this.LoadFile.Encoding;
            set => this.LoadFile = this.LoadFile with { Encoding = value };
        }

        public DistributionType Distribution
        {
            get => this.LoadFile.Distribution;
            set => this.LoadFile = this.LoadFile with { Distribution = value };
        }

        public int AttachmentRate
        {
            get => this.LoadFile.AttachmentRate;
            set => this.LoadFile = this.LoadFile with { AttachmentRate = value };
        }

        public string ColumnDelimiter
        {
            get => this.Delimiters.ColumnDelimiter;
            set => this.Delimiters = this.Delimiters with { ColumnDelimiter = value };
        }

        public string QuoteDelimiter
        {
            get => this.Delimiters.QuoteDelimiter;
            set => this.Delimiters = this.Delimiters with { QuoteDelimiter = value };
        }

        public string NewlineDelimiter
        {
            get => this.Delimiters.NewlineDelimiter;
            set => this.Delimiters = this.Delimiters with { NewlineDelimiter = value };
        }

        public string MultiValueDelimiter
        {
            get => this.Delimiters.MultiValueDelimiter;
            set => this.Delimiters = this.Delimiters with { MultiValueDelimiter = value };
        }

        public string NestedValueDelimiter
        {
            get => this.Delimiters.NestedValueDelimiter;
            set => this.Delimiters = this.Delimiters with { NestedValueDelimiter = value };
        }

        public string EndOfLine
        {
            get => this.Delimiters.EndOfLine;
            set => this.Delimiters = this.Delimiters with { EndOfLine = value };
        }

        public BatesNumberConfig? BatesConfig
        {
            get => this.Bates;
            set => this.Bates = value;
        }

        public (int Min, int Max)? TiffPageRange
        {
            get => this.Tiff.PageRange;
            set => this.Tiff = this.Tiff with { PageRange = value };
        }

        public bool ChaosMode
        {
            get => this.Chaos.ChaosMode;
            set => this.Chaos = this.Chaos with { ChaosMode = value };
        }

        public string? ChaosAmount
        {
            get => this.Chaos.ChaosAmount;
            set => this.Chaos = this.Chaos with { ChaosAmount = value };
        }

        public string? ChaosTypes
        {
            get => this.Chaos.ChaosTypes;
            set => this.Chaos = this.Chaos with { ChaosTypes = value };
        }

        public string? ChaosScenario
        {
            get => this.Chaos.ChaosScenario;
            set => this.Chaos = this.Chaos with { ChaosScenario = value };
        }

        public bool ProductionSet
        {
            get => this.Production.ProductionSet;
            set => this.Production = this.Production with { ProductionSet = value };
        }

        public bool ProductionZip
        {
            get => this.Production.ProductionZip;
            set => this.Production = this.Production with { ProductionZip = value };
        }

        public int VolumeSize
        {
            get => this.Production.VolumeSize;
            set => this.Production = this.Production with { VolumeSize = value };
        }

        public bool LoadfileOnly { get; set; }

        public FileGenerationRequest Clone()
        {
            return new FileGenerationRequest
            {
                Output = this.Output,
                Metadata = this.Metadata,
                LoadFile = this.LoadFile with
                {
                    LoadFileFormats = this.LoadFile.LoadFileFormats is null
                        ? null
                        : new List<LoadFileFormat>(this.LoadFile.LoadFileFormats),
                },
                Delimiters = this.Delimiters,
                Bates = this.Bates,
                Tiff = this.Tiff,
                Chaos = this.Chaos,
                Production = this.Production,
                LoadfileOnly = this.LoadfileOnly,
            };
        }
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
