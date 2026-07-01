namespace Zipper.Profiles.Generation;

using Zipper.Profiles.Data;

internal sealed class EmailAddressGenerator : IColumnValueGenerator
{
    private static readonly string[] Domains = { "example.com", "company.org", "corp.net", "business.io", "enterprise.co" };
    private readonly bool multiValue;
    private readonly int multiValueMin;
    private readonly int multiValueMax;
    private readonly string multiValueDelimiter;

    public EmailAddressGenerator(ColumnDefinition col, ProfileSettings settings)
    {
        this.multiValue = col.MultiValue;
        this.multiValueMin = col.MultiValueCount?.Min ?? 1;
        this.multiValueMax = col.MultiValueCount?.Max ?? 3;
        this.multiValueDelimiter = settings.MultiValueDelimiter;
    }

    public string Generate(ColumnGenerationContext context)
    {
        if (this.multiValue)
        {
            var count = context.Seeded.Next(this.multiValueMin, this.multiValueMax + 1);
            if (count == 0)
            {
                return string.Empty;
            }

            return string.Join(this.multiValueDelimiter, Enumerable.Range(0, count).Select(_ => GenerateSingle(context.Seeded)));
        }

        return GenerateSingle(context.Seeded);
    }

    private static string GenerateSingle(Random random)
    {
        var firstName = Names.FirstNamesLower[random.Next(Names.FirstNamesLower.Length)];
        var lastName = Names.LastNamesLower[random.Next(Names.LastNamesLower.Length)];
        var domain = Domains[random.Next(Domains.Length)];
        return $"{firstName}.{lastName}@{domain}";
    }
}
