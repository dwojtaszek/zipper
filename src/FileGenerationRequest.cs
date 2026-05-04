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
