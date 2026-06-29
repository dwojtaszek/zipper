using Xunit;
using Zipper.Config;

namespace Zipper
{
    public class BatesSequenceTests
    {
        [Fact]
        public void BatesNumberConfig_ShouldBeInConfigNamespace()
        {
            var type = typeof(BatesSequence).Assembly.GetType("Zipper.Config.BatesNumberConfig");
            Assert.NotNull(type);
            Assert.Equal("Zipper.Config", type.Namespace);
        }

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
            var result = BatesSequence.FromConfig(config).Format(0).ToString();

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
            var result = BatesSequence.FromConfig(config).Format(0).ToString();

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
            var result1 = BatesSequence.FromConfig(config).Format(0).ToString();
            var result2 = BatesSequence.FromConfig(config).Format(1).ToString();
            var result3 = BatesSequence.FromConfig(config).Format(2).ToString();

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
            var result = BatesSequence.FromConfig(config).Format(0).ToString();

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
            var result1 = BatesSequence.FromConfig(config).Format(0).ToString();
            var result2 = BatesSequence.FromConfig(config).Format(1).ToString();
            var result3 = BatesSequence.FromConfig(config).Format(2).ToString();

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
            var result4 = BatesSequence.FromConfig(config4Digits).Format(0).ToString();
            var result6 = BatesSequence.FromConfig(config6Digits).Format(0).ToString();
            var result10 = BatesSequence.FromConfig(config10Digits).Format(0).ToString();

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
            var result = BatesSequence.FromConfig(config).Format(0).ToString();

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
            var result = BatesSequence.FromConfig(config).Format(0).ToString();

            // Assert
            Assert.Equal("DOC00000000", result);
        }

        [Fact]
        public void Format_WithDefaultConfig_ShouldGenerateNumberOnly()
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
            var result = BatesSequence.FromConfig(config).Format(0).WithoutPrefix();

            // Assert
            Assert.Equal("00000001", result);
        }

        [Fact]
        public void Format_WithMultipleIndices_ShouldIncrementCorrectly()
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
            var result1 = BatesSequence.FromConfig(config).Format(0).WithoutPrefix();
            var result2 = BatesSequence.FromConfig(config).Format(1).WithoutPrefix();
            var result3 = BatesSequence.FromConfig(config).Format(2).WithoutPrefix();

            // Assert
            Assert.Equal("000100", result1);
            Assert.Equal("000110", result2);
            Assert.Equal("000120", result3);
        }

        [Fact]
        public void Format_WithNegativeIndex_ShouldThrowArgumentOutOfRangeException()
        {
            var seq = BatesSequence.FromConfig(new BatesNumberConfig());
            Assert.Throws<ArgumentOutOfRangeException>(() => seq.Format(-1));
        }

        [Theory]
        [InlineData("NullConfig", "Config cannot be null.")]
        [InlineData("NegativeStart", "Bates start number must be non-negative.")]
        [InlineData("ZeroDigits", "Bates digits must be between 1 and 20.")]
        [InlineData("TooManyDigits", "Bates digits must be between 1 and 20.")]
        [InlineData("ZeroIncrement", "Bates increment must be at least 1.")]
        [InlineData("PathSeparatorSlash", "--bates-prefix must not contain path separators.")]
        [InlineData("PathSeparatorBackslash", "--bates-prefix must not contain path separators.")]
        [InlineData("DirTraversalDots", "--bates-prefix must not contain directory traversal sequences.")]
        [InlineData("DirTraversalSlash", "--bates-prefix must not contain directory traversal sequences.")]
        [InlineData("DirTraversalBackslash", "--bates-prefix must not contain directory traversal sequences.")]
        [InlineData("InvalidChar", "--bates-prefix must only contain letters, digits, underscores, and hyphens.")]
        public void FromConfig_WithInvalidConfig_ShouldThrowArgumentException(string scenario, string expectedMessage)
        {
            BatesNumberConfig badConfig = scenario switch
            {
                "NullConfig" => null!,
                "NegativeStart" => new BatesNumberConfig { Start = -1 },
                "ZeroDigits" => new BatesNumberConfig { Digits = 0 },
                "TooManyDigits" => new BatesNumberConfig { Digits = 21 },
                "ZeroIncrement" => new BatesNumberConfig { Increment = 0 },
                "PathSeparatorSlash" => new BatesNumberConfig { Prefix = "a/b" },
                "PathSeparatorBackslash" => new BatesNumberConfig { Prefix = "a\\b" },
                "DirTraversalDots" => new BatesNumberConfig { Prefix = ".." },
                "DirTraversalSlash" => new BatesNumberConfig { Prefix = "../a" },
                "DirTraversalBackslash" => new BatesNumberConfig { Prefix = "..\\a" },
                "InvalidChar" => new BatesNumberConfig { Prefix = "a b" },
                _ => throw new System.ArgumentException("Unknown scenario")
            };

            var ex = Assert.Throws<System.ArgumentException>(() => BatesSequence.FromConfig(badConfig));
            Assert.Contains(expectedMessage, ex.Message, StringComparison.Ordinal);
        }
    }
}
