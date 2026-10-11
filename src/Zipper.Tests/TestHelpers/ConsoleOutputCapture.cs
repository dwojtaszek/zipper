namespace Zipper.Tests;

public static class ConsoleOutputCapture
{
    public static (string stdout, string stderr) RunWithCapturedOutput(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var (_, stdout, stderr) = RunWithCapturedOutput(() =>
        {
            action();
            return true;
        });
        return (stdout, stderr);
    }

    public static (T result, string stdout, string stderr) RunWithCapturedOutput<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var result = action();
            return (result, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    public static async Task<(string stdout, string stderr)> RunWithCapturedOutputAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var (_, stdout, stderr) = await RunWithCapturedOutputAsync(async () =>
        {
            await action().ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
        return (stdout, stderr);
    }

    public static async Task<(T result, string stdout, string stderr)> RunWithCapturedOutputAsync<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var result = await action().ConfigureAwait(false);
            return (result, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
