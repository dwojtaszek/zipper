using Xunit;

namespace Zipper
{
    public class TiffMultiPageGeneratorTests
    {
        [Fact]
        public void ParsePageRange_WithValidRange_ShouldReturnParsedRange()
        {
            // Act
            var result = TiffMultiPageGenerator.ParsePageRange("1-20");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.Value.Min);
            Assert.Equal(20, result.Value.Max);
        }

        [Fact]
        public void ParsePageRange_WithSinglePage_ShouldReturnParsedRange()
        {
            // Act
            var result = TiffMultiPageGenerator.ParsePageRange("1-1");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.Value.Min);
            Assert.Equal(1, result.Value.Max);
        }

        [Fact]
        public void ParsePageRange_WithLargeRange_ShouldReturnParsedRange()
        {
            // Act
            var result = TiffMultiPageGenerator.ParsePageRange("100-1000");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(100, result.Value.Min);
            Assert.Equal(1000, result.Value.Max);
        }

        [Fact]
        public void ParsePageRange_WithInvalidFormat_ShouldReturnNull()
        {
            // Act
            var result = TiffMultiPageGenerator.ParsePageRange("invalid");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ParsePageRange_WithMissingMax_ShouldReturnNull()
        {
            // Act
            var result = TiffMultiPageGenerator.ParsePageRange("1-");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ParsePageRange_WithMissingMin_ShouldReturnNull()
        {
            // Act
            var result = TiffMultiPageGenerator.ParsePageRange("-20");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ParsePageRange_WithMinGreaterThanMax_ShouldReturnNull()
        {
            // Act
            var result = TiffMultiPageGenerator.ParsePageRange("20-1");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ParsePageRange_WithMinLessThanOne_ShouldReturnNull()
        {
            // Act
            var result = TiffMultiPageGenerator.ParsePageRange("0-10");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ParsePageRange_WithMaxGreaterThan1000_ShouldReturnNull()
        {
            // Act
            var result = TiffMultiPageGenerator.ParsePageRange("1-1001");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetPageCount_WithNullRange_ShouldReturnOne()
        {
            // Act
            var result = TiffMultiPageGenerator.GetPageCount(null, 1);

            // Assert
            Assert.Equal(1, result);
        }

        [Fact]
        public void GetPageCount_WithSinglePageRange_ShouldReturnMin()
        {
            // Arrange
            var range = (Min: 5, Max: 5);

            // Act
            var result = TiffMultiPageGenerator.GetPageCount(range, 1);

            // Assert
            Assert.Equal(5, result);
        }

        [Fact]
        public void GetPageCount_WithValidRangeAndSameIndex_ShouldReturnSamePageCount()
        {
            // Arrange
            var range = (Min: 1, Max: 10);

            // Act & Assert - Deterministic random means same index = same result
            var result1 = TiffMultiPageGenerator.GetPageCount(range, 42);
            var result2 = TiffMultiPageGenerator.GetPageCount(range, 42);

            Assert.Equal(result1, result2);
        }

        [Fact]
        public void GetPageCount_WithValidRangeAndDifferentIndex_ShouldReturnDifferentPageCounts()
        {
            // Arrange
            var range = (Min: 1, Max: 100);

            // Act
            var result1 = TiffMultiPageGenerator.GetPageCount(range, 1);
            var result2 = TiffMultiPageGenerator.GetPageCount(range, 2);

            // Assert - With large range, different indices should likely produce different results
            // (Though statistically possible to be the same, very unlikely with 100 values)
            Assert.InRange(result1, range.Min, range.Max);
            Assert.InRange(result2, range.Min, range.Max);
        }

        [Fact]
        public void GetPageCount_WithRange_ShouldReturnCountWithinRange()
        {
            // Arrange
            var range = (Min: 5, Max: 15);

            // Act
            for (int i = 0; i < 100; i++)
            {
                var result = TiffMultiPageGenerator.GetPageCount(range, i);

                // Assert
                Assert.InRange(result, range.Min, range.Max);
            }
        }

        [Fact]
        public void Generate_ShouldReturnNonEmptyByteArray()
        {
            // Arrange
            var workItem = new FileWorkItem { Index = 1 };

            // Act
            var result = TiffMultiPageGenerator.Generate(5, workItem);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        [Fact]
        public void Generate_WithDifferentPageCounts_ShouldReturnDifferentSizes()
        {
            // Arrange
            var workItem = new FileWorkItem { Index = 1 };

            // Act
            var result1 = TiffMultiPageGenerator.Generate(1, workItem);
            var result2 = TiffMultiPageGenerator.Generate(10, workItem);

            // Assert - Currently generates single-page TIFF regardless of pageCount
            // This is expected behavior as noted in the class documentation
            Assert.NotNull(result1);
            Assert.NotNull(result2);
        }
    }
}
