using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

public class ProductionSetPlannerTests
{
    [Fact]
    public void Plan_WhenBatesConfigIsNull_ThrowsInvalidOperationException()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Bates = null;
        request.Output = request.Output with { FileCount = 10 };
        request.Production = request.Production with { VolumeSize = 5 };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => ProductionSetPlanner.Plan(request));
        Assert.Equal("Production set requires Bates configuration.", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Plan_WhenFileCountIsZeroOrNegative_ThrowsInvalidOperationException(long fileCount)
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Bates = new BatesNumberConfig { Prefix = "PR", Digits = 6 };
        request.Output = request.Output with { FileCount = fileCount };
        request.Production = request.Production with { VolumeSize = 5 };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => ProductionSetPlanner.Plan(request));
        Assert.Equal("Production set requires FileCount > 0.", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Plan_WhenVolumeSizeIsZeroOrNegative_ThrowsInvalidOperationException(int volumeSize)
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Bates = new BatesNumberConfig { Prefix = "PR", Digits = 6 };
        request.Output = request.Output with { FileCount = 10 };
        request.Production = request.Production with { VolumeSize = volumeSize };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => ProductionSetPlanner.Plan(request));
        Assert.Equal("Production set requires VolumeSize > 0.", exception.Message);
    }

    [Fact]
    public void Plan_GeneratesCorrectDocumentPlansAndVolumeBoundaries()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Bates = new BatesNumberConfig
        {
            Prefix = "PROD",
            Digits = 4,
            Start = 10
        };
        request.Output = request.Output with
        {
            FileCount = 5,
            FileType = "TXT" // FileTypeLower should be "txt"
        };
        request.Production = request.Production with
        {
            VolumeSize = 2 // 2 files per volume, so 5 files will span 3 volumes (VOL001: 2, VOL002: 2, VOL003: 1)
        };

        // Act
        var plans = ProductionSetPlanner.Plan(request);

        // Assert
        Assert.NotNull(plans);
        Assert.Equal(5, plans.Count);

        // File 0: Index 0, Vol 1
        Assert.Equal(0, plans[0].Index);
        Assert.Equal(1, plans[0].VolumeIndex);
        Assert.Equal("VOL001", plans[0].VolumeName);
        Assert.Equal("PROD0010", plans[0].BatesNumber);
        Assert.Equal(Path.Combine("NATIVES", "VOL001", "PROD0010.txt"), plans[0].NativeRelPath);
        Assert.Equal(Path.Combine("TEXT", "VOL001", "PROD0010.txt"), plans[0].TextRelPath);
        Assert.Equal(Path.Combine("IMAGES", "VOL001", "PROD0010.tif"), plans[0].ImageRelPath);

        // File 1: Index 1, Vol 1
        Assert.Equal(1, plans[1].Index);
        Assert.Equal(1, plans[1].VolumeIndex);
        Assert.Equal("VOL001", plans[1].VolumeName);
        Assert.Equal("PROD0011", plans[1].BatesNumber);
        Assert.Equal(Path.Combine("NATIVES", "VOL001", "PROD0011.txt"), plans[1].NativeRelPath);
        Assert.Equal(Path.Combine("TEXT", "VOL001", "PROD0011.txt"), plans[1].TextRelPath);
        Assert.Equal(Path.Combine("IMAGES", "VOL001", "PROD0011.tif"), plans[1].ImageRelPath);

        // File 2: Index 2, Vol 2 (Volume boundary transition)
        Assert.Equal(2, plans[2].Index);
        Assert.Equal(2, plans[2].VolumeIndex);
        Assert.Equal("VOL002", plans[2].VolumeName);
        Assert.Equal("PROD0012", plans[2].BatesNumber);
        Assert.Equal(Path.Combine("NATIVES", "VOL002", "PROD0012.txt"), plans[2].NativeRelPath);
        Assert.Equal(Path.Combine("TEXT", "VOL002", "PROD0012.txt"), plans[2].TextRelPath);
        Assert.Equal(Path.Combine("IMAGES", "VOL002", "PROD0012.tif"), plans[2].ImageRelPath);

        // File 3: Index 3, Vol 2
        Assert.Equal(3, plans[3].Index);
        Assert.Equal(2, plans[3].VolumeIndex);
        Assert.Equal("VOL002", plans[3].VolumeName);
        Assert.Equal("PROD0013", plans[3].BatesNumber);
        Assert.Equal(Path.Combine("NATIVES", "VOL002", "PROD0013.txt"), plans[3].NativeRelPath);
        Assert.Equal(Path.Combine("TEXT", "VOL002", "PROD0013.txt"), plans[3].TextRelPath);
        Assert.Equal(Path.Combine("IMAGES", "VOL002", "PROD0013.tif"), plans[3].ImageRelPath);

        // File 4: Index 4, Vol 3
        Assert.Equal(4, plans[4].Index);
        Assert.Equal(3, plans[4].VolumeIndex);
        Assert.Equal("VOL003", plans[4].VolumeName);
        Assert.Equal("PROD0014", plans[4].BatesNumber);
        Assert.Equal(Path.Combine("NATIVES", "VOL003", "PROD0014.txt"), plans[4].NativeRelPath);
        Assert.Equal(Path.Combine("TEXT", "VOL003", "PROD0014.txt"), plans[4].TextRelPath);
        Assert.Equal(Path.Combine("IMAGES", "VOL003", "PROD0014.tif"), plans[4].ImageRelPath);
    }

    [Fact]
    public void Plan_VerifyPathAndBatesStructure()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Bates = new BatesNumberConfig
        {
            Prefix = "ABC",
            Digits = 6,
            Start = 1
        };
        request.Output = request.Output with
        {
            FileCount = 1,
            FileType = "eml"
        };
        request.Production = request.Production with
        {
            VolumeSize = 100
        };

        // Act
        var plans = ProductionSetPlanner.Plan(request);

        // Assert
        Assert.Single(plans);
        var plan = plans[0];

        Assert.Equal(0, plan.Index);
        Assert.Equal(1, plan.VolumeIndex);
        Assert.Equal("VOL001", plan.VolumeName);
        Assert.Equal("ABC000001", plan.BatesNumber);

        // Verify paths
        var expectedNative = Path.Combine("NATIVES", "VOL001", "ABC000001.eml");
        var expectedText = Path.Combine("TEXT", "VOL001", "ABC000001.txt");
        var expectedImage = Path.Combine("IMAGES", "VOL001", "ABC000001.tif");

        Assert.Equal(expectedNative, plan.NativeRelPath);
        Assert.Equal(expectedText, plan.TextRelPath);
        Assert.Equal(expectedImage, plan.ImageRelPath);
    }
}
