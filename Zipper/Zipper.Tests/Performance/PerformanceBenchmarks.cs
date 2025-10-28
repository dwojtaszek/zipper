using System;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Zipper;

namespace Zipper.Tests.Performance
{
    [MemoryDiagnoser]
    [SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net80)]
    public class FileDistributionBenchmarks
    {
        private const int FileCount = 10000;
        private const int FolderCount = 100;

        [Benchmark]
        [Arguments(DistributionType.Proportional)]
        [Arguments(DistributionType.Gaussian)]
        [Arguments(DistributionType.Exponential)]
        public void DistributionAlgorithm_Performance(DistributionType distributionType)
        {
            for (int i = 0; i < FileCount; i++)
            {
                var folderNumber = FileDistributionHelper.GetFolderNumber(i, FileCount, FolderCount, distributionType);
            }
        }

        [Benchmark]
        public void FolderNumberCalculation_Performance()
        {
            for (int i = 0; i < FileCount; i++)
            {
                var proportional = FileDistributionHelper.GetFolderNumber(i, FileCount, FolderCount, DistributionType.Proportional);
                var gaussian = FileDistributionHelper.GetFolderNumber(i, FileCount, FolderCount, DistributionType.Gaussian);
                var exponential = FileDistributionHelper.GetFolderNumber(i, FileCount, FolderCount, DistributionType.Exponential);
            }
        }
    }

    [MemoryDiagnoser]
    [SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net80)]
    public class EmailTemplateBenchmarks
    {
        private const int Iterations = 1000;

        [Benchmark]
        public void EmailGeneration_Performance()
        {
            for (int i = 0; i < Iterations; i++)
            {
                var template = EmailTemplateSystem.GetRandomTemplate(i, i, EmailTemplateSystem.EmailCategory.Business);
            }
        }

        [Benchmark]
        public void EmailWithAttachments_Performance()
        {
            for (int i = 0; i < Iterations; i++)
            {
                var config = new EmlGenerationConfig
                {
                    FileIndex = i,
                    AttachmentRate = 50,
                    Category = EmailTemplateSystem.EmailCategory.Business
                };
                var result = EmlGenerationService.GenerateEmlContent(config);
            }
        }

        [Benchmark]
        public void AllEmailCategories_Performance()
        {
            var categories = Enum.GetValues<EmailTemplateSystem.EmailCategory>();
            foreach (var category in categories)
            {
                for (int i = 0; i < Iterations / categories.Length; i++)
                {
                    var template = EmailTemplateSystem.GetRandomTemplate(i, i, category);
                }
            }
        }
    }

    [MemoryDiagnoser]
    [SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net80)]
    public class MemoryPoolBenchmarks
    {
        private const int Operations = 1000;
        private const int BufferSize = 81920; // 80KB typical file size

        [Benchmark]
        public void MemoryPoolRent_Performance()
        {
            using var manager = new MemoryPoolManager();
            for (int i = 0; i < Operations; i++)
            {
                var memory = manager.Rent(BufferSize);
                memory?.Dispose();
            }
        }

        [Benchmark]
        public void ArrayPooling_Performance()
        {
            for (int i = 0; i < Operations; i++)
            {
                var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(BufferSize);
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        [Benchmark]
        public void DirectAllocation_Performance()
        {
            for (int i = 0; i < Operations; i++)
            {
                var buffer = new byte[BufferSize];
            }
        }
    }

    [MemoryDiagnoser]
    [SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net80)]
    public class ValidationBenchmarks
    {
        private readonly string[][] _testArgs = new[]
        {
            new[] { "--type", "pdf", "--count", "1000", "--output-path", "/tmp/test" },
            new[] { "--type", "eml", "--count", "500", "--output-path", "/tmp/test", "--folders", "10", "--distribution", "gaussian" },
            new[] { "--type", "jpg", "--count", "2000", "--output-path", "/tmp/test", "--with-metadata", "--with-text" }
        };

        [Benchmark]
        public void CommandLineValidation_Performance()
        {
            foreach (var args in _testArgs)
            {
                var result = CommandLineValidator.ValidateAndParseArguments(args);
            }
        }

        [Benchmark]
        public void PathValidation_Performance()
        {
            var testPaths = new[]
            {
                Path.GetTempPath(),
                "/tmp/test/validation",
                "/var/tmp/validation/test"
            };

            foreach (var path in testPaths)
            {
                PathValidator.ValidateAndCreateDirectory(path);
            }
        }
    }

    [MemoryDiagnoser]
    [SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net80)]
    public class ProgressTrackingBenchmarks
    {
        private const int Operations = 10000;

        [Benchmark]
        public void ProgressTracking_Performance()
        {
            ProgressTracker.Initialize(Operations);
            for (int i = 0; i < Operations; i++)
            {
                ProgressTracker.ReportProgress(i + 1, Operations);
            }
        }

        [Benchmark]
        public void PerformanceMonitoring_Performance()
        {
            var monitor = new PerformanceMonitor();
            monitor.Start(Operations);
            for (int i = 0; i < Operations; i += 100)
            {
                monitor.ReportFilesCompleted(100);
            }
            var metrics = monitor.Stop();
        }
    }

    // Note: LoadFileGenerator benchmarks removed as the class is internal
    // Load file performance is tested through integration tests instead

    // Note: PerformanceBenchmarkRunner removed to avoid Main method conflicts
    // Run benchmarks using: dotnet run --project Zipper.Tests.csproj --configuration Release -- --filter "PerformanceBenchmarks"
}