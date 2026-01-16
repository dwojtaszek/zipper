// <copyright file="CommandLineValidator.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

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
        private static ParsedArguments ParseArguments(string[] args)
        {
            var parsed = new ParsedArguments();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--type":
                        if (i + 1 < args.Length)
                        {
                            parsed.FileType = args[++i];
                        }

                        break;
                    case "--count":
                        if (i + 1 < args.Length && long.TryParse(args[++i], out var count))
                        {
                            parsed.Count = count;
                        }

                        break;
                    case "--output-path":
                        if (i + 1 < args.Length)
                        {
                            string pathArg = args[++i];
                            parsed.OutputDirectory = PathValidator.ValidateAndCreateDirectory(pathArg);
                        }

                        break;
                    case "--folders":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var folders))
                        {
                            parsed.Folders = folders;
                        }

                        break;
                    case "--encoding":
                        if (i + 1 < args.Length)
                        {
                            parsed.Encoding = args[++i];
                        }

                        break;
                    case "--distribution":
                        if (i + 1 < args.Length)
                        {
                            parsed.Distribution = args[++i];
                        }

                        break;
                    case "--with-metadata":
                        parsed.WithMetadata = true;
                        break;
                    case "--with-text":
                        parsed.WithText = true;
                        break;
                    case "--attachment-rate":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var attachmentRate))
                        {
                            parsed.AttachmentRate = attachmentRate;
                        }

                        break;
                    case "--target-zip-size":
                        if (i + 1 < args.Length)
                        {
                            parsed.TargetZipSize = args[++i];
                        }

                        break;
                    case "--include-load-file":
                        parsed.IncludeLoadFile = true;
                        break;
                    case "--load-file-format":
                        if (i + 1 < args.Length)
                        {
                            parsed.LoadFileFormat = args[++i];
                        }

                        break;
                    case "--bates-prefix":
                        if (i + 1 < args.Length)
                        {
                            parsed.BatesPrefix = args[++i];
                        }

                        break;
                    case "--bates-start":
                        if (i + 1 < args.Length && long.TryParse(args[++i], out var batesStart))
                        {
                            parsed.BatesStart = batesStart;
                        }

                        break;
                    case "--bates-digits":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var batesDigits))
                        {
                            parsed.BatesDigits = batesDigits;
                        }

                        break;
                    case "--tiff-pages":
                        if (i + 1 < args.Length)
                        {
                            parsed.TiffPagesRange = args[++i];
                        }

                        break;

                    // New column profile arguments
                    case "--column-profile":
                        if (i + 1 < args.Length)
                        {
                            parsed.ColumnProfile = args[++i];
                        }

                        break;
                    case "--seed":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var seed))
                        {
                            parsed.Seed = seed;
                        }

                        break;
                    case "--date-format":
                        if (i + 1 < args.Length)
                        {
                            parsed.DateFormat = args[++i];
                        }

                        break;
                    case "--empty-percentage":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var emptyPct))
                        {
                            parsed.EmptyPercentage = emptyPct;
                        }

                        break;
                    case "--custodian-count":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var custCount))
                        {
                            parsed.CustodianCount = custCount;
                        }

                        break;
                    case "--with-families":
                        parsed.WithFamilies = true;
                        break;
                    case "--load-file-formats":
                        if (i + 1 < args.Length)
                        {
                            parsed.LoadFileFormats = args[++i];
                        }

                        break;
                    case "--dat-delimiters":
                        if (i + 1 < args.Length)
                        {
                            parsed.DatDelimiters = args[++i];
                        }

                        break;
                    case "--delimiter-column":
                        if (i + 1 < args.Length)
                        {
                            parsed.DelimiterColumn = args[++i];
                        }

                        break;
                    case "--delimiter-quote":
                        if (i + 1 < args.Length)
                        {
                            parsed.DelimiterQuote = args[++i];
                        }

                        break;
                    case "--delimiter-newline":
                        if (i + 1 < args.Length)
                        {
                            parsed.DelimiterNewline = args[++i];
                        }

                        break;
                }
            }

            return parsed;
        }

        /// <summary>
        /// Validates required arguments.
        /// </summary>
        private static bool ValidateRequiredArguments(ParsedArguments parsed)
        {
            if (string.IsNullOrEmpty(parsed.FileType))
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

            return true;
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

            return new FileGenerationRequest
            {
                OutputPath = parsed.OutputDirectory!.FullName,
                FileCount = parsed.Count!.Value,
                FileType = parsed.FileType!.ToLower(),
                Folders = parsed.Folders,
                Concurrency = PerformanceConstants.DefaultConcurrency,
                WithMetadata = parsed.WithMetadata,
                WithText = parsed.WithText,
                TargetZipSize = !string.IsNullOrEmpty(parsed.TargetZipSize) ? ParseSize(parsed.TargetZipSize!) : null,
                IncludeLoadFile = parsed.IncludeLoadFile,
                Distribution = GetDistributionFromName(parsed.Distribution ?? "proportional") ?? DistributionType.Proportional,
                Encoding = encoding?.EncodingName ?? "UTF-8",
                AttachmentRate = parsed.AttachmentRate,
                LoadFileFormat = GetLoadFileFormat(parsed.LoadFileFormat ?? "dat") ?? LoadFileFormat.Dat,
                LoadFileFormats = multiFormats,
                ColumnDelimiter = columnDelim,
                QuoteDelimiter = quoteDelim,
                NewlineDelimiter = newlineDelim,
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

            // Handle escaped characters
            arg = arg.Replace("\\t", "\t")
                     .Replace("\\n", "\n")
                     .Replace("\\r", "\r");

            // Try parsing as ASCII decimal code
            if (int.TryParse(arg, out var asciiCode) && asciiCode >= 0 && asciiCode <= 255)
            {
                return ((char)asciiCode).ToString();
            }

            // Use as-is (single character or already escaped)
            return arg.Length > 0 ? arg.Substring(0, 1) : arg;
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
        }
    }
}
