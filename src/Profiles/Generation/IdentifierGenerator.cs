namespace Zipper.Profiles.Generation;

internal sealed class IdentifierGenerator : IColumnValueGenerator
{
    public string Generate(ColumnGenerationContext context) => $"DOC{context.NativeFileIndex:D8}";
}
