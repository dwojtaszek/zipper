using System.Diagnostics;
using System.Text;
using Zipper.Profiles;

namespace Zipper
{
    /// <summary>
    /// Provides validation and parsing for command line arguments.
    /// </summary>
    public static class CommandLineValidator
    {
        private static readonly Dictionary<string, long> SizeMultipliers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["KB"] = 1024,
            ["MB"] = 1024 * 1024,
            ["GB"] = 1024 * 1024 * 1024,
        };

        /// <summary>
        /// Validates and parses command line arguments into a FileGenerationRequest.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>Validated FileGenerationRequest, or null if validation fails.</returns>
        public static FileGenerationRequest? ValidateAndParseArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                ShowUsage();
                return null;
            }

            // Parse arguments
            var parsedArgs = ParseArguments(args);
            if (parsedArgs == null)
            {
                return null;
            }

            // Validate required arguments
            if (!ValidateRequiredArguments(parsedArgs))
            {
                ShowUsage();
                return null;
            }

            // Validate optional arguments
            if (!ValidateOptionalArguments(parsedArgs))
            {
                return null;
            }

            // Convert to FileGenerationRequest
            return CreateFileGenerationRequest(parsedArgs);
        }

        /// <summary>
        /// Shows the usage information.
        /// </summary>
        public static void ShowUsage()
        {
            var exeName = Process.GetCurrentProcess().ProcessName;
            Console.Error.WriteLine("Error: Missing required arguments.");
            Console.Error.WriteLine($"Usage: {exeName} --type <pdf|jpg|tiff|eml|docx|xlsx> --count <number> --output-path <directory> [options]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Required Arguments:");
            Console.Error.WriteLine("  --type <string>          File type: pdf, jpg, tiff, eml, docx, xlsx");
            Console.Error.WriteLine("  --count <number>         Number of files to generate");
            Console.Error.WriteLine("  --output-path <path>     Output directory path");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Optional Arguments:");
            Console.Error.WriteLine("  --folders <number>       Number of folders (1-100, default: 1)");
            Console.Error.WriteLine("  --encoding <string>      Encoding: UTF-8, UTF-16, ANSI (default: UTF-8)");
            Console.Error.WriteLine("  --distribution <string>  Distribution: proportional, gaussian, exponential");
            Console.Error.WriteLine("  --with-metadata          Include metadata columns in load file");
            Console.Error.WriteLine("  --with-text              Generate extracted text files");
            Console.Error.WriteLine("  --attachment-rate <n>    EML attachment percentage (0-100, default: 0)");
            Console.Error.WriteLine("  --target-zip-size <size> Target ZIP size (e.g., 500MB, 10GB)");
            Console.Error.WriteLine("  --include-load-file      Include load file in ZIP archive");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Load File Options:");
            Console.Error.WriteLine("  --load-file-format <fmt> Load file format: dat, opt, csv, edrm-xml (default: dat)");
            Console.Error.WriteLine("  --load-file-formats <f>  Multiple formats comma-separated (e.g., dat,opt,csv)");
            Console.Error.WriteLine("  --dat-delimiters <type>  DAT delimiter style: standard, csv (default: standard)");
            Console.Error.WriteLine("  --delimiter-column <c>   Custom column delimiter (char or ASCII code)");
            Console.Error.WriteLine("  --delimiter-quote <c>    Custom quote delimiter (char or ASCII code)");
            Console.Error.WriteLine("  --delimiter-newline <c>  Custom newline replacement (char or ASCII code)");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Loadfile-Only Options:");
            Console.Error.WriteLine("  --loadfile-only          Skip ZIP/native generation, stream directly to load file");
            Console.Error.WriteLine("  --loadfile-format <f>    Load file schema: dat, opt (default: dat)");
            Console.Error.WriteLine("  --eol <CRLF|LF|CR>      End-of-line format (default: CRLF)");
            Console.Error.WriteLine("  --col-delim <value>      Column delimiter (ascii:<N> or char:<c>)");
            Console.Error.WriteLine("  --quote-delim <value>    Quote delimiter (ascii:<N>, char:<c>, or none)");
            Console.Error.WriteLine("  --newline-delim <value>  In-cell newline replacement (ascii:<N> or char:<c>)");
            Console.Error.WriteLine("  --multi-delim <value>    Multi-value delimiter (ascii:<N> or char:<c>)");
            Console.Error.WriteLine("  --nested-delim <value>   Nested-value delimiter (ascii:<N> or char:<c>)");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Chaos Engine Options:");
            Console.Error.WriteLine("  --chaos-mode             Activate deliberate anomaly injection");
            Console.Error.WriteLine("  --chaos-amount <value>   Anomaly count: percentage (1%) or exact (500)");
            Console.Error.WriteLine("  --chaos-types <list>     Comma-separated anomaly types to inject");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Bates Numbering:");
            Console.Error.WriteLine("  --bates-prefix <string>  Bates number prefix (e.g., CLIENT001)");
            Console.Error.WriteLine("  --bates-start <number>   Bates start number (default: 1)");
            Console.Error.WriteLine("  --bates-digits <number>  Bates digit count (default: 8)");
            Console.Error.WriteLine();
            Console.Error.WriteLine("TIFF Options:");
            Console.Error.WriteLine("  --tiff-pages <min-max>   TIFF page range (e.g., 1-20, default: 1-1)");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Column Profile Options:");
            Console.Error.WriteLine("  --column-profile <name>  Built-in profile: minimal, standard, litigation, full");
            Console.Error.WriteLine("                           Or path to custom JSON profile file");
            Console.Error.WriteLine("  --seed <number>          Random seed for reproducible output");
            Console.Error.WriteLine("  --date-format <fmt>      Override date format (e.g., yyyy-MM-dd)");
            Console.Error.WriteLine("  --empty-percentage <n>   Override empty value percentage (0-100)");
            Console.Error.WriteLine("  --custodian-count <n>    Override custodian count (max: 1000)");
            Console.Error.WriteLine("  --with-families          Generate parent-child document relationships");
        }

        /// <summary>
        /// Parses command line arguments into a dictionary.
        /// </summary>
        private static ParsedArguments? ParseArguments(string[] args)
        {
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
                            parsed.OutputDirectory = PathValidator.ValidateAndCreateDirectory(pathArg);
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine("Error: --output-path requires a value.");
                            return null;
                        }

                        break;
                    case "--folders":
                        if (TryGetValue(args, i, out var foldersStr) && int.TryParse(foldersStr, out var folders))
                        {
                            parsed.Folders = folders;
                            i++;
                        }

                        break;
                    case "--encoding":
                        if (TryGetValue(args, i, out var encoding))
                        {
                            parsed.Encoding = encoding;
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
                        if (TryGetValue(args, i, out var attRateStr) && int.TryParse(attRateStr, out var attachmentRate))
                        {
                            parsed.AttachmentRate = attachmentRate;
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
                        if (TryGetValue(args, i, out var batesStartStr) && long.TryParse(batesStartStr, out var batesStart))
                        {
                            parsed.BatesStart = batesStart;
                            i++;
                        }

                        break;
                    case "--bates-digits":
                        if (TryGetValue(args, i, out var batesDigitsStr) && int.TryParse(batesDigitsStr, out var batesDigits))
                        {
                            parsed.BatesDigits = batesDigits;
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

                    // New column profile arguments
                    case "--column-profile":
                        if (TryGetValue(args, i, out var colProf))
                        {
                            parsed.ColumnProfile = colProf;
                            i++;
                        }

                        break;
                    case "--seed":
                        if (TryGetValue(args, i, out var seedStr) && int.TryParse(seedStr, out var seed))
                        {
                            parsed.Seed = seed;
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
                        if (TryGetValue(args, i, out var emptyPctStr) && int.TryParse(emptyPctStr, out var emptyPct))
                        {
                            parsed.EmptyPercentage = emptyPct;
                            i++;
                        }

                        break;
                    case "--custodian-count":
                        if (TryGetValue(args, i, out var custCountStr) && int.TryParse(custCountStr, out var custCount))
                        {
                            parsed.CustodianCount = custCount;
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

                        break;
                    case "--delimiter-quote":
                        if (TryGetValue(args, i, out var delQuote))
                        {
                            parsed.DelimiterQuote = delQuote;
                            i++;
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

                    // Loadfile-only mode arguments
                    case "--loadfile-only":
                        parsed.LoadfileOnly = true;
                        break;
                    case "--loadfile-format":
                        if (TryGetValue(args, i, out var lfFmt))
                        {
                            parsed.LoadFileFormat = lfFmt;
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

                    // Chaos Engine arguments
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
            if (!arg.StartsWith("--"))
            {
                return false;
            }

            return arg.ToLowerInvariant() switch
            {
                "--with-metadata" => true,
                "--with-text" => true,
                "--include-load-file" => true,
                "--with-families" => true,
                "--loadfile-only" => true,
                "--chaos-mode" => true,
                _ => false,
            };
        }

        /// <summary>
        /// Validates required arguments.
        /// </summary>
        private static bool ValidateRequiredArguments(ParsedArguments parsed)
        {
            // In loadfile-only mode, --type is optional (defaults to pdf for schema)
            if (string.IsNullOrEmpty(parsed.FileType) && !parsed.LoadfileOnly)
            {
                Console.Error.WriteLine("Error: --type is required.");
                return false;
            }

            if (!parsed.Count.HasValue)
            {
                Console.Error.WriteLine("Error: --count is required.");
                return false;
            }

            if (parsed.OutputDirectory == null)
            {
                Console.Error.WriteLine("Error: --output-path is required or was invalid.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates optional arguments and constraints.
        /// </summary>
        private static bool ValidateOptionalArguments(ParsedArguments parsed)
        {
            // Validate target zip size requires count
            if (!string.IsNullOrEmpty(parsed.TargetZipSize) && !parsed.Count.HasValue)
            {
                Console.Error.WriteLine("Error: --target-zip-size requires --count to be specified.");
                return false;
            }

            // Validate folders range
            if (parsed.Folders < 1 || parsed.Folders > 100)
            {
                Console.Error.WriteLine("Error: Number of folders must be between 1 and 100.");
                return false;
            }

            // Validate attachment rate range
            if (parsed.AttachmentRate < 0 || parsed.AttachmentRate > 100)
            {
                Console.Error.WriteLine("Error: Attachment rate must be between 0 and 100.");
                return false;
            }

            // Validate encoding
            if (!string.IsNullOrEmpty(parsed.Encoding) && GetEncodingFromName(parsed.Encoding) == null)
            {
                Console.Error.WriteLine($"Error: Invalid encoding '{parsed.Encoding}'. Supported values are UTF-8, UTF-16, ANSI.");
                return false;
            }

            // Validate distribution type
            if (!string.IsNullOrEmpty(parsed.Distribution) && GetDistributionFromName(parsed.Distribution) == null)
            {
                Console.Error.WriteLine($"Error: Invalid distribution '{parsed.Distribution}'. Supported values are proportional, gaussian, exponential.");
                return false;
            }

            // Validate target zip size format
            if (!string.IsNullOrEmpty(parsed.TargetZipSize))
            {
                if (ParseSize(parsed.TargetZipSize) == null)
                {
                    Console.Error.WriteLine("Error: Invalid format for --target-zip-size. Use KB, MB, GB, etc. (e.g., 500MB, 10GB).");
                    return false;
                }
            }

            // Validate load file format
            if (!string.IsNullOrEmpty(parsed.LoadFileFormat))
            {
                if (GetLoadFileFormat(parsed.LoadFileFormat) == null)
                {
                    Console.Error.WriteLine("Error: Invalid load file format. Supported values are dat, opt, csv, xml, concordance.");
                    return false;
                }
            }

            // Validate Bates number arguments
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

            // Validate TIFF pages range
            if (!string.IsNullOrEmpty(parsed.TiffPagesRange))
            {
                if (TiffMultiPageGenerator.ParsePageRange(parsed.TiffPagesRange) == null)
                {
                    Console.Error.WriteLine("Error: Invalid TIFF pages range. Use format: <min>-<max> (e.g., 1-20).");
                    return false;
                }
            }

            // Validate column profile
            if (!string.IsNullOrEmpty(parsed.ColumnProfile))
            {
                if (!ColumnProfileLoader.IsBuiltInProfile(parsed.ColumnProfile) && !File.Exists(parsed.ColumnProfile))
                {
                    Console.Error.WriteLine($"Error: Column profile '{parsed.ColumnProfile}' is not a valid built-in profile or file path.");
                    Console.Error.WriteLine($"       Built-in profiles: {string.Join(", ", BuiltInProfiles.ProfileNames)}");
                    return false;
                }
            }

            // Validate empty percentage
            if (parsed.EmptyPercentage.HasValue && (parsed.EmptyPercentage.Value < 0 || parsed.EmptyPercentage.Value > 100))
            {
                Console.Error.WriteLine("Error: Empty percentage must be between 0 and 100.");
                return false;
            }

            // Validate custodian count
            if (parsed.CustodianCount.HasValue && (parsed.CustodianCount.Value < 1 || parsed.CustodianCount.Value > 1000))
            {
                Console.Error.WriteLine("Error: Custodian count must be between 1 and 1000.");
                return false;
            }

            // Validate load file formats (multi)
            if (!string.IsNullOrEmpty(parsed.LoadFileFormats))
            {
                var formats = parsed.LoadFileFormats.Split(',');
                foreach (var fmt in formats)
                {
                    if (GetLoadFileFormat(fmt.Trim()) == null)
                    {
                        Console.Error.WriteLine($"Error: Invalid load file format '{fmt}'. Supported: dat, opt, csv, edrm-xml.");
                        return false;
                    }
                }
            }

            // Validate DAT delimiters
            if (!string.IsNullOrEmpty(parsed.DatDelimiters))
            {
                var delim = parsed.DatDelimiters.ToLowerInvariant();
                if (delim != "standard" && delim != "csv")
                {
                    Console.Error.WriteLine("Error: DAT delimiters must be 'standard' or 'csv'.");
                    return false;
                }
            }

            // Check for --with-metadata and --column-profile conflict
            if (parsed.WithMetadata && !string.IsNullOrEmpty(parsed.ColumnProfile))
            {
                Console.Error.WriteLine("Warning: --column-profile takes precedence over --with-metadata. --with-metadata will be ignored.");
                parsed.WithMetadata = false;
            }

            // === Loadfile-only dependency validation ===

            // Delimiter and format args require --loadfile-only
            var loadfileOnlyArgs = new[] { parsed.Eol, parsed.ColDelim, parsed.QuoteDelim, parsed.NewlineDelim, parsed.MultiDelim, parsed.NestedDelim };
            var loadfileOnlyNames = new[] { "--eol", "--col-delim", "--quote-delim", "--newline-delim", "--multi-delim", "--nested-delim" };
            for (int idx = 0; idx < loadfileOnlyArgs.Length; idx++)
            {
                if (!string.IsNullOrEmpty(loadfileOnlyArgs[idx]) && !parsed.LoadfileOnly)
                {
                    Console.Error.WriteLine($"Error: {loadfileOnlyNames[idx]} requires --loadfile-only.");
                    return false;
                }
            }

            // Chaos mode requires --loadfile-only
            if (parsed.ChaosMode && !parsed.LoadfileOnly)
            {
                Console.Error.WriteLine("Error: --chaos-mode requires --loadfile-only.");
                return false;
            }

            // Chaos amount/types require --chaos-mode
            if (!string.IsNullOrEmpty(parsed.ChaosAmount) && !parsed.ChaosMode)
            {
                Console.Error.WriteLine("Error: --chaos-amount requires --chaos-mode.");
                return false;
            }

            if (!string.IsNullOrEmpty(parsed.ChaosTypes) && !parsed.ChaosMode)
            {
                Console.Error.WriteLine("Error: --chaos-types requires --chaos-mode.");
                return false;
            }

            // Loadfile-only conflicts with ZIP-related options
            if (parsed.LoadfileOnly && !string.IsNullOrEmpty(parsed.TargetZipSize))
            {
                Console.Error.WriteLine("Error: --loadfile-only conflicts with --target-zip-size.");
                return false;
            }

            if (parsed.LoadfileOnly && parsed.IncludeLoadFile)
            {
                Console.Error.WriteLine("Error: --loadfile-only conflicts with --include-load-file.");
                return false;
            }

            // Validate --eol values
            if (!string.IsNullOrEmpty(parsed.Eol))
            {
                var eolUpper = parsed.Eol.ToUpperInvariant();
                if (eolUpper != "CRLF" && eolUpper != "LF" && eolUpper != "CR")
                {
                    Console.Error.WriteLine("Error: --eol must be CRLF, LF, or CR.");
                    return false;
                }
            }

            // Validate strict delimiter prefix (ascii: or char:) for new-style delimiter args
            var strictDelimArgs = new[] { parsed.ColDelim, parsed.NewlineDelim, parsed.MultiDelim, parsed.NestedDelim };
            var strictDelimNames = new[] { "--col-delim", "--newline-delim", "--multi-delim", "--nested-delim" };
            for (int idx = 0; idx < strictDelimArgs.Length; idx++)
            {
                if (!string.IsNullOrEmpty(strictDelimArgs[idx]) && !IsValidStrictDelimiter(strictDelimArgs[idx]!))
                {
                    Console.Error.WriteLine($"Error: {strictDelimNames[idx]} must use 'ascii:<N>' or 'char:<c>' prefix.");
                    return false;
                }
            }

            // --quote-delim allows 'none' in addition to ascii:/char: prefix
            if (!string.IsNullOrEmpty(parsed.QuoteDelim) &&
                !parsed.QuoteDelim.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                !IsValidStrictDelimiter(parsed.QuoteDelim))
            {
                Console.Error.WriteLine("Error: --quote-delim must use 'ascii:<N>', 'char:<c>', or 'none'.");
                return false;
            }

            // Validate --chaos-amount format
            if (!string.IsNullOrEmpty(parsed.ChaosAmount))
            {
                if (!IsValidChaosAmount(parsed.ChaosAmount))
                {
                    Console.Error.WriteLine("Error: --chaos-amount must be a percentage (e.g., '1%') or an exact count (e.g., '500').");
                    return false;
                }
            }

            // Validate --chaos-types names
            if (!string.IsNullOrEmpty(parsed.ChaosTypes))
            {
                var validDat = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mixed-delimiters", "quotes", "columns", "eol", "encoding" };
                var validOpt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "opt-boundary", "opt-columns", "opt-pagecount" };
                var format = GetLoadFileFormat(parsed.LoadFileFormat ?? "dat") ?? LoadFileFormat.Dat;
                var validTypes = format == LoadFileFormat.Opt ? validOpt : validDat;
                var types = parsed.ChaosTypes.Split(',');
                foreach (var t in types)
                {
                    if (!validTypes.Contains(t.Trim()))
                    {
                        Console.Error.WriteLine($"Error: Invalid chaos type '{t.Trim()}'. Valid types for {format}: {string.Join(", ", validTypes)}");
                        return false;
                    }
                }
            }

            // Validate encoding for loadfile-only (accept extended set)
            if (parsed.LoadfileOnly && !string.IsNullOrEmpty(parsed.Encoding))
            {
                var enc = EncodingHelper.GetEncoding(parsed.Encoding);
                if (enc == null)
                {
                    Console.Error.WriteLine($"Error: Invalid encoding '{parsed.Encoding}'. Supported: UTF-8, UTF-16LE, Windows-1252, ASCII.");
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidStrictDelimiter(string value)
        {
            return value.StartsWith("ascii:", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("char:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidChaosAmount(string value)
        {
            if (value.EndsWith('%'))
            {
                return double.TryParse(value.TrimEnd('%'), out var pct) && pct > 0;
            }

            return int.TryParse(value, out var count) && count > 0;
        }

        /// <summary>
        /// Creates a FileGenerationRequest from validated parsed arguments.
        /// </summary>
        private static FileGenerationRequest CreateFileGenerationRequest(ParsedArguments parsed)
        {
            var encoding = GetEncodingFromName(parsed.Encoding ?? "UTF-8");

            // Load column profile if specified
            ColumnProfile? profile = null;
            if (!string.IsNullOrEmpty(parsed.ColumnProfile))
            {
                profile = ColumnProfileLoader.Load(parsed.ColumnProfile);
                if (profile == null)
                {
                    Console.Error.WriteLine($"Warning: Failed to load column profile '{parsed.ColumnProfile}'.");
                }
            }

            // Parse multiple load file formats if specified
            List<LoadFileFormat>? multiFormats = null;
            if (!string.IsNullOrEmpty(parsed.LoadFileFormats))
            {
                multiFormats = parsed.LoadFileFormats
                    .Split(',')
                    .Select(f => GetLoadFileFormat(f.Trim()))
                    .Where(f => f.HasValue)
                    .Select(f => f!.Value)
                    .ToList();
            }

            // Parse delimiters with preset and override logic
            string columnDelim = "\u0014";  // ASCII 20 default
            string quoteDelim = "\u00fe";   // ASCII 254 default
            string newlineDelim = "\u00ae"; // ASCII 174 default

            // Apply preset if specified
            if (!string.IsNullOrEmpty(parsed.DatDelimiters))
            {
                if (parsed.DatDelimiters.Equals("csv", StringComparison.OrdinalIgnoreCase))
                {
                    columnDelim = ",";
                    quoteDelim = "\"";
                    newlineDelim = " "; // CSV typically uses space for newlines
                }

                // "standard" keeps the defaults
            }

            // Override with specific flags if provided
            if (!string.IsNullOrEmpty(parsed.DelimiterColumn))
            {
                columnDelim = ParseDelimiterArgument(parsed.DelimiterColumn);
            }

            if (!string.IsNullOrEmpty(parsed.DelimiterQuote))
            {
                quoteDelim = ParseDelimiterArgument(parsed.DelimiterQuote);
            }

            if (!string.IsNullOrEmpty(parsed.DelimiterNewline))
            {
                newlineDelim = ParseDelimiterArgument(parsed.DelimiterNewline);
            }

            // Override with strict-prefix delimiter args (--col-delim, --quote-delim, etc.)
            if (!string.IsNullOrEmpty(parsed.ColDelim))
            {
                columnDelim = ParseStrictDelimiter(parsed.ColDelim);
            }

            if (!string.IsNullOrEmpty(parsed.QuoteDelim))
            {
                quoteDelim = parsed.QuoteDelim.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : ParseStrictDelimiter(parsed.QuoteDelim);
            }

            if (!string.IsNullOrEmpty(parsed.NewlineDelim))
            {
                newlineDelim = ParseStrictDelimiter(parsed.NewlineDelim);
            }

            string multiDelim = ";";
            if (!string.IsNullOrEmpty(parsed.MultiDelim))
            {
                multiDelim = ParseStrictDelimiter(parsed.MultiDelim);
            }

            string nestedDelim = "\\";
            if (!string.IsNullOrEmpty(parsed.NestedDelim))
            {
                nestedDelim = ParseStrictDelimiter(parsed.NestedDelim);
            }

            // Handle encoding for loadfile-only mode (supports extended set)
            var encodingName = encoding?.EncodingName ?? "UTF-8";
            if (parsed.LoadfileOnly && !string.IsNullOrEmpty(parsed.Encoding))
            {
                var lfEncoding = EncodingHelper.GetEncoding(parsed.Encoding);
                if (lfEncoding != null)
                {
                    encodingName = parsed.Encoding.ToUpperInvariant();
                }
            }

            return new FileGenerationRequest
            {
                OutputPath = parsed.OutputDirectory!.FullName,
                FileCount = parsed.Count!.Value,
                FileType = (parsed.FileType ?? "pdf").ToLower(),
                Folders = parsed.Folders,
                Concurrency = PerformanceConstants.DefaultConcurrency,
                WithMetadata = parsed.WithMetadata,
                WithText = parsed.WithText,
                TargetZipSize = !string.IsNullOrEmpty(parsed.TargetZipSize) ? ParseSize(parsed.TargetZipSize!) : null,
                IncludeLoadFile = parsed.IncludeLoadFile,
                Distribution = GetDistributionFromName(parsed.Distribution ?? "proportional") ?? DistributionType.Proportional,
                Encoding = encodingName,
                AttachmentRate = parsed.AttachmentRate,
                LoadFileFormat = GetLoadFileFormat(parsed.LoadFileFormat ?? "dat") ?? LoadFileFormat.Dat,
                LoadFileFormats = multiFormats,
                ColumnDelimiter = columnDelim,
                QuoteDelimiter = quoteDelim,
                NewlineDelimiter = newlineDelim,
                MultiValueDelimiter = multiDelim,
                NestedValueDelimiter = nestedDelim,
                BatesConfig = !string.IsNullOrEmpty(parsed.BatesPrefix) ? new BatesNumberConfig
                {
                    Prefix = parsed.BatesPrefix,
                    Start = parsed.BatesStart ?? 1,
                    Digits = parsed.BatesDigits ?? 8,
                }
                : null,
                TiffPageRange = !string.IsNullOrEmpty(parsed.TiffPagesRange) ? TiffMultiPageGenerator.ParsePageRange(parsed.TiffPagesRange!) : null,
                ColumnProfile = profile,
                Seed = parsed.Seed,
                DateFormatOverride = parsed.DateFormat,
                EmptyPercentageOverride = parsed.EmptyPercentage,
                CustodianCountOverride = parsed.CustodianCount,
                WithFamilies = parsed.WithFamilies,
                LoadfileOnly = parsed.LoadfileOnly,
                EndOfLine = parsed.Eol ?? "CRLF",
                ChaosMode = parsed.ChaosMode,
                ChaosAmount = parsed.ChaosAmount,
                ChaosTypes = parsed.ChaosTypes,
            };
        }

        /// <summary>
        /// Parses size string (e.g., "500MB") into bytes.
        /// </summary>
        private static long? ParseSize(string size)
        {
            size = size.Trim();

            foreach (var (suffix, multiplier) in SizeMultipliers)
            {
                if (size.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    var numberPart = size.Substring(0, size.Length - suffix.Length);
                    return long.TryParse(numberPart, out var value) ? value * multiplier : null;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets DistributionType from string name.
        /// </summary>
        private static DistributionType? GetDistributionFromName(string name)
        {
            return name.ToUpperInvariant() switch
            {
                "PROPORTIONAL" => DistributionType.Proportional,
                "GAUSSIAN" => DistributionType.Gaussian,
                "EXPONENTIAL" => DistributionType.Exponential,
                _ => null,
            };
        }

        /// <summary>
        /// Gets Encoding from string name.
        /// </summary>
        private static Encoding? GetEncodingFromName(string name) => EncodingHelper.GetEncoding(name);

        /// <summary>
        /// Gets LoadFileFormat from string name.
        /// </summary>
        private static LoadFileFormat? GetLoadFileFormat(string name)
        {
            return name.ToUpperInvariant().Replace("-", string.Empty) switch
            {
                "DAT" => LoadFileFormat.Dat,
                "OPT" => LoadFileFormat.Opt,
                "CSV" => LoadFileFormat.Csv,
                "XML" => LoadFileFormat.Xml,
                "EDRMXML" => LoadFileFormat.EdrmXml,
                "CONCORDANCE" => LoadFileFormat.Concordance,
                _ => null,
            };
        }

        /// <summary>
        /// Parses a delimiter argument from command line.
        /// </summary>
        /// <param name="arg">Delimiter argument (single char, ASCII code, or escaped char).</param>
        /// <returns>Parsed delimiter string.</returns>
        private static string ParseDelimiterArgument(string arg)
        {
            if (string.IsNullOrEmpty(arg))
            {
                throw new ArgumentException("Delimiter argument cannot be empty.");
            }

            // Handle escaped characters - check for specific escape sequences before replacement
            if (arg == "\\t")
            {
                return "\t";
            }

            if (arg == "\\n")
            {
                return "\n";
            }

            if (arg == "\\r")
            {
                return "\r";
            }

            if (arg == "\\r\\n")
            {
                return "\r\n";
            }

            // Try parsing as ASCII decimal code
            if (int.TryParse(arg, out var asciiCode) && asciiCode >= 0 && asciiCode <= 255)
            {
                return ((char)asciiCode).ToString();
            }

            // Validate single-character input
            if (arg.Length > 1)
            {
                Console.Error.WriteLine($"Warning: Delimiter argument '{arg}' is longer than 1 character. Using first character: '{arg[0]}'");
            }

            // Use first character
            return arg[0].ToString();
        }

        /// <summary>
        /// Parses a strict-format delimiter (requires ascii: or char: prefix).
        /// </summary>
        /// <param name="arg">Delimiter argument with ascii:<N> or char:<c> prefix.</param>
        /// <returns>Parsed delimiter string.</returns>
        private static string ParseStrictDelimiter(string arg)
        {
            if (arg.StartsWith("ascii:", StringComparison.OrdinalIgnoreCase))
            {
                var numPart = arg.Substring(6);
                if (int.TryParse(numPart, out var code) && code >= 0 && code <= 255)
                {
                    return ((char)code).ToString();
                }

                throw new ArgumentException($"Invalid ASCII code in delimiter: '{arg}'");
            }

            if (arg.StartsWith("char:", StringComparison.OrdinalIgnoreCase))
            {
                var charPart = arg.Substring(5);
                if (charPart.Length >= 1)
                {
                    return charPart[0].ToString();
                }

                throw new ArgumentException($"Missing character in delimiter: '{arg}'");
            }

            throw new ArgumentException($"Delimiter must use 'ascii:<N>' or 'char:<c>' prefix: '{arg}'");
        }

        /// <summary>
        /// Internal class to hold parsed arguments before validation.
        /// </summary>
        private class ParsedArguments
        {
            public string? FileType { get; set; }

            public long? Count { get; set; }

            public DirectoryInfo? OutputDirectory { get; set; }

            public int Folders { get; set; } = 1;

            public string? Encoding { get; set; } = "UTF-8";

            public string? Distribution { get; set; } = "proportional";

            public bool WithMetadata { get; set; }

            public bool WithText { get; set; }

            public int AttachmentRate { get; set; }

            public string? TargetZipSize { get; set; }

            public bool IncludeLoadFile { get; set; }

            public string? LoadFileFormat { get; set; } = "dat";

            public string? LoadFileFormats { get; set; }

            public string? DatDelimiters { get; set; }

            public string? DelimiterColumn { get; set; }

            public string? DelimiterQuote { get; set; }

            public string? DelimiterNewline { get; set; }

            public string? BatesPrefix { get; set; }

            public long? BatesStart { get; set; }

            public int? BatesDigits { get; set; }

            public string? TiffPagesRange { get; set; }

            // Column profile arguments
            public string? ColumnProfile { get; set; }

            public int? Seed { get; set; }

            public string? DateFormat { get; set; }

            public int? EmptyPercentage { get; set; }

            public int? CustodianCount { get; set; }

            public bool WithFamilies { get; set; }

            // Loadfile-only arguments
            public bool LoadfileOnly { get; set; }

            public string? Eol { get; set; }

            public string? ColDelim { get; set; }

            public string? QuoteDelim { get; set; }

            public string? NewlineDelim { get; set; }

            public string? MultiDelim { get; set; }

            public string? NestedDelim { get; set; }

            // Chaos Engine arguments
            public bool ChaosMode { get; set; }

            public string? ChaosAmount { get; set; }

            public string? ChaosTypes { get; set; }
        }
    }
}
