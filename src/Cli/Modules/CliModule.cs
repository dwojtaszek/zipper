namespace Zipper.Cli.Modules;

/// <summary>
/// A domain-scoped CLI module: owns the flags, argument parsing, validation, and
/// config construction for one sub-domain of FileGenerationRequest.
/// </summary>
public abstract class CliModule
{
    /// <summary>Flag names this module consumes (lowercase, with "--" prefix).</summary>
    public abstract IReadOnlyCollection<string> OwnedFlags { get; }

    /// <summary>Whether the flag consumes a following value token. Parameterless flags override to false.</summary>
    public virtual bool TakesValue(string flag) => true;

    /// <summary>
    /// Applies one flag token. <paramref name="value"/> is null for parameterless flags.
    /// Returns false (after writing to Console.Error) on a hard parse failure.
    /// OwnedFlags and the TryApply switch must stay identical: a silent
    /// <c>default: return false</c> would drop the current
    /// "Error: Unknown argument..." line.
    /// </summary>
    public abstract bool TryApply(string flag, string? value);

    public bool Owns(string flag) => OwnedFlags.Contains(flag);
}
