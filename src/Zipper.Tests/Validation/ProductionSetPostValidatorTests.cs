using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.Validation;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public sealed class ProductionSetPostValidatorTests : IDisposable
{
    private const char ColDelim = '\x14';
    private const char QuoteDelim = '\xfe';
    private readonly string testOutputPath;

    public ProductionSetPostValidatorTests()
    {
        this.testOutputPath = Path.Combine(Path.GetTempPath(), $"zipper_dat_opt_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.testOutputPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.testOutputPath))
        {
            try
            {
                Directory.Delete(this.testOutputPath, true);
            }
            catch
            {
                // Best-effort cleanup in temp
            }
        }
    }

    private FileGenerationRequest CreateRequest(int fileCount = 2)
    {
        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = this.testOutputPath,
                FileCount = fileCount,
                FileType = "pdf",
            },
            Production = new ProductionConfig
            {
                ProductionSet = true,
                VolumeSize = 10,
            },
            Metadata = new MetadataConfig { Seed = 42 },
            Bates = new BatesNumberConfig
            {
                Prefix = "TEST",
                Start = 1,
                Digits = 8,
            },
        };
    }

    private void SetupProductionSet(
        string prodDir,
        List<string> datRows,
        List<string> optRows,
        List<string>? imageFiles = null)
    {
        var dataDir = Path.Combine(prodDir, "DATA");
        var imagesDir = Path.Combine(prodDir, "IMAGES", "VOL001");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(imagesDir);

        File.WriteAllLines(Path.Combine(dataDir, "loadfile.dat"), datRows, Encoding.UTF8);
        File.WriteAllLines(Path.Combine(dataDir, "loadfile.opt"), optRows, Encoding.UTF8);

        imageFiles ??= ["DOC00000001.tif", "DOC00000002.tif"];
        foreach (var img in imageFiles)
        {
            var imgPath = Path.Combine(prodDir, img.Replace('\\', '/'));
            var dir = Path.GetDirectoryName(imgPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllBytes(imgPath, [0x49, 0x49, 0x2A, 0x00]); // TIFF magic bytes
        }
    }

    private static string JoinDat(params string[] fields)
    {
        var quote = QuoteDelim.ToString();
        var col = ColDelim.ToString();
        return string.Join(col, fields.Select(f => $"{quote}{f}{quote}"));
    }

    [Theory]
    [InlineData("Y")]
    [InlineData("1")]
    public void Validate_MatchingDatAndOpt_PassesWithoutErrors(string documentBreak)
    {
        var prodDir = Path.Combine(this.testOutputPath, "matching_set");
        var request = this.CreateRequest(2);

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH", "PAGECOUNT"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif", "1"),
            JoinDat("DOC00000002", "TEST00000002", "IMAGES\\VOL001\\DOC00000002.tif", "2"),
        };

        var optRows = new List<string>
        {
            $"TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,{documentBreak},,,1",
            $"TEST00000002,VOL001,IMAGES\\VOL001\\DOC00000002_001.tif,{documentBreak},,,2",
            "TEST00000002_002,VOL001,IMAGES\\VOL001\\DOC00000002_002.tif,,,,",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            imageFiles:
            [
                "IMAGES/VOL001/DOC00000001.tif",
                "IMAGES/VOL001/DOC00000002_001.tif",
                "IMAGES/VOL001/DOC00000002_002.tif",
            ]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("passed", report.Status);
        Assert.Equal(0, report.ErrorCount);
    }

    [Fact]
    public void Validate_OptDocumentsReuseDatMatch_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "opt_documents_reuse_dat_match");
        var request = this.CreateRequest(1);
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH", "PAGECOUNT"),
            JoinDat("DOC00000001", "TEST00000001", "VOL001", "IMAGES\\VOL001\\DOC00000001.tif", "1"),
        };
        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
            "TEST00000001_001,VOL001,IMAGES\\VOL001\\DOC00000001_001.tif,Y,,,1",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            ["IMAGES/VOL001/DOC00000001.tif", "IMAGES/VOL001/DOC00000001_001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "OptBoundary"
            && f.Message.Contains("already matched", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MatchingDatAndOptAcrossVolumes_PassesWithoutErrors()
    {
        var prodDir = Path.Combine(this.testOutputPath, "matching_sets_across_volumes");
        var request = this.CreateRequest(2);
        request.Production = request.Production! with { VolumeSize = 1 };
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH"),
            JoinDat("TEST00000001", "TEST00000001", "VOL001", "IMAGES\\VOL001\\TEST00000001.tif"),
            JoinDat("TEST00000002", "TEST00000002", "VOL002", "IMAGES\\VOL002\\TEST00000002.tif"),
        };
        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\TEST00000001.tif,Y,,,1",
            "TEST00000002,VOL002,IMAGES\\VOL002\\TEST00000002.tif,Y,,,1",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            ["IMAGES/VOL001/TEST00000001.tif", "IMAGES/VOL002/TEST00000002.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("passed", report.Status);
        Assert.Equal(0, report.ErrorCount);
    }

    [Fact]
    public void Validate_CrossReference_BatesKeyWinsDocIdCollision_Passes()
    {
        var prodDir = Path.Combine(this.testOutputPath, "bates_key_wins_docid_collision");
        var request = this.CreateRequest(2);
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH", "PAGECOUNT"),
            JoinDat("TEST00000002", "TEST00000001", "VOL001", "IMAGES\\VOL001\\DOC00000001.tif", "1"),
            JoinDat("TEST00000003", "TEST00000002", "VOL001", "IMAGES\\VOL001\\DOC00000002.tif", "1"),
        };
        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
            "TEST00000002,VOL001,IMAGES\\VOL001\\DOC00000002.tif,Y,,,1",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            ["IMAGES/VOL001/DOC00000001.tif", "IMAGES/VOL001/DOC00000002.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("passed", report.Status);
        Assert.Equal(0, report.ErrorCount);
    }

    [Fact]
    public void Validate_CrossReference_BatesPresentDoesNotFallbackToDocId_ReportsMissingOptDocument()
    {
        var prodDir = Path.Combine(this.testOutputPath, "bates_present_no_docid_fallback");
        var request = this.CreateRequest(2);
        const string imagePath = "IMAGES\\VOL001\\DOC00000001.tif";
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH", "PAGECOUNT"),
            JoinDat("DOC00000001", "TEST00000001", "VOL001", imagePath, "1"),
            JoinDat("TEST00000001", "TEST00000002", "VOL001", imagePath, "1"),
        };
        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "OptBoundary"
            && f.Message.Contains("DAT document 'TEST00000001' does not exist in OPT load file.", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SortOrderWithDocIdBatesCollision_Fails()
    {
        var prodDir = Path.Combine(this.testOutputPath, "sort_order_docid_bates_collision");
        var request = this.CreateRequest(2);
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH", "PAGECOUNT"),
            JoinDat("TEST00000002", "TEST00000001", "VOL001", "IMAGES\\VOL001\\DOC00000001.tif", "1"),
            JoinDat("TEST00000001", "TEST00000002", "VOL001", "IMAGES\\VOL001\\DOC00000002.tif", "1"),
        };
        var optRows = new List<string>
        {
            "TEST00000002,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000002.tif,Y,,,1",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            ["IMAGES/VOL001/DOC00000001.tif", "IMAGES/VOL001/DOC00000002.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "BatesConsistency"
            && f.Message.Contains("sort order mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MatchingDatAndOptWithAttachmentChild_PassesWithoutErrors()
    {
        var prodDir = Path.Combine(this.testOutputPath, "matching_set_with_attachment");
        var request = this.CreateRequest(1);
        request.Metadata = request.Metadata! with { WithFamilies = true };
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH", "PARENTDOCID"),
            JoinDat("TEST00000001", "TEST00000001", "VOL001", "IMAGES\\VOL001\\TEST00000001.tif", string.Empty),
            JoinDat("TEST00000001_A001", "TEST00000001_A001", "VOL001", "IMAGES\\VOL001\\TEST00000001_A001.tif", "TEST00000001"),
        };
        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\TEST00000001.tif,Y,,,1",
            "TEST00000001_A001,VOL001,IMAGES\\VOL001\\TEST00000001_A001.tif,Y,,,1",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            ["IMAGES/VOL001/TEST00000001.tif", "IMAGES/VOL001/TEST00000001_A001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("passed", report.Status);
        Assert.Equal(0, report.ErrorCount);
    }

    [Fact]
    public void Validate_MissingDocumentIdentityColumns_ReportsCrossReferenceWithoutThrowing()
    {
        var prodDir = Path.Combine(this.testOutputPath, "missing_document_identity_columns");
        var request = this.CreateRequest(1);
        const string imagePath = "IMAGES\\VOL001\\DOC00000001.tif";

        var datRows = new List<string>
        {
            JoinDat("VOLUME", "IMAGE_PATH"),
            JoinDat("VOL001", imagePath),
        };

        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "OptBoundary"
            && f.Message.Contains("does not exist in OPT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_CrossReference_UsesDocIdWhenBatesNumberIsMissing_Passes()
    {
        var prodDir = Path.Combine(this.testOutputPath, "docid_fallback_without_bates");
        var request = this.CreateRequest(1);
        const string imagePath = "IMAGES\\VOL001\\DOC00000001.tif";
        var datRows = new List<string>
        {
            JoinDat("DOCID", "VOLUME", "IMAGE_PATH", "PAGECOUNT"),
            JoinDat("DOC00000001", "VOL001", imagePath, "1"),
        };
        var optRows = new List<string>
        {
            $"DOC00000001,VOL001,{imagePath},Y,,,1",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("passed", report.Status);
        Assert.Equal(0, report.ErrorCount);
    }

    [Fact]
    public void Validate_PageCountDesync_DatMismatch_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "pagecount_dat_mismatch");
        var request = this.CreateRequest(1);

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH", "PAGECOUNT"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif", "3"), // DAT claims 3
        };

        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1", // OPT has only 1 page
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        var finding = Assert.Single(report.Findings, f => f.Code == "OptBoundary" && f.Message.Contains("page count mismatch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("DAT specifies 3", finding.Message, StringComparison.Ordinal);
        Assert.Contains("1", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Validate_PageCountDesync_DatNonPositivePageCount_FailsWithActionableMessage(string pageCount)
    {
        ArgumentNullException.ThrowIfNull(pageCount);

        var prodDir = Path.Combine(this.testOutputPath, $"pagecount_dat_nonpositive_{pageCount}");
        var request = this.CreateRequest(1);
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH", "PAGECOUNT"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif", pageCount),
        };
        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "InvalidValue"
            && f.Message.Contains("Invalid page count", StringComparison.OrdinalIgnoreCase)
            && f.Message.Contains(pageCount, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_PageCountDesync_OptDeclaredMismatch_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "pagecount_opt_declared_mismatch");
        var request = this.CreateRequest(1);

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif"),
        };

        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,5", // Declared 5 but only 1 row
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        var finding = Assert.Single(report.Findings, f => f.Code == "OptBoundary" && f.Message.Contains("page count mismatch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("declared 5", finding.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1", finding.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Validate_PageCountDesync_OptInvalidPageCount_FailsWithActionableMessage(string pageCount)
    {
        ArgumentNullException.ThrowIfNull(pageCount);

        var prodDir = Path.Combine(this.testOutputPath, $"pagecount_opt_invalid_{pageCount}");
        var request = this.CreateRequest(1);

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif"),
        };

        var optRows = new List<string>
        {
            $"TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,{pageCount}",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        var finding = Assert.Single(report.Findings, f => f.Code == "OptBoundary" && f.Message.Contains("invalid page count", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(pageCount, finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_PageCountDesync_OptMissingPageCount_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "pagecount_opt_missing");
        var request = this.CreateRequest(1);
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif"),
        };
        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "OptBoundary"
            && f.Message.Contains("has no page count", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_PageCountDesync_ContinuationPageSpecifiesPageCount_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "pagecount_continuation_nonzero");
        var request = this.CreateRequest(1);

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001_001.tif"),
        };

        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001_001.tif,Y,,,2",
            "TEST00000001_002,VOL001,IMAGES\\VOL001\\DOC00000001_002.tif,,,,2", // Continuation page must not declare page count
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            ["IMAGES/VOL001/DOC00000001_001.tif", "IMAGES/VOL001/DOC00000001_002.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f => f.Code == "OptBoundary" && f.Message.Contains("not a document break", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_OptContinuationWithoutDocumentBreak_ReportsBoundary()
    {
        var prodDir = Path.Combine(this.testOutputPath, "continuation_without_document_break");
        var request = this.CreateRequest(1);
        const string imagePath = "IMAGES\\VOL001\\TEST00000001.tif";
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH"),
            JoinDat("TEST00000001", "TEST00000001", "VOL001", imagePath),
        };
        var optRows = new List<string>
        {
            "TEST00000001_002,VOL001,IMAGES\\VOL001\\TEST00000001_002.tif,,,,",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            ["IMAGES/VOL001/TEST00000001_001.tif", "IMAGES/VOL001/TEST00000001_002.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "OptBoundary"
            && f.Message.Contains("without a preceding document break", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_OptShortRow_ReportsColumnCountWithoutThrowing()
    {
        var prodDir = Path.Combine(this.testOutputPath, "opt_short_row");
        var request = this.CreateRequest(1);
        const string imagePath = "IMAGES\\VOL001\\TEST00000001.tif";
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH"),
            JoinDat("TEST00000001", "TEST00000001", "VOL001", imagePath),
        };
        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\TEST00000001.tif,Y",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/TEST00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "OptBoundary"
            && f.Message.Contains("has 4 columns, expected 7", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_PathDesync_OptPathTraversal_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "path_opt_traversal");
        var request = this.CreateRequest(1);

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif"),
        };

        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\..\\..\\invalid\\path.tif,Y,,,1",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f => f.Code == "PathReconciliation" && f.Message.Contains("traversal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_PathDesync_OptPathRooted_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "path_opt_rooted");
        var request = this.CreateRequest(1);

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif"),
        };

        var optRows = new List<string>
        {
            "TEST00000001,VOL001,/IMAGES/VOL001/DOC00000001.tif,Y,,,1",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f => f.Code == "PathReconciliation" && f.Message.Contains("relative path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_PathDesync_DatPathTraversal_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "path_dat_traversal");
        var request = this.CreateRequest(1);
        const string datImagePath = "IMAGES\\VOL001\\..\\DOC00000001.tif";
        const string optImagePath = "IMAGES\\VOL001\\DOC00000001.tif";
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "VOL001", datImagePath),
        };
        var optRows = new List<string>
        {
            $"TEST00000001,VOL001,{optImagePath},Y,,,1",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "PathReconciliation"
            && f.Path == ProductionSetPostValidator.DatRelativePath
            && f.Message.Contains("DAT image path", StringComparison.Ordinal)
            && f.Message.Contains("traversal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_PathDesync_DatAndOptImagePathsDisagree_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "path_dat_opt_disagree");
        var request = this.CreateRequest(1);

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif"),
        };

        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\COMPLETELY_DIFFERENT.tif,Y,,,1",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            ["IMAGES/VOL001/DOC00000001.tif", "IMAGES/VOL001/COMPLETELY_DIFFERENT.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        var finding = Assert.Single(report.Findings, f => f.Code == "PathReconciliation" && f.Message.Contains("Image path mismatch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("DOC00000001", finding.Message, StringComparison.Ordinal);
        Assert.Contains("DOC00000001.tif", finding.Message, StringComparison.Ordinal);
        Assert.Contains("COMPLETELY_DIFFERENT.tif", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_OrderDesync_DatAndOptRecordOrderMisaligned_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "order_misaligned");
        var request = this.CreateRequest(2);

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif"),
            JoinDat("DOC00000002", "TEST00000002", "IMAGES\\VOL001\\DOC00000002.tif"),
        };

        // Inverted order in OPT: DOC2 then DOC1
        var optRows = new List<string>
        {
            "TEST00000002,VOL001,IMAGES\\VOL001\\DOC00000002.tif,Y,,,1",
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            ["IMAGES/VOL001/DOC00000001.tif", "IMAGES/VOL001/DOC00000002.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        var finding = Assert.Single(report.Findings, f => f.Code == "BatesConsistency" && f.Message.Contains("sort order mismatch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("DOC00000001", finding.Message, StringComparison.Ordinal);
        Assert.Contains("TEST00000002", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_CrossReferenceDesync_DocumentInDatNotInOpt_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "xref_dat_not_in_opt");
        var request = this.CreateRequest(2);

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif"),
            JoinDat("DOC00000002", "TEST00000002", "IMAGES\\VOL001\\DOC00000002.tif"),
        };

        // OPT only has DOC1
        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            ["IMAGES/VOL001/DOC00000001.tif", "IMAGES/VOL001/DOC00000002.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        var finding = Assert.Single(report.Findings, f => f.Code == "OptBoundary" && f.Message.Contains("does not exist in OPT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("DOC00000002", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_CrossReferenceDesync_DocumentInOptNotInDat_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "xref_opt_not_in_dat");
        var request = this.CreateRequest(1);

        // DAT only has DOC1
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif"),
        };

        // OPT has DOC1 and an extra DOC2
        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
            "TEST00000002,VOL001,IMAGES\\VOL001\\DOC00000002.tif,Y,,,1",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            ["IMAGES/VOL001/DOC00000001.tif", "IMAGES/VOL001/DOC00000002.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        var finding = Assert.Single(report.Findings, f => f.Code == "OptBoundary" && f.Message.Contains("does not exist in DAT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("TEST00000002", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_CrossReferenceDesync_OrphanAttachmentInOpt_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "xref_orphan_attachment");
        var request = this.CreateRequest(1);
        const string parentImage = "IMAGES\\VOL001\\DOC00000001.tif";
        const string childImage = "IMAGES\\VOL001\\TEST00000001_A001.tif";

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", parentImage),
        };

        var optRows = new List<string>
        {
            $"TEST00000001,VOL001,{parentImage},Y,,,1",
            $"TEST00000001_A001,VOL001,{childImage},Y,,,1",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            ["IMAGES/VOL001/DOC00000001.tif", "IMAGES/VOL001/TEST00000001_A001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        var finding = Assert.Single(report.Findings, f => f.Code == "OptBoundary" && f.Message.Contains("does not exist in DAT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("TEST00000001_A001", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_MissingImagePathInEitherLoadFile_FailsWithActionableMessage(bool datPathIsEmpty)
    {
        var prodDir = Path.Combine(this.testOutputPath, $"missing_image_path_{datPathIsEmpty}");
        var request = this.CreateRequest(1);
        const string imagePath = "IMAGES\\VOL001\\DOC00000001.tif";

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "VOL001", datPathIsEmpty ? string.Empty : imagePath),
        };

        var optImagePath = datPathIsEmpty ? imagePath : string.Empty;
        var optRows = new List<string>
        {
            $"TEST00000001,VOL001,{optImagePath},Y,,,1",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "PathReconciliation"
            && f.Message.Contains(datPathIsEmpty ? "DAT" : "OPT", StringComparison.Ordinal)
            && f.Message.Contains("image path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ImagePathVolumeMismatch_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "image_path_volume_mismatch");
        var request = this.CreateRequest(1);
        const string imagePath = "IMAGES\\VOL002\\DOC00000001.tif";

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "VOL001", imagePath),
        };

        var optRows = new List<string>
        {
            $"TEST00000001,VOL001,{imagePath},Y,,,1",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL002/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "PathReconciliation"
            && f.Message.Contains("VOL001", StringComparison.Ordinal)
            && f.Message.Contains("VOL002", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_OptImagePath_WithBlankVolume_PassesWhenPathIdentifiesVolume()
    {
        var prodDir = Path.Combine(this.testOutputPath, "opt_blank_volume");
        var request = this.CreateRequest(1);
        const string imagePath = "IMAGES\\VOL001\\DOC00000001.tif";
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "VOL001", imagePath),
        };
        var optRows = new List<string>
        {
            $"TEST00000001,,{imagePath},Y,,,1",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("passed", report.Status);
        Assert.Equal(0, report.ErrorCount);
    }

    [Fact]
    public void Validate_DatImagePath_WithBlankVolume_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "dat_blank_volume");
        var request = this.CreateRequest(1);
        const string imagePath = "IMAGES\\VOL001\\DOC00000001.tif";
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", string.Empty, imagePath),
        };
        var optRows = new List<string>
        {
            $"TEST00000001,VOL001,{imagePath},Y,,,1",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "PathReconciliation"
            && f.Path == "DATA/loadfile.dat"
            && f.Message.Contains("empty Volume", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ImagePathWithoutVolumeDirectory_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "image_path_without_volume_directory");
        var request = this.CreateRequest(1);
        const string imagePath = "IMAGES\\DOC00000001.tif";
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "VOL001", imagePath),
        };
        var optRows = new List<string>
        {
            $"TEST00000001,VOL001,{imagePath},Y,,,1",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "PathReconciliation"
            && f.Message.Contains("not volume-relative", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ContinuationImageWithMissingFirstPagePath_DoesNotReportPathMismatch()
    {
        var prodDir = Path.Combine(this.testOutputPath, "continuation_image_with_missing_first_page_path");
        var request = this.CreateRequest(1);
        const string firstImagePath = "IMAGES\\VOL001\\TEST00000001_001.tif";
        const string continuationImagePath = "IMAGES\\VOL001\\TEST00000001_002.tif";
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH", "PAGECOUNT"),
            JoinDat("DOC00000001", "TEST00000001", "VOL001", firstImagePath, "2"),
        };
        var optRows = new List<string>
        {
            "TEST00000001_001,VOL001,,Y,,,2",
            $"TEST00000001_002,VOL001,{continuationImagePath},,,,",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            ["IMAGES/VOL001/TEST00000001_001.tif", "IMAGES/VOL001/TEST00000001_002.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "PathReconciliation"
            && f.Message.Contains("OPT image path is empty", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Findings, f =>
            f.Message.Contains("OPT continuation image path", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ContinuationImagePath_MismatchedDocument_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "continuation_image_path_mismatch");
        var request = this.CreateRequest(2);
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "VOL001", "IMAGES\\VOL001\\TEST00000001.tif"),
            JoinDat("DOC00000002", "TEST00000002", "VOL001", "IMAGES\\VOL001\\TEST00000002.tif"),
        };
        var optRows = new List<string>
        {
            "TEST00000001_001,VOL001,IMAGES\\VOL001\\TEST00000001_001.tif,Y,,,2",
            "TEST00000001_002,VOL001,IMAGES\\VOL001\\TEST00000002_002.tif,,,,",
            "TEST00000002_001,VOL001,IMAGES\\VOL001\\TEST00000002_001.tif,Y,,,2",
            "TEST00000002_002,VOL001,IMAGES\\VOL001\\TEST00000002_002.tif,,,,",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            [
                "IMAGES/VOL001/TEST00000001_001.tif",
                "IMAGES/VOL001/TEST00000001_002.tif",
                "IMAGES/VOL001/TEST00000002_001.tif",
                "IMAGES/VOL001/TEST00000002_002.tif",
            ]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        var finding = Assert.Single(report.Findings, f =>
            f.Code == "PathReconciliation"
            && f.Message.Contains("OPT continuation image path", StringComparison.Ordinal)
            && f.Message.Contains("TEST00000001_002", StringComparison.Ordinal)
            && f.Message.Contains("TEST00000002_002", StringComparison.Ordinal));
        Assert.Equal(2, finding.Line);
    }

    [Fact]
    public void Validate_ContinuationKeyMismatch_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "continuation_key_mismatch");
        var request = this.CreateRequest(1);
        const string firstImage = "IMAGES\\VOL001\\TEST00000001_001.tif";
        const string secondImage = "IMAGES\\VOL001\\TEST00000001_002.tif";
        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH", "PAGECOUNT"),
            JoinDat("DOC00000001", "TEST00000001", "VOL001", firstImage, "2"),
        };
        var optRows = new List<string>
        {
            $"TEST00000001_001,VOL001,{firstImage},Y,,,2",
            $"TEST00000001_003,VOL001,{secondImage},,,,",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            ["IMAGES/VOL001/TEST00000001_001.tif", "IMAGES/VOL001/TEST00000001_002.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        var finding = Assert.Single(report.Findings, f =>
            f.Code == "OptBoundary"
            && f.Message.Contains("expected page key 'TEST00000001_002'", StringComparison.Ordinal));
        Assert.Equal(2, finding.Line);
    }

    [Fact]
    public void Validate_PageCountReconciliation_WithUnderscoreBatesPrefix_PassesMatchingPages()
    {
        var prodDir = Path.Combine(this.testOutputPath, "underscore_bates_prefix");
        var request = this.CreateRequest(2);
        request.Bates = request.Bates! with { Prefix = "TEST_" };

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH", "PAGECOUNT"),
            JoinDat("TEST_00000001", "TEST_00000001", "IMAGES\\VOL001\\TEST_00000001.tif", "2"),
            JoinDat("TEST_00000002", "TEST_00000002", "IMAGES\\VOL001\\TEST_00000002.tif", "3"),
        };

        var optRows = new List<string>
        {
            "TEST_00000001_001,VOL001,IMAGES\\VOL001\\TEST_00000001_001.tif,Y,,,2",
            "TEST_00000001_002,VOL001,IMAGES\\VOL001\\TEST_00000001_002.tif,,,,",
            "TEST_00000002_001,VOL001,IMAGES\\VOL001\\TEST_00000002_001.tif,Y,,,3",
            "TEST_00000002_002,VOL001,IMAGES\\VOL001\\TEST_00000002_002.tif,,,,",
            "TEST_00000002_003,VOL001,IMAGES\\VOL001\\TEST_00000002_003.tif,,,,",
        };

        this.SetupProductionSet(
            prodDir,
            datRows,
            optRows,
            [
                "IMAGES/VOL001/TEST_00000001_001.tif",
                "IMAGES/VOL001/TEST_00000001_002.tif",
                "IMAGES/VOL001/TEST_00000002_001.tif",
                "IMAGES/VOL001/TEST_00000002_002.tif",
                "IMAGES/VOL001/TEST_00000002_003.tif",
            ]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("passed", report.Status);
        Assert.Equal(0, report.ErrorCount);
    }

    [Fact]
    public void Validate_DuplicateKey_DuplicateDocBreakInOpt_FailsWithActionableMessage()
    {
        var prodDir = Path.Combine(this.testOutputPath, "duplicate_doc_break_opt");
        var request = this.CreateRequest(1);

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif"),
        };

        // OPT has duplicate doc break for TEST00000001
        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f => f.Code == "UniqueId" && f.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ChaosOptPagecountAnomaly_DetectedByPostValidator()
    {
        var prodDir = Path.Combine(this.testOutputPath, "chaos_opt_pagecount");
        var request = this.CreateRequest(1);

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "IMAGE_PATH"),
            JoinDat("DOC00000001", "TEST00000001", "IMAGES\\VOL001\\DOC00000001.tif"),
        };

        const string cleanLine = "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1";
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        var corruptedLine = applier.Apply(1, cleanLine, "TEST00000001", "opt-pagecount", anomalies);

        Assert.NotEqual(cleanLine, corruptedLine);
        var anomaly = Assert.Single(anomalies);
        Assert.Equal("opt-pagecount", anomaly.ErrorType);

        var optRows = new List<string> { corruptedLine };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Findings, f =>
            f.Code == "OptBoundary"
            && f.Message.Contains("invalid page count", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("PAGECOUNT")]
    [InlineData("PAGE_COUNT")]
    [InlineData("PAGE COUNT")]
    [InlineData("PAGECNT")]
    [InlineData("PAGES")]
    [InlineData("PAGE_CNT")]
    public void Validate_DatPageCountHeaderAliases_RecognizedAndReconciled(string headerName)
    {
        ArgumentNullException.ThrowIfNull(headerName);

        var prodDir = Path.Combine(this.testOutputPath, $"pagecount_alias_{headerName.Replace(' ', '_')}");
        var request = this.CreateRequest(1);

        var datRows = new List<string>
        {
            JoinDat("DOCID", "BATES_NUMBER", "VOLUME", "IMAGE_PATH", headerName),
            JoinDat("DOC00000001", "TEST00000001", "VOL001", "IMAGES\\VOL001\\DOC00000001.tif", "1"),
        };

        var optRows = new List<string>
        {
            "TEST00000001,VOL001,IMAGES\\VOL001\\DOC00000001.tif,Y,,,1",
        };

        this.SetupProductionSet(prodDir, datRows, optRows, ["IMAGES/VOL001/DOC00000001.tif"]);

        var report = ProductionSetPostValidator.Validate(prodDir, request);

        Assert.Equal("passed", report.Status);
        Assert.DoesNotContain(report.Findings, f => f.Severity == "error");
    }
}
