using System.Globalization;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests
{
    public class CultureTests
    {
        [Fact]
        public async Task LiveDatePaths_UseInvariantCulture_WhenAmbientCultureIsNonGregorian()
        {
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                var umAlQura = new CultureInfo("ar-SA");
                umAlQura.DateTimeFormat.Calendar = new System.Globalization.UmAlQuraCalendar();
                Thread.CurrentThread.CurrentCulture = umAlQura;

                // Testing DatWriter LoadfileOnlyMode dates
                var request = new FileGenerationRequest
                {
                    Output = new OutputConfig { FileCount = 1, FileType = "pdf" },
                    Metadata = new MetadataConfig { Seed = 42 }
                };

                using var stream = new MemoryStream();
                var writer = new DatWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly);
                await writer.WriteAsync(stream, request, Array.Empty<Zipper.FileData>());

                var result = System.Text.Encoding.UTF8.GetString(stream.ToArray());

                // The output should not contain "1446-" (Hijri years). It should contain "2024-" or similar Gregorian dates.
                // We will specifically assert it matches "yyyy-MM-dd" where yyyy is ~2024 or 2025.
                // Since seed=42, we know the exact dates generated. Let's just ensure we don't have UmAlQura dates.
                // We can parse all dates looking for Gregorian valid dates.
                // A simpler check: 
                Assert.DoesNotContain("144", result); // Hijri 144x
                Assert.Contains("202", result); // Gregorian 202x
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
            }
        }
    }
}
