#!/bin/bash
set -e

cat << 'PYEOF' > refactor_rb.py
with open('src/Cli/RequestBuilder.cs', 'r') as f:
    text = f.read()

import re

# Replace parsed.ParsedDelimiterColumn, etc. with parsing
text = re.sub(r'if \(!string\.IsNullOrEmpty\(parsed\.DelimiterColumn\) && !string\.IsNullOrEmpty\(parsed\.ParsedDelimiterColumn\)\)\s*\{\s*columnDelim = parsed\.ParsedDelimiterColumn;\s*\}', 
              'if (!string.IsNullOrEmpty(parsed.DelimiterColumn))\n        {\n            columnDelim = ParseDelimiterArgument(parsed.DelimiterColumn);\n        }', text)

text = re.sub(r'if \(!string\.IsNullOrEmpty\(parsed\.DelimiterQuote\) && !string\.IsNullOrEmpty\(parsed\.ParsedDelimiterQuote\)\)\s*\{\s*quoteDelim = parsed\.ParsedDelimiterQuote;\s*\}',
              'if (!string.IsNullOrEmpty(parsed.DelimiterQuote))\n        {\n            quoteDelim = ParseDelimiterArgument(parsed.DelimiterQuote);\n        }', text)

text = re.sub(r'if \(!string\.IsNullOrEmpty\(parsed\.DelimiterNewline\) && !string\.IsNullOrEmpty\(parsed\.ParsedDelimiterNewline\)\)\s*\{\s*newlineDelim = parsed\.ParsedDelimiterNewline;\s*\}',
              'if (!string.IsNullOrEmpty(parsed.DelimiterNewline))\n        {\n            newlineDelim = ParseDelimiterArgument(parsed.DelimiterNewline);\n        }', text)

text = re.sub(r'if \(!string\.IsNullOrEmpty\(parsed\.ColDelim\) && !string\.IsNullOrEmpty\(parsed\.ParsedColDelim\)\)\s*\{\s*columnDelim = parsed\.ParsedColDelim;\s*\}',
              'if (!string.IsNullOrEmpty(parsed.ColDelim))\n        {\n            columnDelim = ParseStrictDelimiter(parsed.ColDelim);\n        }', text)

text = re.sub(r'if \(!string\.IsNullOrEmpty\(parsed\.QuoteDelim\) && parsed\.ParsedQuoteDelim != null\)\s*\{\s*quoteDelim = parsed\.ParsedQuoteDelim;\s*\}',
              'if (!string.IsNullOrEmpty(parsed.QuoteDelim))\n        {\n            quoteDelim = parsed.QuoteDelim.Equals("none", StringComparison.OrdinalIgnoreCase) ? string.Empty : ParseStrictDelimiter(parsed.QuoteDelim);\n        }', text)

text = re.sub(r'if \(!string\.IsNullOrEmpty\(parsed\.NewlineDelim\) && !string\.IsNullOrEmpty\(parsed\.ParsedNewlineDelim\)\)\s*\{\s*newlineDelim = parsed\.ParsedNewlineDelim;\s*\}',
              'if (!string.IsNullOrEmpty(parsed.NewlineDelim))\n        {\n            newlineDelim = ParseStrictDelimiter(parsed.NewlineDelim);\n        }', text)

text = re.sub(r'if \(!string\.IsNullOrEmpty\(parsed\.MultiDelim\) && !string\.IsNullOrEmpty\(parsed\.ParsedMultiDelim\)\)\s*\{\s*multiDelim = parsed\.ParsedMultiDelim;\s*\}',
              'if (!string.IsNullOrEmpty(parsed.MultiDelim))\n        {\n            multiDelim = ParseStrictDelimiter(parsed.MultiDelim);\n        }', text)

text = re.sub(r'if \(!string\.IsNullOrEmpty\(parsed\.NestedDelim\) && !string\.IsNullOrEmpty\(parsed\.ParsedNestedDelim\)\)\s*\{\s*nestedDelim = parsed\.ParsedNestedDelim;\s*\}',
              'if (!string.IsNullOrEmpty(parsed.NestedDelim))\n        {\n            nestedDelim = ParseStrictDelimiter(parsed.NestedDelim);\n        }', text)

with open('src/Cli/RequestBuilder.cs', 'w') as f:
    f.write(text)

with open('src/Cli/Validation/CrossCuttingValidator.cs', 'r') as f:
    text = f.read()

text = text.replace('_ when value.EndsWith("%") =>', '_ when value.EndsWith("%", StringComparison.Ordinal) =>')

with open('src/Cli/Validation/CrossCuttingValidator.cs', 'w') as f:
    f.write(text)

with open('src/Zipper.Tests/RequestBuilderTests.cs', 'r') as f:
    text = f.read()

# Assert the return value of CliValidator.Validate(parsed)
text = text.replace('CliValidator.Validate(parsed);', 'Assert.True(CliValidator.Validate(parsed));')

with open('src/Zipper.Tests/RequestBuilderTests.cs', 'w') as f:
    f.write(text)

PYEOF
python3 refactor_rb.py
dotnet build
