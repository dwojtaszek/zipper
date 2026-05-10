namespace Zipper.Profiles.Generation;

internal interface IColumnValueGenerator
{
    string Generate(ColumnGenerationContext context);
}
