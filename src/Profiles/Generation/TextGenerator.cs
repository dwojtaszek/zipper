namespace Zipper.Profiles.Generation;

using Zipper.Profiles.Data;

internal sealed class TextGenerator : IColumnValueGenerator
{
    private readonly string colName;
    private readonly string? generatorName;
    private readonly string[]? dataSourceValues;
    private readonly int[]? distributionIndices;

    public TextGenerator(ColumnDefinition col, string[]? dataSourceValues, int[]? distributionIndices)
    {
        this.colName = col.Name;
        this.generatorName = col.Generator;
        this.dataSourceValues = dataSourceValues;
        this.distributionIndices = distributionIndices;
    }

    public string Generate(ColumnGenerationContext context)
    {
        if (this.colName.Equals("FILEPATH", StringComparison.OrdinalIgnoreCase))
        {
            return context.FileData?.WorkItem.FilePathInZip ?? string.Empty;
        }

        if (this.colName.Equals("FILENAME", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(context.FileData?.WorkItem.FilePathInZip ?? string.Empty);
        }

        if (this.colName.Equals("FILEEXT", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetExtension(context.FileData?.WorkItem.FilePathInZip ?? string.Empty).TrimStart('.');
        }

        if (this.dataSourceValues is not null)
        {
            if (this.distributionIndices is not null)
            {
                var idx = this.distributionIndices[context.DocumentIndex % this.distributionIndices.Length];
                return this.dataSourceValues[idx % this.dataSourceValues.Length];
            }

            return this.dataSourceValues[context.Seeded.Next(this.dataSourceValues.Length)];
        }

        if (!string.IsNullOrEmpty(this.generatorName))
        {
            return this.GenerateFromNamed(context);
        }

        return $"Value_{context.DocumentIndex}_{this.colName}";
    }

    private string GenerateFromNamed(ColumnGenerationContext context) =>
        this.generatorName!.ToLowerInvariant() switch
        {
            "emailsubject" => EmailSubjects.GetRandom(context.Seeded),
            "md5hash" => GenerateHash(32, context.Seeded),
            "sha1hash" => GenerateHash(40, context.Seeded),
            "sha256hash" => GenerateHash(64, context.Seeded),
            "reviewnote" => ReviewNotes.GetRandomNote(context.Seeded),
            "encoding" => Encodings.GetRandom(context.Seeded),
            "timezone" => TimeZones.GetRandom(context.Seeded),
            _ => $"Generated_{this.generatorName}_{context.DocumentIndex}",
        };

    private static string GenerateHash(int length, Random random)
    {
        const string chars = "0123456789abcdef";
        Span<char> buffer = stackalloc char[length];
        for (int i = 0; i < length; i++)
        {
            buffer[i] = chars[random.Next(chars.Length)];
        }

        return new string(buffer);
    }
}
