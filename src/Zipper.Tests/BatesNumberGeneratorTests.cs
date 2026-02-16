using Xunit;

namespace Zipper
{
    public class BatesNumberGeneratorTests
    {
        [Fact]
        public void Generate_WithDefaultConfig_ShouldGenerateCorrectNumber()
        {
            // Arrange
            var config = new BatesNumberConfig
            {
                Prefix = "DOC",
                Start = 1,
                Digits = 8,
                Increment = 1,
            };

            // Act
            var result = BatesNumberGenerator.Generate(config, 0);

            // Assert
            Assert.Equal("DOC00000001", result);
        }

        [Fact]
        public void Generate_WithCustomPrefix_ShouldIncludePrefix()
        {
            // Arrange
            var config = new BatesNumberConfig
            {
                Prefix = "CLIENT001",
                Start = 1,
                Digits = 8,
                Increment = 1,
            };

            // Act
            var result = BatesNumberGenerator.Generate(config, 0);

            // Assert
            Assert.Equal("CLIENT00100000001", result);
        }

        [Fact]
        public void Generate_WithMultipleIndices_ShouldIncrementCorrectly()
        {
            // Arrange
            var config = new BatesNumberConfig
            {
                Prefix = "DOC",
                Start = 1,
                Digits = 8,
                Increment = 1,
            };

            // Act
            var result1 = BatesNumberGenerator.Generate(config, 0);
            var result2 = BatesNumberGenerator.Generate(config, 1);
            var result3 = BatesNumberGenerator.Generate(config, 2);

            // Assert
            Assert.Equal("DOC00000001", result1);
            Assert.Equal("DOC00000002", result2);
            Assert.Equal("DOC00000003", result3);
        }

        [Fact]
        public void Generate_WithCustomStart_ShouldStartFromCorrectNumber()
        {
            // Arrange
            var config = new BatesNumberConfig
            {
                Prefix = "DOC",
                Start = 100,
                Digits = 8,
                Increment = 1,
            };

            // Act
            var result = BatesNumberGenerator.Generate(config, 0);

            // Assert
            Assert.Equal("DOC00000100", result);
        }

        [Fact]
        public void Generate_WithCustomIncrement_ShouldIncrementByStep()
        {
            // Arrange
            var config = new BatesNumberConfig
            {
                Prefix = "DOC",
                Start = 1,
                Digits = 8,
                Increment = 5,
            };

            // Act
            var result1 = BatesNumberGenerator.Generate(config, 0);
            var result2 = BatesNumberGenerator.Generate(config, 1);
            var result3 = BatesNumberGenerator.Generate(config, 2);

            // Assert
            Assert.Equal("DOC00000001", result1);
            Assert.Equal("DOC00000006", result2);
            Assert.Equal("DOC00000011", result3);
        }

        [Fact]
        public void Generate_WithDifferentDigitCounts_ShouldPadCorrectly()
        {
            // Arrange
            var config4Digits = new BatesNumberConfig { Prefix = "A", Start = 1, Digits = 4 };
            var config6Digits = new BatesNumberConfig { Prefix = "B", Start = 1, Digits = 6 };
            var config10Digits = new BatesNumberConfig { Prefix = "C", Start = 1, Digits = 10 };

            // Act
            var result4 = BatesNumberGenerator.Generate(config4Digits, 0);
            var result6 = BatesNumberGenerator.Generate(config6Digits, 0);
            var result10 = BatesNumberGenerator.Generate(config10Digits, 0);

            // Assert
            Assert.Equal("A0001", result4);
            Assert.Equal("B000001", result6);
            Assert.Equal("C0000000001", result10);
        }

        [Fact]
        public void Generate_WithLargeNumber_ShouldHandleCorrectly()
        {
            // Arrange
            var config = new BatesNumberConfig
            {
                Prefix = "DOC",
                Start = 999999,
                Digits = 8,
                Increment = 1,
            };

            // Act
            var result = BatesNumberGenerator.Generate(config, 0);

            // Assert
            Assert.Equal("DOC00999999", result);
        }

        [Fact]
        public void Generate_WithZeroStart_ShouldGenerateCorrectly()
        {
            // Arrange
            var config = new BatesNumberConfig
            {
                Prefix = "DOC",
                Start = 0,
                Digits = 8,
                Increment = 1,
            };

            // Act
            var result = BatesNumberGenerator.Generate(config, 0);

            // Assert
            Assert.Equal("DOC00000000", result);
        }

        [Fact]
        public void GenerateWithoutPrefix_WithDefaultConfig_ShouldGenerateNumberOnly()
        {
            // Arrange
            var config = new BatesNumberConfig
            {
                Prefix = "DOC",
                Start = 1,
                Digits = 8,
                Increment = 1,
            };

            // Act
            var result = BatesNumberGenerator.GenerateWithoutPrefix(config, 0);

            // Assert
            Assert.Equal("00000001", result);
        }

        [Fact]
        public void GenerateWithoutPrefix_WithMultipleIndices_ShouldIncrementCorrectly()
        {
            // Arrange
            var config = new BatesNumberConfig
            {
                Prefix = "PREFIX",  // Prefix should be ignored
                Start = 100,
                Digits = 6,
                Increment = 10,
            };

            // Act
            var result1 = BatesNumberGenerator.GenerateWithoutPrefix(config, 0);
            var result2 = BatesNumberGenerator.GenerateWithoutPrefix(config, 1);
            var result3 = BatesNumberGenerator.GenerateWithoutPrefix(config, 2);

            // Assert
            Assert.Equal("000100", result1);
            Assert.Equal("000110", result2);
            Assert.Equal("000120", result3);
        }
    }
}
