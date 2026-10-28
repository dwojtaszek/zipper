using Xunit;

namespace Zipper.Tests;

public class ChaosAnomalyTypesTests
{
    [Fact]
    public void Dat_ContainsExpectedTypes()
    {
        Assert.Contains("mixed-delimiters", ChaosAnomalyTypes.Dat);
        Assert.Contains("quotes", ChaosAnomalyTypes.Dat);
        Assert.Contains("columns", ChaosAnomalyTypes.Dat);
        Assert.Contains("eol", ChaosAnomalyTypes.Dat);
        Assert.Contains("encoding", ChaosAnomalyTypes.Dat);
        Assert.Contains("path-pointer", ChaosAnomalyTypes.Dat);
        Assert.Contains("field-length", ChaosAnomalyTypes.Dat);
        Assert.Contains("date-format", ChaosAnomalyTypes.Dat);
        Assert.Contains("null-byte", ChaosAnomalyTypes.Dat);
        Assert.Contains("trailing-empties", ChaosAnomalyTypes.Dat);
        Assert.Equal(10, ChaosAnomalyTypes.Dat.Count);
    }

    [Fact]
    public void DatDefault_ExcludesOptInTypes()
    {
        Assert.Equal(
            new[] { "mixed-delimiters", "quotes", "columns", "eol", "encoding" },
            ChaosAnomalyTypes.DatDefault);
    }

    [Fact]
    public void Opt_ContainsExpectedTypes()
    {
        Assert.Contains("opt-boundary", ChaosAnomalyTypes.Opt);
        Assert.Contains("opt-columns", ChaosAnomalyTypes.Opt);
        Assert.Contains("opt-pagecount", ChaosAnomalyTypes.Opt);
        Assert.Contains("opt-path", ChaosAnomalyTypes.Opt);
        Assert.Contains("opt-batesnumber", ChaosAnomalyTypes.Opt);
        Assert.Contains("opt-encoding", ChaosAnomalyTypes.Opt);
        Assert.Equal(6, ChaosAnomalyTypes.Opt.Count);
    }

    [Fact]
    public void Csv_ContainsExpectedFormatSpecificTypes()
    {
        Assert.Contains("csv-quote-escape", ChaosAnomalyTypes.Csv);
        Assert.Contains("csv-bom", ChaosAnomalyTypes.Csv);
        Assert.Contains("csv-embedded-newline", ChaosAnomalyTypes.Csv);
        // Must not silently inherit DAT arms
        Assert.DoesNotContain("mixed-delimiters", ChaosAnomalyTypes.Csv);
        Assert.DoesNotContain("quotes", ChaosAnomalyTypes.Csv);
    }

    [Fact]
    public void Concordance_ContainsExpectedFormatSpecificTypes()
    {
        Assert.Contains("concordance-delimiter", ChaosAnomalyTypes.Concordance);
        Assert.Contains("concordance-quote-escape", ChaosAnomalyTypes.Concordance);
        // Must not silently inherit DAT arms
        Assert.DoesNotContain("mixed-delimiters", ChaosAnomalyTypes.Concordance);
        Assert.DoesNotContain("quotes", ChaosAnomalyTypes.Concordance);
    }

    [Fact]
    public void EdrmXml_ContainsExpectedFormatSpecificTypes()
    {
        Assert.Contains("xml-unclosed-tag", ChaosAnomalyTypes.EdrmXml);
        Assert.Contains("xml-bad-entity", ChaosAnomalyTypes.EdrmXml);
        Assert.Contains("xml-encoding-lie", ChaosAnomalyTypes.EdrmXml);
        // Must not inherit DAT arms
        Assert.DoesNotContain("mixed-delimiters", ChaosAnomalyTypes.EdrmXml);
        Assert.DoesNotContain("eol", ChaosAnomalyTypes.EdrmXml);
    }

    [Fact]
    public void ForFormat_Dat_ReturnsDatTypes()
    {
        var types = ChaosAnomalyTypes.ForFormat(LoadFileFormat.Dat);
        Assert.Equal(ChaosAnomalyTypes.Dat, types);
    }

    [Fact]
    public void ForFormat_Opt_ReturnsOptTypes()
    {
        var types = ChaosAnomalyTypes.ForFormat(LoadFileFormat.Opt);
        Assert.Equal(ChaosAnomalyTypes.Opt, types);
    }

    [Fact]
    public void ForFormat_Csv_ReturnsCsvTypes()
    {
        var types = ChaosAnomalyTypes.ForFormat(LoadFileFormat.Csv);
        Assert.Equal(ChaosAnomalyTypes.Csv, types);
    }

    [Fact]
    public void ForFormat_Concordance_ReturnsConcordanceTypes()
    {
        var types = ChaosAnomalyTypes.ForFormat(LoadFileFormat.Concordance);
        Assert.Equal(ChaosAnomalyTypes.Concordance, types);
    }

    [Fact]
    public void ForFormat_EdrmXml_ReturnsEdrmXmlTypes()
    {
        var types = ChaosAnomalyTypes.ForFormat(LoadFileFormat.EdrmXml);
        Assert.Equal(ChaosAnomalyTypes.EdrmXml, types);
    }
}
