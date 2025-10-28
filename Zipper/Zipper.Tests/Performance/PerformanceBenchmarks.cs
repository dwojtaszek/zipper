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
        [Arguments("proportional")]
        [Arguments("gaussian")]
        [Arguments("exponential")]
        public void DistributionAlgorithm_Performance(string algorithm)
        {
            for (int i = 0; i < FileCount; i++)
            {
                var folderNumber = FileDistributionHelper.GetFolderNumber(i, FolderCount, algorithm);
            }
        }

        [Benchmark]
        public void GetDistribution_Performance()
        {
            FileDistributionHelper.GetDistribution("proportional");
            FileDistributionHelper.GetDistribution("gaussian");
            FileDistributionHelper.GetDistribution("exponential");
        }

        [Benchmark]
        public void FolderNumberCalculation_Performance()
        {
            for (int i = 0; i < FileCount; i++)
            {
                var proportional = FileDistributionHelper.GetFolderNumber(i, FolderCount, "proportional");
                var gaussian = FileDistributionHelper.GetFolderNumber(i, FolderCount, "gaussian");
                var exponential = FileDistributionHelper.GetFolderNumber(i, FolderCount, "exponential");
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
                memory.Dispose();
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
                ProgressTracker.ReportProgress();
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

    [MemoryDiagnoser]
    [SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net80)]
    public class LoadFileBenchmarks
    {
        private const int FileCount = 1000;
        private readonly string _tempPath;

        public LoadFileBenchmarks()
        {
            _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempPath);
        }

        [Benchmark]
        public void LoadFileGeneration_Performance()
        {
            var loadFilePath = Path.Combine(_tempPath, $"test_{Guid.NewGuid()}.dat");
            ProgressTracker.Initialize(FileCount);
            LoadFileGenerator.CreateLoadFile(loadFilePath, "pdf", FileCount, "UTF-8");
            File.Delete(loadFilePath);
        }

        [Benchmark]
        public void LoadFileGeneration_DifferentEncodings()
        {
            var encodings = new[] { "UTF-8", "UTF-16", "ANSI" };
            foreach (var encoding in encodings)
            {
                var loadFilePath = Path.Combine(_tempPath, $"test_{encoding}_{Guid.NewGuid()}.dat");
                ProgressTracker.Initialize(FileCount);
                LoadFileGenerator.CreateLoadFile(loadFilePath, "pdf", FileCount / encodings.Length, encoding);
                File.Delete(loadFilePath);
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempPath))
            {
                Directory.Delete(_tempPath, true);
            }
        }
    }

    public class PerformanceBenchmarkRunner
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Running Zipper Performance Benchmarks...");
            Console.WriteLine("==========================================");

            // Run all benchmark suites
            BenchmarkRunner.Run<FileDistributionBenchmarks>();
            BenchmarkRunner.Run<EmailTemplateBenchmarks>();
            BenchmarkRunner.Run<MemoryPoolBenchmarks>();
            BenchmarkRunner.Run<ValidationBenchmarks>();
            BenchmarkRunner.Run<ProgressTrackingBenchmarks>();
            BenchmarkRunner.Run<LoadFileBenchmarks>();

            Console.WriteLine("==========================================");
            Console.WriteLine("All benchmarks completed!");
        }
    }
}