using System.Collections.Generic;

namespace Zipper;

/// <summary>
/// Shared hash computation used by both ParallelFileGenerator and ProductionSetGenerator.
/// Eliminates the duplicated ComputeActualHashes / ComputeSimulatedHashes logic.
/// </summary>
internal class HashComputer : IHashComputer
{
    /// <summary>
    /// Computes real hashes from raw bytes.
    /// </summary>
    public virtual IReadOnlyDictionary<HashAlgorithm, string> ComputeActualHashes(
        byte[] content,
        HashConfig hashConfig)
    {
        return ComputeActualHashes(content.AsSpan(), hashConfig);
    }

    /// <summary>
    /// Computes real hashes from a byte span.
    /// </summary>
    public virtual IReadOnlyDictionary<HashAlgorithm, string> ComputeActualHashes(
        System.ReadOnlySpan<byte> data,
        HashConfig hashConfig)
    {
        var dict = new Dictionary<HashAlgorithm, string>(hashConfig.Algorithms.Count);
        foreach (var algo in hashConfig.Algorithms)
        {
            dict[algo] = HashUtility.ComputeHashHex(data, algo);
        }

        return dict;
    }

    /// <summary>
    /// Computes deterministic simulated hashes for the given work item.
    /// </summary>
    public virtual IReadOnlyDictionary<HashAlgorithm, string> ComputeSimulatedHashes(
        FileWorkItem workItem,
        HashConfig hashConfig,
        FileGenerationRequest request)
    {
        var dict = new Dictionary<HashAlgorithm, string>(hashConfig.Algorithms.Count);
        var rng = HashUtility.CreateSeededRandom(request, workItem.Index);

        foreach (var algo in hashConfig.Algorithms)
        {
            dict[algo] = HashUtility.GenerateSimulatedHash(algo, rng);
        }

        return dict;
    }
}
