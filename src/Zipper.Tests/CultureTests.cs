using System.Globalization;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests;

/// <summary>
/// Tests ensuring that the application behavior is consistent regardless of the ambient system culture.
/// </summary>
public class CultureTests
{
    /// <summary>
    /// Verifies that output paths formatting dates use InvariantCulture.
    /// This prevents dates from being formatted into localized or non-Gregorian calendars
    /// when the thread's ambient culture dictates otherwise.
    /// </summary>
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
            var writer = new DatComposingWriter(Zipper.LoadFiles.WriterMode.LoadfileOnly);
            await writer.WriteAsync(stream, request, Array.Empty<Zipper.FileData>());

            var result = System.Text.Encoding.UTF8.GetString(stream.ToArray());

            // The output should not contain "1446-" (Hijri years). It should contain "2024-" or similar Gregorian dates.
            // Match yyyy-MM-dd patterns where yyyy is Gregorian 202x or Hijri 144x
            Assert.DoesNotMatch(@"144\d-\d{2}-\d{2}", result); // No Hijri date patterns
            Assert.Matches(@"202[45]-\d{2}-\d{2}", result); // Gregorian 2024-2025 date patterns
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }
}
