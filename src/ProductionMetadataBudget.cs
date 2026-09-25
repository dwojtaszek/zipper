namespace Zipper;

/// <summary>
/// The Azure Blob custom-metadata budget for a Production Manifest's Production Metadata block
/// (REQ-225, REQ-226). Both enforcement points measure through this type so the writer's
/// by-construction guard and <c>ProductionSetPostValidator</c>'s artifact finding cannot drift
/// apart: the accounting is the sum of the UTF-8 byte counts of every key and every value, and
/// a block of exactly <see cref="MaxBytes"/> bytes is within budget.
/// </summary>
internal static class ProductionMetadataBudget
{
    /// <summary>Azure Blob caps an upload's custom metadata at 8 KB across all keys and values (ticket #881).</summary>
    internal const int MaxBytes = 8192;

    /// <summary>UTF-8 byte cost of one key/value pair against the budget.</summary>
    internal static long PairBytes(string key, string value) =>
        System.Text.Encoding.UTF8.GetByteCount(key) + System.Text.Encoding.UTF8.GetByteCount(value);
}
