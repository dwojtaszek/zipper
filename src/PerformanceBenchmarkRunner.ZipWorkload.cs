using System.Globalization;
using System.IO.Compression;
using Zipper.Config;

namespace Zipper;

public static partial class PerformanceBenchmarkRunner
{
    internal const string ZipWorkloadUsage =
        "--zip-workload <archive.zip> <seed> <count:bytes:binary|repeat:store|deflate> [...]";

    private static readonly DateTimeOffset WorkloadTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed record WorkloadGroup(long Count, long Bytes, bool Repeated, CompressionLevel Compression);

    internal static async Task<int> RunZipWorkloadAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            if (args.Length < 3 || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int seed))
            {
                throw new ArgumentException($"Expected {ZipWorkloadUsage}. Seed must be a non-negative 32-bit integer.");
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
                Path.GetFullPath(args[0]), seed, groups, cancellationToken).ConfigureAwait(false);

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"ZIP workload seed={seed} recipe={string.Join(',', args.Skip(2))} entries={count} payloadBytes={bytes} archiveBytes={archiveBytes}"));
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine("ZIP workload cancelled.");
            return 130;
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.Error.WriteLine($"ZIP workload error: {ex.Message}");
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
            throw new ArgumentException($"Invalid ZIP workload group '{recipe}'. Expected {ZipWorkloadUsage}.");
        }

        var method = fields[3] == "store" ? ZipCompressionMethod.Store : ZipCompressionMethod.Deflate;
        return new WorkloadGroup(count, bytes, fields[2] == "repeat", ZipArchiveSink.ResolveCompressionLevel(method));
    }

    private static async Task<long> WriteZipWorkloadAsync(
        string path, int seed, WorkloadGroup[] groups, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException($"Destination '{path}' already exists.");
        }

        var staging = Path.Combine(Path.GetDirectoryName(path)!, $".zip-workload-{Guid.NewGuid():N}.tmp");
        var buffer = new byte[64 * 1024];
        var random = new Random(seed);
        var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);
        try
        {
            await using (stream.ConfigureAwait(false))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                long index = 0;
                foreach (var group in groups)
                {
                    for (long member = 0; member < group.Count; member++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entry = archive.CreateEntry(checked(++index).ToString("D8", CultureInfo.InvariantCulture) + ".bin", group.Compression);
                        entry.LastWriteTime = WorkloadTimestamp;
                        await WriteWorkloadPayloadAsync(entry, group, random, buffer, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            long archiveBytes = new FileInfo(staging).Length;
            cancellationToken.ThrowIfCancellationRequested();
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
        ZipArchiveEntry entry, WorkloadGroup group, Random random, byte[] buffer, CancellationToken cancellationToken)
    {
        await using var payload = entry.Open();
        if (group.Repeated && group.Bytes > 0)
        {
            buffer.AsSpan().Fill((byte)random.Next(256));
        }

        for (long remaining = group.Bytes; remaining > 0;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int size = (int)Math.Min(remaining, buffer.Length);
            if (!group.Repeated)
            {
                random.NextBytes(buffer.AsSpan(0, size));
            }

            await payload.WriteAsync(buffer.AsMemory(0, size), cancellationToken).ConfigureAwait(false);
            remaining -= size;
        }
    }
}
