#!/bin/bash
set -e

# We will apply the changes using python script for robustness
cat << 'PYEOF' > refactor.py
import re

# 1. ParsedArguments.cs
with open('src/Cli/ParsedArguments.cs', 'r') as f:
    parsed_args = f.read()

# remove unnecessary properties
to_remove = [
    "    public string? ParsedColDelim { get; set; }\n\n",
    "    public string? ParsedQuoteDelim { get; set; }\n\n",
    "    public string? ParsedNewlineDelim { get; set; }\n\n",
    "    public string? ParsedMultiDelim { get; set; }\n\n",
    "    public string? ParsedNestedDelim { get; set; }\n\n",
    "    public string? ParsedDelimiterColumn { get; set; }\n\n",
    "    public string? ParsedDelimiterQuote { get; set; }\n\n",
    "    public string? ParsedDelimiterNewline { get; set; }\n\n",
]

for prop in to_remove:
    parsed_args = parsed_args.replace(prop, "")

with open('src/Cli/ParsedArguments.cs', 'w') as f:
    f.write(parsed_args)

# 2. RequestBuilder.cs
with open('src/Cli/RequestBuilder.cs', 'r') as f:
    req_builder = f.read()

# Replace parsed.Parsed... with parse method calls
old_lines = """        if (!string.IsNullOrEmpty(parsed.DelimiterColumn) && !string.IsNullOrEmpty(parsed.ParsedDelimiterColumn))
        {
            columnDelim = parsed.ParsedDelimiterColumn;
        }

        if (!string.IsNullOrEmpty(parsed.DelimiterQuote) && !string.IsNullOrEmpty(parsed.ParsedDelimiterQuote))
        {
            quoteDelim = parsed.ParsedDelimiterQuote;
        }

        if (!string.IsNullOrEmpty(parsed.DelimiterNewline) && !string.IsNullOrEmpty(parsed.ParsedDelimiterNewline))
        {
            newlineDelim = parsed.ParsedDelimiterNewline;
        }

        if (!string.IsNullOrEmpty(parsed.ColDelim) && !string.IsNullOrEmpty(parsed.ParsedColDelim))
        {
            columnDelim = parsed.ParsedColDelim;
        }

        if (!string.IsNullOrEmpty(parsed.QuoteDelim) && parsed.ParsedQuoteDelim != null)
        {
            quoteDelim = parsed.ParsedQuoteDelim;
        }

        if (!string.IsNullOrEmpty(parsed.NewlineDelim) && !string.IsNullOrEmpty(parsed.ParsedNewlineDelim))
        {
            newlineDelim = parsed.ParsedNewlineDelim;
        }

        string multiDelim = ";";
        if (!string.IsNullOrEmpty(parsed.MultiDelim) && !string.IsNullOrEmpty(parsed.ParsedMultiDelim))
        {
            multiDelim = parsed.ParsedMultiDelim;
        }

        string nestedDelim = "\\";
        if (!string.IsNullOrEmpty(parsed.NestedDelim) && !string.IsNullOrEmpty(parsed.ParsedNestedDelim))
        {
            nestedDelim = parsed.ParsedNestedDelim;
        }"""

new_lines = """        if (!string.IsNullOrEmpty(parsed.DelimiterColumn))
        {
            columnDelim = ParseDelimiterArgument(parsed.DelimiterColumn!);
        }

        if (!string.IsNullOrEmpty(parsed.DelimiterQuote))
        {
            quoteDelim = ParseDelimiterArgument(parsed.DelimiterQuote!);
        }

        if (!string.IsNullOrEmpty(parsed.DelimiterNewline))
        {
            newlineDelim = ParseDelimiterArgument(parsed.DelimiterNewline!);
        }

        if (!string.IsNullOrEmpty(parsed.ColDelim))
        {
            columnDelim = ParseStrictDelimiter(parsed.ColDelim!);
        }

        if (!string.IsNullOrEmpty(parsed.QuoteDelim))
        {
            quoteDelim = parsed.QuoteDelim!.Equals("none", StringComparison.OrdinalIgnoreCase) ? string.Empty : ParseStrictDelimiter(parsed.QuoteDelim);
        }

        if (!string.IsNullOrEmpty(parsed.NewlineDelim))
        {
            newlineDelim = ParseStrictDelimiter(parsed.NewlineDelim!);
        }

        string multiDelim = ";";
        if (!string.IsNullOrEmpty(parsed.MultiDelim))
        {
            multiDelim = ParseStrictDelimiter(parsed.MultiDelim!);
        }

        string nestedDelim = "\\";
        if (!string.IsNullOrEmpty(parsed.NestedDelim))
        {
            nestedDelim = ParseStrictDelimiter(parsed.NestedDelim!);
        }"""

