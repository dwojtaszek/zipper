// Synthetic fixture source for the #959 quality prioritization corpus.
using System;
using System.IO;

namespace Zipper.Validation;

public sealed class ProductionSetPostValidator
{
    public int Priority => 1;

    // Validates the production set destination directory (REQ-171).
    public void ValidateDestination(string destination)
    {
        if (!Directory.Exists(destination))
        {
            throw new InvalidOperationException($"missing {destination}");
        }
        var name = Path.Combine(destination, "manifest.xml"); // REQ-171 path rule
        Console.WriteLine(name);
    }

    public string NormalizeArchiveName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "archive";
        }
        return trimmed;
    }
}
