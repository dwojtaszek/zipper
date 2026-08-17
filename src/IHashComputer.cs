using System.Collections.Generic;
using Zipper.Config;

namespace Zipper;

/// <summary>
/// Abstraction for hash computation so ProductionSet tests can verify hash behavior without static coupling.
/// </summary>
internal interface IHashComputer
{
    /// <summary>Computes hashes for the given content using the request's hash configuration.</summary>
    IReadOnlyDictionary<HashAlgorithm, string>? ComputeHashes(
        byte[] content,
        HashConfig hashConfig,
        FileWorkItem workItem,
        FileGenerationRequest request);
}
