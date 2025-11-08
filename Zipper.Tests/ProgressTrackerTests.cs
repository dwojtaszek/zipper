using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Zipper.Tests
{
    public class ProgressTrackerTests
    {
        private readonly ITestOutputHelper _output;

        public ProgressTrackerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Initialize_WithValidTotal_SetsCorrectValues()
        {
            // Arrange
            const long totalFiles = 1000;

            // Act
            ProgressTracker.Initialize(totalFiles);

            // Assert
            var completed = ProgressTracker.GetCompletedCount();
            Assert.Equal(0, completed);
        }

        [Fact]
        public void ReportFilesCompleted_WithValidCount_IncrementsCorrectly()
        {
            // Arrange
            const long totalFiles = 1000;
            ProgressTracker.Initialize(totalFiles);

            // Act
            ProgressTracker.ReportFilesCompleted(100);

            // Assert
            var completed = ProgressTracker.GetCompletedCount();
            Assert.Equal(100, completed);
        }

        [Fact]
        public void ReportFilesCompleted_MultipleCalls_AccumulatesCorrectly()
        {
            // Arrange
            const long totalFiles = 1000;
            ProgressTracker.Initialize(totalFiles);

            // Act
            ProgressTracker.ReportFilesCompleted(100);
            ProgressTracker.ReportFilesCompleted(200);
            ProgressTracker.ReportFilesCompleted(300);

            // Assert
            var completed = ProgressTracker.GetCompletedCount();
            Assert.Equal(600, completed);
        }

        [Fact]
        public void ReportFileGenerated_SingleCall_IncrementsByOne()
        {
            // Arrange
            const long totalFiles = 1000;
            ProgressTracker.Initialize(totalFiles);

            // Act
            ProgressTracker.ReportFileGenerated("test.txt");

            // Assert
            var completed = ProgressTracker.GetCompletedCount();
            Assert.Equal(1, completed);
        }

        [Fact]
        public void ReportFileGenerated_MultipleCalls_IncrementsCorrectly()
        {
            // Arrange
            const long totalFiles = 1000;
            ProgressTracker.Initialize(totalFiles);

            // Act
            ProgressTracker.ReportFileGenerated("file1.txt");
            ProgressTracker.ReportFileGenerated("file2.txt");
            ProgressTracker.ReportFileGenerated("file3.txt");

            // Assert
            var completed = ProgressTracker.GetCompletedCount();
            Assert.Equal(3, completed);
        }

        [Fact]
        public void ReportProgress_WithValidInputs_DoesNotThrow()
        {
            // Arrange & Act & Assert
            ProgressTracker.ReportProgress(500, 1000); // Should not throw
        }

        [Fact]
        public void ReportProgress_WithZeroTotal_DoesNotThrow()
        {
            // Arrange & Act & Assert
            ProgressTracker.ReportProgress(100, 0); // Should not throw
        }

        [Fact]
        public void ReportProgress_WithCompletedGreaterThanTotal_DoesNotThrow()
        {
            // Arrange & Act & Assert
            ProgressTracker.ReportProgress(1500, 1000); // Should not throw
        }

        [Fact]
        public void GetCompletedCount_AfterInitialize_ReturnsZero()
        {
            // Arrange & Act
            ProgressTracker.Initialize(1000);

            // Assert
            var completed = ProgressTracker.GetCompletedCount();
            Assert.Equal(0, completed);
        }

        [Fact]
        public void Reset_AfterReporting_ClearsCompletedCount()
        {
            // Arrange
            ProgressTracker.Initialize(1000);
            ProgressTracker.ReportFilesCompleted(500);

            // Act
            ProgressTracker.Reset();

            // Assert
            var completed = ProgressTracker.GetCompletedCount();
            Assert.Equal(0, completed);
        }

        [Fact]
        public void FinalizeProgress_DoesNotThrow()
        {
            // Arrange & Act & Assert
            ProgressTracker.FinalizeProgress(); // Should not throw
        }

        [Fact]
        public void ConcurrentReportFilesCompleted_ThreadSafe()
        {
            // Arrange
            const long totalFiles = 10000;
            const int threadCount = 10;
            const int filesPerThread = 1000;

            ProgressTracker.Initialize(totalFiles);
            var tasks = new Task[threadCount];
            var random = new Random();

            // Act
            for (int i = 0; i < threadCount; i++)
            {
                int threadIndex = i;
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < filesPerThread; j++)
                    {
                        // Randomly choose between batch and single reporting
                        if (j % 10 == 0)
                        {
                            ProgressTracker.ReportFilesCompleted(10);
                        }
                        else
                        {
                            ProgressTracker.ReportFileGenerated($"file_{threadIndex}_{j}.txt");
                        }

                        // Small delay to simulate real work
                        Thread.Sleep(random.Next(1, 5));
                    }
                });
            }

            Task.WaitAll(tasks);

            // Assert
            var completed = ProgressTracker.GetCompletedCount();
            Assert.Equal(totalFiles, completed);
        }

        [Fact]
        public void ProgressTrackingWorkflow_RealisticScenario()
        {
            // Arrange
            const long totalFiles = 5000;
            ProgressTracker.Initialize(totalFiles);

            // Act - Simulate realistic file generation workflow
            var random = new Random();
            long totalReported = 0;

            for (int batch = 0; batch < 50; batch++) // 50 batches
            {
                var batchSize = random.Next(50, 150); // Random batch sizes

                if (batchSize <= (totalFiles - totalReported))
                {
                    ProgressTracker.ReportFilesCompleted(batchSize);
                    totalReported += batchSize;
                }

                // Simulate some individual file reports
                for (int i = 0; i < random.Next(1, 10); i++)
                {
                    if (totalReported < totalFiles)
                    {
                        ProgressTracker.ReportFileGenerated($"individual_file_{totalReported + 1}.txt");
                        totalReported++;
                    }
                }

                Thread.Sleep(random.Next(10, 50)); // Simulate processing time
            }

            // Assert
            var completed = ProgressTracker.GetCompletedCount();
            Assert.Equal(totalFiles, completed);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(10000)]
        public void ReportFilesCompleted_LargeCounts_HandlesCorrectly(long count)
        {
            // Arrange
            ProgressTracker.Initialize(count + 1000);

            // Act
            ProgressTracker.ReportFilesCompleted(count);

            // Assert
            var completed = ProgressTracker.GetCompletedCount();
            Assert.Equal(count, completed);
        }

        [Fact]
        public void MultipleInitializeCalls_ResetsCorrectly()
        {
            // Arrange
            ProgressTracker.Initialize(1000);
            ProgressTracker.ReportFilesCompleted(500);

            // Act - Reinitialize with different total
            ProgressTracker.Initialize(2000);

            // Assert
            var completed = ProgressTracker.GetCompletedCount();
            Assert.Equal(0, completed);
        }
    }
}