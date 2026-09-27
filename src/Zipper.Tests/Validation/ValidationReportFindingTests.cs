using System.Text.Json;
using Xunit;
using Zipper.Validation;

namespace Zipper.Tests;

public class ValidationReportFindingTests
{
    [Fact]
    public void Error_WithLine_InitializesAllPropertiesAndEnforcesErrorSeverity()
    {
        var finding = ValidationReportFinding.Error(
            code: "UniqueId",
            path: "DATA/loadfile.dat",
            message: "Duplicate DOCID: 'DOC001'",
            line: 42);

        Assert.Equal("UniqueId", finding.Code);
        Assert.Equal("error", finding.Severity);
        Assert.Equal("DATA/loadfile.dat", finding.Path);
        Assert.Equal(42L, finding.Line);
        Assert.Equal("Duplicate DOCID: 'DOC001'", finding.Message);
    }

    [Fact]
    public void Error_WithoutLine_LeavesLineNull()
    {
        var finding = ValidationReportFinding.Error(
            code: "PathExistence",
            path: "_manifest.json",
            message: "Manifest missing.");

        Assert.Equal("PathExistence", finding.Code);
        Assert.Equal("error", finding.Severity);
        Assert.Equal("_manifest.json", finding.Path);
        Assert.Null(finding.Line);
        Assert.Equal("Manifest missing.", finding.Message);
    }

    [Fact]
    public void Warning_EnforcesWarningSeverity()
    {
        var finding = ValidationReportFinding.Warning(
            code: "CustomWarning",
            path: "DATA/loadfile.opt",
            message: "Warning message",
            line: 10);

        Assert.Equal("CustomWarning", finding.Code);
        Assert.Equal("warning", finding.Severity);
        Assert.Equal("DATA/loadfile.opt", finding.Path);
        Assert.Equal(10L, finding.Line);
        Assert.Equal("Warning message", finding.Message);
    }

    [Fact]
    public void JsonSerialization_RoundtripsIdentically()
    {
        var original = ValidationReportFinding.Error(
            code: "BatesConsistency",
            path: "DATA/loadfile.dat",
            message: "Bates range inconsistency",
            line: 5);

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ValidationReportFinding>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Code, deserialized.Code);
        Assert.Equal(original.Severity, deserialized.Severity);
        Assert.Equal(original.Path, deserialized.Path);
        Assert.Equal(original.Line, deserialized.Line);
        Assert.Equal(original.Message, deserialized.Message);
    }
}
