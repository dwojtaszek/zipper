// <copyright file="ProgressTrackerTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using Xunit;

namespace Zipper
{
    public class ProgressTrackerTests
    {
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
        public async Task ConcurrentReportFilesCompleted_ThreadSafe()
        {
            // Arrange
            const long totalFiles = 10000;
            const int threadCount = 10;
            const int filesPerThread = 1000;

            ProgressTracker.Initialize(totalFiles);
            var tasks = new Task[threadCount];

            // Act
            for (int i = 0; i < threadCount; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < filesPerThread; j++)
                    {
                        ProgressTracker.ReportFileGenerated("file.txt");
                    }
                });
            }

            await Task.WhenAll(tasks);

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

        [Fact]
        public void ReportProgress_DoesNotThrow()
        {
            // Arrange & Act & Assert
            ProgressTracker.ReportProgress(500, 1000); // Should not throw
        }
    }
}
