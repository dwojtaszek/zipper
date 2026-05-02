using Xunit;
using Zipper.Cli;

namespace Zipper.Tests
{
    [Collection("ConsoleTests")]
    public class CliValidatorTests
    {
        private static ParsedArguments CreateValidArgs()
        {
            return new ParsedArguments
            {
                FileType = "pdf",
                Count = 10,
                OutputDirectory = new DirectoryInfo(Path.GetTempPath()),
            };
        }

        [Fact]
        public void Validate_ValidArgs_ReturnsTrue()
        {
            Assert.True(CliValidator.Validate(CreateValidArgs()));
        }

        [Fact]
        public void Validate_NullArg_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CliValidator.Validate(null!));
        }

        [Fact]
        public void Validate_MissingType_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.FileType = null;
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_LoadfileOnly_WithoutType_ReturnsTrue()
        {
            var args = CreateValidArgs();
            args.FileType = null;
            args.LoadfileOnly = true;
            Assert.True(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_ProductionSet_WithoutType_ReturnsTrue()
        {
            var args = CreateValidArgs();
            args.FileType = null;
            args.ProductionSet = true;
            args.BatesPrefix = "PREFIX";
            Assert.True(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_MissingCount_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.Count = null;
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_CountZero_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.Count = 0;
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_CountNegative_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.Count = -1;
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_CountExceedsMax_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.Count = int.MaxValue;
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_NullOutputDirectory_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.OutputDirectory = null;
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_FoldersOutOfRange_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.Folders = 0;
            Assert.False(CliValidator.Validate(args));

            args.Folders = 101;
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_AttachmentRateOutOfRange_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.AttachmentRate = -1;
            Assert.False(CliValidator.Validate(args));

            args.AttachmentRate = 101;
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_InvalidEncoding_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.Encoding = "INVALID_ENCODING";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_InvalidDistribution_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.Distribution = "invalid_dist";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_InvalidTargetZipSize_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.TargetZipSize = "invalid";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_TargetZipSizeWithoutCount_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.Count = null;
            args.TargetZipSize = "10MB";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_InvalidLoadFileFormat_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.LoadFileFormat = "invalid";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_LoadfileOnlyArgs_WithoutLoadfileOnly_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.Eol = "LF";
            Assert.False(CliValidator.Validate(args));

            args.Eol = null;
            args.ColDelim = "ascii:20";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_ChaosMode_WithoutLoadfileOnly_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.ChaosMode = true;
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_ChaosAmount_WithoutChaosMode_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.LoadfileOnly = true;
            args.ChaosAmount = "5%";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_ChaosTypes_WithoutChaosMode_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.LoadfileOnly = true;
            args.ChaosTypes = "quotes";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_ChaosScenario_WithoutChaosMode_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.LoadfileOnly = true;
            args.ChaosScenario = "basic";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_ChaosScenarioWithTypes_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.LoadfileOnly = true;
            args.ChaosMode = true;
            args.ChaosScenario = "basic";
            args.ChaosTypes = "quotes";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_LoadfileOnlyWithTargetZipSize_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.LoadfileOnly = true;
            args.TargetZipSize = "100MB";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_LoadfileOnlyWithIncludeLoadFile_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.LoadfileOnly = true;
            args.IncludeLoadFile = true;
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_ProductionSet_ConflictsWithLoadfileOnly_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.ProductionSet = true;
            args.LoadfileOnly = true;
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_ProductionSet_WithoutBatesPrefix_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.ProductionSet = true;
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_ProductionZip_WithoutProductionSet_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.ProductionZip = true;
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_VolumeSize_WithoutProductionSet_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.VolumeSize = 100;
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_BatesPrefix_WithPathSeparator_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.BatesPrefix = "foo/bar";
            Assert.False(CliValidator.Validate(args));

            args.BatesPrefix = "foo\\bar";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_BatesPrefix_WithDotDot_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.BatesPrefix = "..";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_BatesPrefix_WithSpecialChars_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.BatesPrefix = "hello!@#";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_InvalidEol_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.LoadfileOnly = true;
            args.Eol = "INVALID";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_ValidEol_ReturnsTrue()
        {
            var args = CreateValidArgs();
            args.LoadfileOnly = true;
            foreach (var eol in new[] { "CRLF", "LF", "CR" })
            {
                args.Eol = eol;
                Assert.True(CliValidator.Validate(args));
            }
        }

        [Fact]
        public void Validate_InvalidStrictDelimiter_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.LoadfileOnly = true;
            args.ColDelim = "20"; // missing ascii: or char: prefix
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void Validate_InvalidChaosAmount_ReturnsFalse()
        {
            var args = CreateValidArgs();
            args.LoadfileOnly = true;
            args.ChaosMode = true;
            args.ChaosAmount = "abc";
            Assert.False(CliValidator.Validate(args));

            args.ChaosAmount = "10.5x%";
            Assert.False(CliValidator.Validate(args));
        }

        [Fact]
        public void IsValidStrictDelimiter_ValidAscii_ReturnsTrue()
        {
            Assert.True(CliValidator.IsValidStrictDelimiter("ascii:20"));
            Assert.True(CliValidator.IsValidStrictDelimiter("ascii:0"));
            Assert.True(CliValidator.IsValidStrictDelimiter("ascii:255"));
        }

        [Fact]
        public void IsValidStrictDelimiter_InvalidAscii_ReturnsFalse()
        {
            Assert.False(CliValidator.IsValidStrictDelimiter("ascii:256"));
            Assert.False(CliValidator.IsValidStrictDelimiter("ascii:-1"));
            Assert.False(CliValidator.IsValidStrictDelimiter("ascii:abc"));
        }

        [Fact]
        public void IsValidStrictDelimiter_ValidChar_ReturnsTrue()
        {
            Assert.True(CliValidator.IsValidStrictDelimiter("char:;"));
            Assert.True(CliValidator.IsValidStrictDelimiter("char:|"));
        }

        [Fact]
        public void IsValidStrictDelimiter_InvalidFormat_ReturnsFalse()
        {
            Assert.False(CliValidator.IsValidStrictDelimiter("20"));
            Assert.False(CliValidator.IsValidStrictDelimiter(string.Empty));
            Assert.False(CliValidator.IsValidStrictDelimiter("ascii:"));
        }

        [Fact]
        public void IsValidChaosAmount_ValidPercentage_ReturnsTrue()
        {
            Assert.True(CliValidator.IsValidChaosAmount("1%"));
            Assert.True(CliValidator.IsValidChaosAmount("100%"));
            Assert.True(CliValidator.IsValidChaosAmount("0.5%"));
        }

        [Fact]
        public void IsValidChaosAmount_ValidExact_ReturnsTrue()
        {
            Assert.True(CliValidator.IsValidChaosAmount("500"));
            Assert.True(CliValidator.IsValidChaosAmount("1"));
        }

        [Fact]
        public void IsValidChaosAmount_Invalid_ReturnsFalse()
        {
            Assert.False(CliValidator.IsValidChaosAmount("abc"));
            Assert.False(CliValidator.IsValidChaosAmount("0%"));
            Assert.False(CliValidator.IsValidChaosAmount("-5"));
        }
    }
}