req_builder = req_builder.replace(old_lines, new_lines)

with open('src/Cli/RequestBuilder.cs', 'w') as f:
    f.write(req_builder)

# 3. ProductionSetValidator.cs
with open('src/Cli/Validation/ProductionSetValidator.cs', 'r') as f:
    prod_val = f.read()

prod_val_new = """namespace Zipper.Cli.Validation;

internal static class ProductionSetValidator
{
    public static bool Validate(ParsedArguments parsed)
    {
        return ValidateDependencies(parsed) &&
               ValidateParameters(parsed) &&
               ValidatePrefix(parsed);
    }

    private static bool ValidateDependencies(ParsedArguments parsed)
    {
        if (parsed.ProductionSet)
        {
            if (parsed.LoadfileOnly)
            {
                Console.Error.WriteLine("Error: --production-set conflicts with --loadfile-only.");
                return false;
            }

            if (string.IsNullOrEmpty(parsed.BatesPrefix))
            {
                Console.Error.WriteLine("Error: --production-set requires --bates-prefix.");
                return false;
            }

            if (parsed.VolumeSize.HasValue && parsed.VolumeSize.Value < 1)
            {
                Console.Error.WriteLine("Error: --volume-size must be at least 1.");
                return false;
            }
        }

        if (parsed.ProductionZip && !parsed.ProductionSet)
        {
            Console.Error.WriteLine("Error: --production-zip requires --production-set.");
            return false;
        }

        if (parsed.VolumeSize.HasValue && !parsed.ProductionSet)
        {
            Console.Error.WriteLine("Error: --volume-size requires --production-set.");
            return false;
        }

        return true;
    }

    private static bool ValidateParameters(ParsedArguments parsed)
    {
        if (parsed.BatesStart.HasValue && parsed.BatesStart.Value < 0)
        {
            Console.Error.WriteLine("Error: Bates start number must be non-negative.");
            return false;
        }

        if (parsed.BatesDigits.HasValue && (parsed.BatesDigits.Value < 1 || parsed.BatesDigits.Value > 20))
        {
            Console.Error.WriteLine("Error: Bates digits must be between 1 and 20.");
            return false;
        }

        return true;
    }

    private static bool ValidatePrefix(ParsedArguments parsed)
    {
        if (!string.IsNullOrEmpty(parsed.BatesPrefix))
        {
            if (parsed.BatesPrefix.Contains('/', StringComparison.Ordinal) || parsed.BatesPrefix.Contains('\\\\', StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Error: --bates-prefix must not contain path separators.");
                return false;
            }

            if (string.Equals(parsed.BatesPrefix, "..", StringComparison.Ordinal) || parsed.BatesPrefix.Contains("../", StringComparison.Ordinal) || parsed.BatesPrefix.Contains("..\\\\", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Error: --bates-prefix must not contain directory traversal sequences.");
                return false;
            }

            if (!parsed.BatesPrefix.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
            {
                Console.Error.WriteLine("Error: --bates-prefix must only contain letters, digits, underscores, and hyphens.");
                return false;
            }
        }

        return true;
    }
}
"""

with open('src/Cli/Validation/ProductionSetValidator.cs', 'w') as f:
    f.write(prod_val_new)

PYEOF
python3 refactor.py
