using Xunit;
using Zipper.Profiles.Generation;

namespace Zipper.Tests;

/// <summary>
/// Tests for the standard-row value generators used by the DAT Standard composer's profile path
/// (ADR-0004 registry, issue #747).
/// </summary>
public class StandardRowValueGeneratorsTests
{
    private static StandardRowResolution MakeResolution(
        string controlIdentity = "CTRL42",
        string batesIdentity = "BATES42",
        string nativePath = "NATIVES/001/DOC00000001.pdf",
        string? fileSize = null,
        string textPath = "TEXT/DOC00000001.txt",
        int pageCount = 7,
        bool isChild = false,
        bool withFamilies = false,
        bool withText = false,
        string begAttach = "B1",
        string endAttach = "E1",
        string parentDocId = "P1")
        => new()
        {
            ControlIdentity = controlIdentity,
            BatesIdentity = batesIdentity,
            NativePath = nativePath,
            FileSize = fileSize,
            TextPath = textPath,
            PageCount = pageCount,
            IsChild = isChild,
            WithFamilies = withFamilies,
            WithText = withText,
            BegAttach = begAttach,
            EndAttach = endAttach,
            ParentDocId = parentDocId,
        };

    private static ColumnGenerationContext MakeContext(StandardRowResolution resolution, string? profileValue = null)
    {
#pragma warning disable S2245
        return new ColumnGenerationContext
        {
            NativeFileIndex = 1,
            FolderNumber = 1,
            DocumentIndex = 0,
            Seeded = new Random(42),
            Now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            FileData = new FileData
            {
                WorkItem = new FileWorkItem { Index = 1, FolderNumber = 1 },
                DataLength = 2048,
                PageCount = 7,
            },
            StandardRow = resolution,
            ProfileValue = profileValue,
        };
#pragma warning restore S2245
    }

    [Fact]
    public void ControlNumberGenerator_ReturnsControlIdentity()
    {
        var gen = new ControlNumberGenerator();
        Assert.Equal("CTRL42", gen.Generate(MakeContext(MakeResolution(controlIdentity: "CTRL42"))));
    }

    [Fact]
    public void BatesNumberGenerator_ReturnsBatesIdentity()
    {
        var gen = new BatesNumberGenerator();
        Assert.Equal("BATES42", gen.Generate(MakeContext(MakeResolution(batesIdentity: "BATES42"))));
    }

    [Fact]
    public void NativePathGenerator_ReturnsNativePath()
    {
        var gen = new NativePathGenerator();
        Assert.Equal("NATIVES/001/DOC00000001.pdf", gen.Generate(MakeContext(MakeResolution(nativePath: "NATIVES/001/DOC00000001.pdf"))));
    }

    [Fact]
    public void FileSizeGenerator_FileSizePreferred_OverProfileValue()
    {
        var gen = new FileSizeGenerator();
        var ctx = MakeContext(MakeResolution(fileSize: "2048"), profileValue: "9999");
        Assert.Equal("2048", gen.Generate(ctx));
    }

    [Fact]
    public void FileSizeGenerator_MissingFileSize_FallsBackToProfileValue()
    {
        var gen = new FileSizeGenerator();
        var ctx = MakeContext(MakeResolution(fileSize: null), profileValue: "9999");
        Assert.Equal("9999", gen.Generate(ctx));
    }

    [Fact]
    public void FileSizeGenerator_MissingBoth_ReturnsEmpty()
    {
        var gen = new FileSizeGenerator();
        Assert.Equal(string.Empty, gen.Generate(MakeContext(MakeResolution(fileSize: null))));
    }

    [Fact]
    public void BegAttachGenerator_WithFamilies_UsesContextValue()
    {
        var gen = new FamilyValueGenerator(s => s.BegAttach);
        var ctx = MakeContext(MakeResolution(withFamilies: true, begAttach: "B1"), profileValue: "PV");
        Assert.Equal("B1", gen.Generate(ctx));
    }

    [Fact]
    public void BegAttachGenerator_WithoutFamilies_UsesProfileValue()
    {
        var gen = new FamilyValueGenerator(s => s.BegAttach);
        var ctx = MakeContext(MakeResolution(withFamilies: false, begAttach: "B1"), profileValue: "PV");
        Assert.Equal("PV", gen.Generate(ctx));
    }

    [Fact]
    public void EndAttachGenerator_WithFamilies_UsesContextValue()
    {
        var gen = new FamilyValueGenerator(s => s.EndAttach);
        var ctx = MakeContext(MakeResolution(withFamilies: true, endAttach: "E1"), profileValue: "PV");
        Assert.Equal("E1", gen.Generate(ctx));
    }

    [Fact]
    public void EndAttachGenerator_WithoutFamilies_UsesProfileValue()
    {
        var gen = new FamilyValueGenerator(s => s.EndAttach);
        var ctx = MakeContext(MakeResolution(withFamilies: false, endAttach: "E1"), profileValue: "PV");
        Assert.Equal("PV", gen.Generate(ctx));
    }

    [Fact]
    public void ParentDocIdGenerator_WithFamilies_UsesContextValue()
    {
        var gen = new FamilyValueGenerator(s => s.ParentDocId);
        var ctx = MakeContext(MakeResolution(withFamilies: true, parentDocId: "P1"), profileValue: "PV");
        Assert.Equal("P1", gen.Generate(ctx));
    }

    [Fact]
    public void ParentDocIdGenerator_WithoutFamilies_UsesProfileValue()
    {
        var gen = new FamilyValueGenerator(s => s.ParentDocId);
        var ctx = MakeContext(MakeResolution(withFamilies: false, parentDocId: "P1"), profileValue: "PV");
        Assert.Equal("PV", gen.Generate(ctx));
    }

