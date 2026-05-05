using Xunit;
using Xunit.Abstractions;

namespace Zipper
{
    public class EmailAttachmentPickerTests
    {
        private readonly ITestOutputHelper output;

        public EmailAttachmentPickerTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void Pick_RespectsRate()
        {
            var picker = new EmailAttachmentPicker();
            var pool = MakePool(3);
            const double expectedRate = 0.50;
            const int n = 10_000;
            int picked = 0;
            var rng = new Random(42);

            for (int i = 0; i < n; i++)
            {
                var result = picker.Pick(-999L, expectedRate * 100.0, pool, rng);
                if (result != null)
                {
                    picked++;
                }
            }

            double actual = (double)picked / n;
            double chiSquare = ComputeBinomialChiSquare(picked, n, expectedRate);
            this.output.WriteLine($"Expected rate: {expectedRate:P0}, Actual: {actual:P2}, χ²={chiSquare:F3}");

            // p ≥ 0.01 corresponds to χ² < 6.635 for df=1
            Assert.True(chiSquare < 6.635, $"Rate deviates significantly: χ²={chiSquare:F3} (limit 6.635), actual={actual:P2}");
        }

        [Fact]
        public void Pick_NeverPicksSelf()
        {
            var picker = new EmailAttachmentPicker();
            var pool = new NativeFileReference[]
            {
                new(1L, "file1.pdf", new byte[] { 1, 2, 3 }),
                new(2L, "file2.pdf", new byte[] { 4, 5, 6 }),
                new(3L, "file3.pdf", new byte[] { 7, 8, 9 }),
            };

            // nativeFileIndex = 2 → file2.pdf must never be chosen
            for (int seed = 0; seed < 200; seed++)
            {
                var result = picker.Pick(2L, 100.0, pool, new Random(seed));
                Assert.NotNull(result);
                Assert.NotEqual("file2.pdf", result!.FileName);
            }
        }

        [Fact]
        public void Pick_Deterministic_ForSameSeed()
        {
            var picker = new EmailAttachmentPicker();
            var pool = MakePool(3);

            var r1 = picker.Pick(-999L, 50.0, pool, new Random(77));
            var r2 = picker.Pick(-999L, 50.0, pool, new Random(77));

            Assert.Equal(r1?.FileName, r2?.FileName);
            Assert.Equal(r1?.Content, r2?.Content);
        }

        [Fact]
        public void Pick_ZeroRate_ReturnsNull()
        {
            var picker = new EmailAttachmentPicker();
            var pool = MakePool(3);

            for (int i = 0; i < 50; i++)
            {
                var result = picker.Pick(-999L, 0.0, pool, new Random(i));
                Assert.Null(result);
            }
        }

        [Fact]
        public void Pick_FullRate_NeverReturnsNull_WhenCandidatesExist()
        {
            var picker = new EmailAttachmentPicker();
            var pool = MakePool(3);

            for (int i = 0; i < 50; i++)
            {
                var result = picker.Pick(-999L, 100.0, pool, new Random(i));
                Assert.NotNull(result);
            }
        }

        [Fact]
        public void Pick_EmptyPool_ReturnsNull()
        {
            var picker = new EmailAttachmentPicker();
            var result = picker.Pick(0L, 100.0, Array.Empty<NativeFileReference>(), new Random(1));
            Assert.Null(result);
        }

        [Fact]
        public void Pick_AllCandidatesAreSelf_ReturnsNull()
        {
            var picker = new EmailAttachmentPicker();
            var pool = new NativeFileReference[]
            {
                new(42L, "only.pdf", new byte[] { 1 }),
            };

            // nativeFileIndex matches the only pool item → no candidates
            var result = picker.Pick(42L, 100.0, pool, new Random(1));
            Assert.Null(result);
        }

        [Fact]
        public void Pick_AttachmentInfo_MapsFileNameAndContent()
        {
            var picker = new EmailAttachmentPicker();
            var expectedContent = new byte[] { 0x25, 0x50, 0x44, 0x46 };
            var pool = new NativeFileReference[]
            {
                new(-1L, "report.pdf", expectedContent, "application/pdf"),
            };

            var result = picker.Pick(0L, 100.0, pool, new Random(1));

            Assert.NotNull(result);
            Assert.Equal("report.pdf", result!.FileName);
            Assert.Equal(expectedContent, result.Content);
            Assert.Equal("application/pdf", result.ContentType);
        }

        [Fact]
        public void Pick_NullPool_ThrowsArgumentNullException()
        {
            var picker = new EmailAttachmentPicker();
            Assert.Throws<ArgumentNullException>(() => picker.Pick(0L, 50.0, null!, new Random(1)));
        }

        [Fact]
        public void Pick_NullRandom_ThrowsArgumentNullException()
        {
            var picker = new EmailAttachmentPicker();
            Assert.Throws<ArgumentNullException>(() => picker.Pick(0L, 50.0, MakePool(1), null!));
        }

        [Fact]
        public void PlaceholderPool_HasThreeItems_WithNegativeIndices()
        {
            Assert.Equal(3, EmailAttachmentPicker.PlaceholderPool.Count);
            Assert.All(EmailAttachmentPicker.PlaceholderPool, r =>
            {
                Assert.True(r.Index < 0, $"Placeholder pool item {r.FileName} has non-negative index {r.Index}");
                Assert.False(string.IsNullOrEmpty(r.FileName));
                Assert.NotEmpty(r.Content);
            });
        }

        private static IReadOnlyList<NativeFileReference> MakePool(int count, long startIndex = -100)
        {
            var pool = new NativeFileReference[count];
            for (int i = 0; i < count; i++)
            {
                pool[i] = new NativeFileReference(startIndex + i, $"file{i}.pdf", new byte[] { (byte)i, 1, 2 });
            }

            return pool;
        }

        private static double ComputeBinomialChiSquare(int observed, int n, double expectedRate)
        {
            double expectedYes = n * expectedRate;
            double expectedNo = n * (1.0 - expectedRate);
            double observedNo = n - observed;

            if (expectedYes < 1.0 || expectedNo < 1.0)
            {
                return 0.0;
            }

            return ((observed - expectedYes) * (observed - expectedYes) / expectedYes)
                 + ((observedNo - expectedNo) * (observedNo - expectedNo) / expectedNo);
        }
    }
}
