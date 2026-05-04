using Xunit;

using Zipper.Config;

namespace Zipper
{
    [Collection("ConsoleTests")]
    public class GenerationRunnerTests
    {
        [Fact]
        public async Task RunAsync_SuccessfulMode_ReturnsZero()
        {
            var (exit, _, _) = await RunWithCapture(new SuccessfulMode(), DefaultRequest());
            Assert.Equal(0, exit);
        }

        [Fact]
        public async Task RunAsync_SuccessfulMode_DelegatesToMode()
        {
            var mode = new SuccessfulMode();
            await RunWithCapture(mode, DefaultRequest());
            Assert.Equal(1, mode.RunCount);
        }

        [Fact]
        public async Task RunAsync_ThrowingMode_ReturnsOne()
        {
            var (exit, _, _) = await RunWithCapture(new ThrowingMode("boom"), DefaultRequest());
            Assert.Equal(1, exit);
        }

        [Fact]
        public async Task RunAsync_ThrowingMode_PrintsLegacyErrorFormatToStderr()
        {
            var (_, _, err) = await RunWithCapture(new ThrowingMode("disk full"), DefaultRequest());
            Assert.Contains("\nAn error occurred: disk full", err);
        }

        [Fact]
        public async Task RunAsync_ThrowingMode_DoesNotWriteErrorToStdout()
        {
            var (_, output, _) = await RunWithCapture(new ThrowingMode("boom"), DefaultRequest());
            Assert.DoesNotContain("An error occurred", output);
        }

        [Fact]
        public async Task RunAsync_PassesRequestToMode()
        {
            var mode = new RequestCapturingMode();
            var request = DefaultRequest();
            await RunWithCapture(mode, request);
            Assert.Same(request, mode.LastRequest);
        }

        [Fact]
        public void Program_SelectMode_LoadfileOnly_ReturnsLoadfileOnlyMode()
        {
            var request = DefaultRequest();
            request.LoadfileOnly = true;
            Assert.IsType<LoadfileOnlyMode>(Program.SelectMode(request));
        }

        [Fact]
        public void Program_SelectMode_ProductionSet_ReturnsProductionSetMode()
        {
            var request = DefaultRequest();
            request.Production = request.Production with { ProductionSet = true };
            Assert.IsType<ProductionSetMode>(Program.SelectMode(request));
        }

        [Fact]
        public void Program_SelectMode_Default_ReturnsStandardMode()
        {
            var request = DefaultRequest();
            Assert.IsType<StandardMode>(Program.SelectMode(request));
        }

        [Fact]
        public void Program_SelectMode_LoadfileOnlyTakesPrecedenceOverProductionSet()
        {
            var request = DefaultRequest();
            request.LoadfileOnly = true;
            request.Production = request.Production with { ProductionSet = true };
            Assert.IsType<LoadfileOnlyMode>(Program.SelectMode(request));
        }

        private static async Task<(int exitCode, string stdout, string stderr)> RunWithCapture(IGenerationMode mode, FileGenerationRequest request)
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            try
            {
                using var outWriter = new StringWriter();
                using var errWriter = new StringWriter();
                Console.SetOut(outWriter);
                Console.SetError(errWriter);
                int exit = await GenerationRunner.RunAsync(mode, request);
                return (exit, outWriter.ToString(), errWriter.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        private static FileGenerationRequest DefaultRequest()
        {
            return new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = "/tmp/test",
                    FileCount = 1,
                    FileType = "pdf",
                    Folders = 1,
                },
            };
        }

        private sealed class SuccessfulMode : IGenerationMode
        {
            public int RunCount { get; private set; }

            public Task RunAsync(FileGenerationRequest request)
            {
                this.RunCount++;
                return Task.CompletedTask;
            }
        }

        private sealed class ThrowingMode : IGenerationMode
        {
            private readonly string message;

            public ThrowingMode(string message)
            {
                this.message = message;
            }

            public Task RunAsync(FileGenerationRequest request) =>
                throw new InvalidOperationException(this.message);
        }

        private sealed class RequestCapturingMode : IGenerationMode
        {
            public FileGenerationRequest? LastRequest { get; private set; }

            public Task RunAsync(FileGenerationRequest request)
            {
                this.LastRequest = request;
                return Task.CompletedTask;
            }
        }
    }
}
