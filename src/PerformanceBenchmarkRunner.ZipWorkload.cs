using System.Globalization;
using System.IO.Compression;
using Zipper.Cli;
using Zipper.Config;

namespace Zipper;

public static partial class PerformanceBenchmarkRunner
{
    internal const string ZipWorkloadUsage =
        "--zip-workload <archive.zip> <seed> <count:bytes:binary|repeat:store|deflate> [...]";

    private static readonly DateTimeOffset WorkloadTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed record WorkloadGroup(long Count, long Bytes, bool Repeated, CompressionLevel Compression);

    internal static Task<int> RunZipWorkloadAsync(string[] args, CancellationToken cancellationToken) =>
        RunZipWorkloadAsync(args, cancellationToken, onStagingProgress: null);

    internal static async Task<int> RunZipWorkloadAsync(
        string[] args, CancellationToken cancellationToken, Action<string>? onStagingProgress)
    {
        try
        {
            if (args.Length < 3 || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int seed))
            {
                throw new ArgumentException($"Expected {ZipWorkloadUsage}. Seed must be a non-negative 32-bit integer.");
            }

            if (string.IsNullOrWhiteSpace(args[0]))
            {
                throw new ArgumentException($"Invalid destination archive path '{ArgumentHelpers.SanitizeForEcho(args[0])}'.");
            }

            var path = Path.GetFullPath(args[0]);
            if (string.IsNullOrEmpty(Path.GetFileName(path)))
            {
                throw new ArgumentException($"Invalid destination archive path '{ArgumentHelpers.SanitizeForEcho(args[0])}'.");
            }

            var groups = args.Skip(2).Select(ParseWorkloadGroup).ToArray();
            long count = 0;
            long bytes = 0;
            foreach (var group in groups)
            {
                count = checked(count + group.Count);
                bytes = checked(bytes + checked(group.Count * group.Bytes));
            }

            var archiveBytes = await WriteZipWorkloadAsync(
                path, seed, groups, cancellationToken, onStagingProgress);

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"ZIP workload seed={seed} recipe={string.Join(',', args.Skip(2))} entries={count} payloadBytes={bytes} archiveBytes={archiveBytes}"));
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await Console.Error.WriteLineAsync("ZIP workload cancelled.".AsMemory(), CancellationToken.None);
            return 130;
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await Console.Error.WriteLineAsync(
                $"ZIP workload error: {ArgumentHelpers.SanitizeForEcho(ex.Message)}".AsMemory(), CancellationToken.None);
            return 1;
        }
    }

    private static WorkloadGroup ParseWorkloadGroup(string recipe)
    {
        var fields = recipe.Split(':');
        if (fields.Length != 4
            || !long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out long count)
            || count == 0
            || !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out long bytes)
            || fields[2] is not ("binary" or "repeat")
            || fields[3] is not ("store" or "deflate"))
        {
            throw new ArgumentException($"Invalid ZIP workload group '{ArgumentHelpers.SanitizeForEcho(recipe)}'. Expected {ZipWorkloadUsage}.");
        }

        var method = fields[3] == "store" ? ZipCompressionMethod.Store : ZipCompressionMethod.Deflate;
        return new WorkloadGroup(count, bytes, fields[2] == "repeat", ZipArchiveSink.ResolveCompressionLevel(method));
    }

    // ZipArchiveSink buffers whole files via ChannelReader<FileData> and writes Load Files,
    // so workload generation streams directly through ZipArchive using a fixed 64 KiB buffer
    // while reusing ZipArchiveSink.ResolveCompressionLevel and exclusive staging/publication.
    private static async Task<long> WriteZipWorkloadAsync(
        string path, int seed, WorkloadGroup[] groups, CancellationToken cancellationToken, Action<string>? onStagingProgress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDestinationDoesNotExist(path);

        var parentDirectory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException($"Invalid destination archive path '{ArgumentHelpers.SanitizeForEcho(path)}'.");
        var staging = Path.Combine(parentDirectory, $".zip-workload-{Guid.NewGuid():N}.tmp");
        var buffer = new byte[PerformanceConstants.DefaultBufferSize];
#pragma warning disable S2245 // Seeded randomness makes synthetic ZIP payloads reproducible.
        var random = new Random(seed);
#pragma warning restore S2245
        var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);
        try
        {
            await using (stream)
            {
                await using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                long index = 0;
                foreach (var group in groups)
                {
                    for (long member = 0; member < group.Count; member++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entryName = string.Create(CultureInfo.InvariantCulture, $"{checked(++index):D8}.bin");
                        var entry = archive.CreateEntry(entryName, group.Compression);
                        entry.LastWriteTime = WorkloadTimestamp;
                        await WriteWorkloadPayloadAsync(
                            entry, group, random, buffer, staging, onStagingProgress, cancellationToken);
                    }
                }
            }

            long archiveBytes = new FileInfo(staging).Length;
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDestinationDoesNotExist(path);
            File.Move(staging, path, overwrite: false);
            return archiveBytes;
        }
        catch
        {
            try
            {
                File.Delete(staging);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Preserve the original generation or publication error.
            }

            throw;
        }
    }

    private static async Task WriteWorkloadPayloadAsync(
        ZipArchiveEntry entry,
        WorkloadGroup group,
        Random random,
        byte[] buffer,
        string staging,
        Action<string>? onStagingProgress,
        CancellationToken cancellationToken)
    {
        await using var payload = await entry.OpenAsync(cancellationToken);
        if (group.Repeated && group.Bytes > 0)
        {
            int fillLength = (int)Math.Min(group.Bytes, buffer.Length);
            buffer.AsSpan(0, fillLength).Fill((byte)random.Next(256));
        }

        for (long remaining = group.Bytes; remaining > 0;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int size = (int)Math.Min(remaining, buffer.Length);
            if (!group.Repeated)
            {
                random.NextBytes(buffer.AsSpan(0, size));
            }

            await payload.WriteAsync(buffer.AsMemory(0, size), cancellationToken);
            remaining -= size;
            onStagingProgress?.Invoke(staging);
        }

        if (group.Bytes == 0)
        {
            onStagingProgress?.Invoke(staging);
        }
    }

    private static void EnsureDestinationDoesNotExist(string path)
    {
        if (File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null)
        {
            throw new IOException($"Destination '{ArgumentHelpers.SanitizeForEcho(path)}' already exists.");
        }
    }
}