    [Fact]
    public void ProfileValueUnlessChildGenerator_Child_ReturnsEmpty()
    {
        var gen = new ProfileValueUnlessChildGenerator();
        var ctx = MakeContext(MakeResolution(isChild: true), profileValue: "X");
        Assert.Equal(string.Empty, gen.Generate(ctx));
    }

    [Fact]
    public void ProfileValueUnlessChildGenerator_NotChild_ReturnsProfileValue()
    {
        var gen = new ProfileValueUnlessChildGenerator();
        var ctx = MakeContext(MakeResolution(isChild: false), profileValue: "X");
        Assert.Equal("X", gen.Generate(ctx));
    }

    [Fact]
    public void PageCountGenerator_Child_ReturnsOne()
    {
        var gen = new PageCountGenerator();
        var ctx = MakeContext(MakeResolution(isChild: true, pageCount: 7));
        Assert.Equal("1", gen.Generate(ctx));
    }

    [Fact]
    public void PageCountGenerator_NotChild_ReturnsPageCount()
    {
        var gen = new PageCountGenerator();
        var ctx = MakeContext(MakeResolution(isChild: false, pageCount: 7));
        Assert.Equal("7", gen.Generate(ctx));
    }

    [Fact]
    public void TextPathGenerator_WithText_UsesResolvedTextPath()
    {
        var gen = new TextPathGenerator();
        var ctx = MakeContext(MakeResolution(withText: true, textPath: "TEXT/DOC00000001.txt"), profileValue: "PV");
        Assert.Equal("TEXT/DOC00000001.txt", gen.Generate(ctx));
    }

    [Fact]
    public void TextPathGenerator_WithoutText_UsesProfileValue()
    {
        var gen = new TextPathGenerator();
        var ctx = MakeContext(MakeResolution(withText: false, textPath: "TEXT/DOC00000001.txt"), profileValue: "PV");
        Assert.Equal("PV", gen.Generate(ctx));
    }

    [Fact]
    public void ByName_ContainsEveryProfilePathSwitchAlias()
    {
        foreach (var name in new[]
        {
            "DOCID", "CONTROLNUMBER", "CONTROL_NUMBER", "CONTROL NUMBER",
            "BEGBATES", "ENDBATES",
            "FILEPATH", "FILE_PATH", "FILE PATH", "NATIVEPATH", "NATIVE_PATH", "NATIVE PATH",
            "FILESIZE", "FILE_SIZE", "FILE SIZE",
            "BEGATTACH", "BEG_ATTACH", "BEG ATTACH",
            "ENDATTACH", "END_ATTACH", "END ATTACH",
            "PARENTDOCID", "PARENT_DOC_ID", "PARENT DOC ID",
            "DATESENT", "DATE_SENT", "DATE SENT", "AUTHOR",
            "EMAILTO", "EMAIL_TO", "EMAIL TO",
            "EMAILFROM", "EMAIL_FROM", "EMAIL FROM",
            "EMAILCC", "EMAIL_CC", "EMAIL CC",
            "EMAILSUBJECT", "EMAIL_SUBJECT", "EMAIL SUBJECT",
            "EMAILSENTDATE", "EMAIL_SENT_DATE", "EMAIL SENT DATE",
            "EMAILATTACHMENT", "EMAIL_ATTACHMENT", "EMAIL ATTACHMENT",
            "PAGECOUNT", "PAGE_COUNT", "PAGE COUNT",
            "TEXTPATH", "TEXT_PATH", "TEXT PATH",
        })
        {
            Assert.True(StandardRowValueGenerators.ByName.ContainsKey(name), $"registry missing alias: {name}");
        }
    }

    [Fact]
    public void ByName_BindsAliasesToExpectedGenerators()
    {
        Assert.IsType<ControlNumberGenerator>(StandardRowValueGenerators.ByName["CONTROL NUMBER"]);
        Assert.IsType<BatesNumberGenerator>(StandardRowValueGenerators.ByName["BEGBATES"]);
        Assert.IsType<NativePathGenerator>(StandardRowValueGenerators.ByName["NATIVE PATH"]);
        Assert.IsType<FileSizeGenerator>(StandardRowValueGenerators.ByName["FILE SIZE"]);
        Assert.IsType<FamilyValueGenerator>(StandardRowValueGenerators.ByName["BEGATTACH"]);
        Assert.IsType<FamilyValueGenerator>(StandardRowValueGenerators.ByName["ENDATTACH"]);
        Assert.IsType<FamilyValueGenerator>(StandardRowValueGenerators.ByName["PARENTDOCID"]);
        Assert.IsType<ProfileValueUnlessChildGenerator>(StandardRowValueGenerators.ByName["DATESENT"]);
        Assert.IsType<ProfileValueUnlessChildGenerator>(StandardRowValueGenerators.ByName["AUTHOR"]);
        Assert.IsType<PageCountGenerator>(StandardRowValueGenerators.ByName["PAGE COUNT"]);
        Assert.IsType<TextPathGenerator>(StandardRowValueGenerators.ByName["TEXT PATH"]);
    }

    [Fact]
    public void ByName_DoesNotContainHashOrArbitraryColumns()
    {
        Assert.False(StandardRowValueGenerators.ByName.ContainsKey("MD5HASH"));
        Assert.False(StandardRowValueGenerators.ByName.ContainsKey("SHA256HASH"));
        Assert.False(StandardRowValueGenerators.ByName.ContainsKey("CUSTODIAN"));
        Assert.False(StandardRowValueGenerators.ByName.ContainsKey("DOESNOTEXIST"));
    }
}
