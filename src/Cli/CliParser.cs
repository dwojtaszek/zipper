namespace Zipper.Cli;

public static class CliParser
{
    public static ParsedArguments? Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var parsed = new ParsedArguments();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--type":
                    if (TryGetValue(args, i, out var fileType))
                    {
                        parsed.FileType = fileType;
                        i++;
                    }
                    else
                    {
                        Console.Error.WriteLine("Error: --type requires a value.");
                        return null;
                    }

                    break;
                case "--count":
                    if (TryGetValue(args, i, out var countStr))
                    {
                        if (long.TryParse(countStr, out var count))
                        {
                            parsed.Count = count;
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: Invalid value for --count: '{countStr}'");
                            return null;
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine("Error: --count requires a value.");
                        return null;
                    }

                    break;
                case "--output-path":
                    if (TryGetValue(args, i, out var pathArg))
                    {
                        var dir = PathValidator.ValidateAndCreateDirectory(pathArg, Directory.GetCurrentDirectory());
                        if (dir == null)
                        {
                            return null;
                        }

                        parsed.OutputDirectory = dir;
                        i++;
                    }
                    else
                    {
                        Console.Error.WriteLine("Error: --output-path requires a value.");
                        return null;
                    }

                    break;
                case "--folders":
                    if (TryGetValue(args, i, out var foldersStr))
                    {
                        if (int.TryParse(foldersStr, out var folders))
                        {
                            parsed.Folders = folders;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: Invalid value for --folders: '{foldersStr}'");
                            return null;
                        }

                        i++;
                    }

                    break;
                case "--encoding":
                    if (TryGetValue(args, i, out var encoding))
                    {
                        parsed.Encoding = encoding;
                        parsed.IsEncodingExplicit = true;
                        i++;
                    }

                    break;
                case "--distribution":
                    if (TryGetValue(args, i, out var dist))
                    {
                        parsed.Distribution = dist;
                        i++;
                    }

                    break;
                case "--with-metadata":
                    parsed.WithMetadata = true;
                    break;
                case "--with-text":
                    parsed.WithText = true;
                    break;
                case "--attachment-rate":
                    if (TryGetValue(args, i, out var attRateStr))
                    {
                        if (int.TryParse(attRateStr, out var attachmentRate))
                        {
                            parsed.AttachmentRate = attachmentRate;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: Invalid value for --attachment-rate: '{attRateStr}'");
                            return null;
                        }

                        i++;
                    }

                    break;
                case "--target-zip-size":
                    if (TryGetValue(args, i, out var zipSize))
                    {
                        parsed.TargetZipSize = zipSize;
                        i++;
                    }

                    break;
                case "--include-load-file":
                    parsed.IncludeLoadFile = true;
                    break;
                case "--load-file-format":
                    if (TryGetValue(args, i, out var loadFmt))
                    {
                        parsed.LoadFileFormat = loadFmt;
                        parsed.IsLoadFileFormatExplicit = true;
                        i++;
                    }

                    break;
                case "--bates-prefix":
                    if (TryGetValue(args, i, out var batesPfx))
                    {
                        parsed.BatesPrefix = batesPfx;
                        i++;
                    }

                    break;
                case "--bates-start":
                    if (TryGetValue(args, i, out var batesStartStr))
                    {
                        if (long.TryParse(batesStartStr, out var batesStart))
                        {
                            parsed.BatesStart = batesStart;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: Invalid value for --bates-start: '{batesStartStr}'");
                            return null;
                        }

                        i++;
                    }

                    break;
                case "--bates-digits":
                    if (TryGetValue(args, i, out var batesDigitsStr))
                    {
                        if (int.TryParse(batesDigitsStr, out var batesDigits))
                        {
                            parsed.BatesDigits = batesDigits;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: Invalid value for --bates-digits: '{batesDigitsStr}'");
                            return null;
                        }

                        i++;
                    }

                    break;
                case "--tiff-pages":
                    if (TryGetValue(args, i, out var tiffPages))
                    {
                        parsed.TiffPagesRange = tiffPages;
                        i++;
                    }

                    break;
                case "--column-profile":
                    if (TryGetValue(args, i, out var colProf))
                    {
                        parsed.ColumnProfile = colProf;
                        i++;
                    }

                    break;
                case "--seed":
                    if (TryGetValue(args, i, out var seedStr))
                    {
                        if (int.TryParse(seedStr, out var seed))
                        {
                            parsed.Seed = seed;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: Invalid value for --seed: '{seedStr}'");
                            return null;
                        }

                        i++;
                    }

                    break;
                case "--date-format":
                    if (TryGetValue(args, i, out var dateFmt))
                    {
                        parsed.DateFormat = dateFmt;
                        i++;
                    }

                    break;
                case "--empty-percentage":
                    if (TryGetValue(args, i, out var emptyPctStr))
                    {
                        if (int.TryParse(emptyPctStr, out var emptyPct))
                        {
                            parsed.EmptyPercentage = emptyPct;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: Invalid value for --empty-percentage: '{emptyPctStr}'");
                            return null;
                        }

                        i++;
                    }

                    break;
                case "--custodian-count":
                    if (TryGetValue(args, i, out var custCountStr))
                    {
                        if (int.TryParse(custCountStr, out var custCount))
                        {
                            parsed.CustodianCount = custCount;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: Invalid value for --custodian-count: '{custCountStr}'");
                            return null;
                        }

                        i++;
                    }

                    break;
                case "--with-families":
                    parsed.WithFamilies = true;
                    break;
                case "--load-file-formats":
                    if (TryGetValue(args, i, out var loadFmts))
                    {
                        parsed.LoadFileFormats = loadFmts;
                        parsed.IsLoadFileFormatExplicit = true;
                        i++;
                    }

                    break;
                case "--dat-delimiters":
                    if (TryGetValue(args, i, out var datDelims))
                    {
                        parsed.DatDelimiters = datDelims;
                        i++;
                    }

                    break;
                case "--delimiter-column":
                    if (TryGetValue(args, i, out var delCol))
                    {
                        parsed.DelimiterColumn = delCol;
                        i++;
                    }
                    else
                    {
                        Console.Error.WriteLine("Error: --delimiter-column requires a value.");
                        return null;
                    }

                    break;
                case "--delimiter-quote":
                    if (TryGetValue(args, i, out var delQuote))
                    {
                        parsed.DelimiterQuote = delQuote;
                        i++;
                    }
                    else
                    {
                        Console.Error.WriteLine("Error: --delimiter-quote requires a value.");
                        return null;
                    }

                    break;
                case "--delimiter-newline":
                    if (TryGetValue(args, i, out var delNew))
                    {
                        parsed.DelimiterNewline = delNew;
                        i++;
                    }
                    else
                    {
                        Console.Error.WriteLine("Error: --delimiter-newline requires a value.");
                        return null;
                    }

                    break;
                case "--loadfile-only":
                    parsed.LoadfileOnly = true;
                    break;
                case "--loadfile-format":
                    if (TryGetValue(args, i, out var lfFmt))
                    {
                        parsed.LoadFileFormat = lfFmt;
                        parsed.IsLoadFileFormatExplicit = true;
                        i++;
                    }

                    break;
                case "--eol":
                    if (TryGetValue(args, i, out var eolVal))
                    {
                        parsed.Eol = eolVal;
                        i++;
                    }

                    break;
                case "--col-delim":
                    if (TryGetValue(args, i, out var colDelimVal))
                    {
                        parsed.ColDelim = colDelimVal;
                        i++;
                    }

                    break;
                case "--quote-delim":
                    if (TryGetValue(args, i, out var quoteDelimVal))
                    {
                        parsed.QuoteDelim = quoteDelimVal;
                        i++;
                    }

                    break;
                case "--newline-delim":
                    if (TryGetValue(args, i, out var newlineDelimVal))
                    {
                        parsed.NewlineDelim = newlineDelimVal;
                        i++;
                    }

                    break;
                case "--multi-delim":
                    if (TryGetValue(args, i, out var multiDelimVal))
                    {
                        parsed.MultiDelim = multiDelimVal;
                        i++;
                    }

                    break;
                case "--nested-delim":
                    if (TryGetValue(args, i, out var nestedDelimVal))
                    {
                        parsed.NestedDelim = nestedDelimVal;
                        i++;
                    }

                    break;
                case "--chaos-mode":
                    parsed.ChaosMode = true;
                    break;
                case "--chaos-amount":
                    if (TryGetValue(args, i, out var chaosAmtVal))
                    {
                        parsed.ChaosAmount = chaosAmtVal;
                        i++;
                    }

                    break;
                case "--chaos-types":
                    if (TryGetValue(args, i, out var chaosTypesVal))
                    {
                        parsed.ChaosTypes = chaosTypesVal;
                        i++;
                    }

                    break;
                case "--chaos-scenario":
                    if (TryGetValue(args, i, out var chaosScenarioVal))
                    {
                        parsed.ChaosScenario = chaosScenarioVal;
                        i++;
                    }

                    break;
                case "--chaos-list":
                    parsed.ChaosList = true;
                    break;
                case "--production-set":
                    parsed.ProductionSet = true;
                    break;
                case "--production-zip":
                    parsed.ProductionZip = true;
                    break;
                case "--volume-size":
                    if (TryGetValue(args, i, out var volumeSizeVal))
                    {
                        if (int.TryParse(volumeSizeVal, out var volumeSize))
                        {
                            parsed.VolumeSize = volumeSize;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: Invalid value for --volume-size: '{volumeSizeVal}'");
                            return null;
                        }

                        i++;
                    }

                    break;
                default:
                    Console.Error.WriteLine($"Warning: Unknown argument or unconsumed value '{args[i]}' ignored.");
                    break;
            }
        }

        return parsed;
    }

    private static bool TryGetValue(string[] args, int currentIndex, out string value)
    {
        if (currentIndex + 1 < args.Length && !IsParameterlessFlag(args[currentIndex + 1]))
        {
            value = args[currentIndex + 1];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsParameterlessFlag(string arg)
    {
        if (!arg.StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        return CliOptions.ParameterlessFlags.Contains(arg);
    }
}
