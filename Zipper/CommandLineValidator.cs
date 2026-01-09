using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

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
            ["GB"] = 1024 * 1024 * 1024
        };

        /// <summary>
        /// Validates and parses command line arguments into a FileGenerationRequest.
        /// </summary>
        /// <param name="args">Command line arguments</param>
        /// <returns>Validated FileGenerationRequest, or null if validation fails</returns>
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
            Console.Error.WriteLine($"Usage: {exeName} --type <pdf|jpg|tiff|eml> --count <number> --output-path <directory> [--folders <number>] [--encoding <UTF-8|UTF-16|ANSI>] [--distribution <proportional|gaussian|exponential>] [--attachment-rate <percentage>] [--target-zip-size <size>] [--include-load-file]");
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
                        if (i + 1 < args.Length) parsed.FileType = args[++i];
                        break;
                    case "--count":
                        if (i + 1 < args.Length && long.TryParse(args[++i], out var count)) parsed.Count = count;
                        break;
                    case "--output-path":
                        if (i + 1 < args.Length)
                        {
                            string pathArg = args[++i];
                            parsed.OutputDirectory = PathValidator.ValidateAndCreateDirectory(pathArg);
                        }
                        break;
                    case "--folders":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var folders)) parsed.Folders = folders;
                        break;
                    case "--encoding":
                        if (i + 1 < args.Length) parsed.Encoding = args[++i];
                        break;
                    case "--distribution":
                        if (i + 1 < args.Length) parsed.Distribution = args[++i];
                        break;
                    case "--with-metadata":
                        parsed.WithMetadata = true;
                        break;
                    case "--with-text":
                        parsed.WithText = true;
                        break;
                    case "--attachment-rate":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var attachmentRate)) parsed.AttachmentRate = attachmentRate;
                        break;
                    case "--target-zip-size":
                        if (i + 1 < args.Length) parsed.TargetZipSize = args[++i];
                        break;
                    case "--include-load-file":
                        parsed.IncludeLoadFile = true;
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

            return true;
        }

        /// <summary>
        /// Creates a FileGenerationRequest from validated parsed arguments.
        /// </summary>
        private static FileGenerationRequest CreateFileGenerationRequest(ParsedArguments parsed)
        {
            var encoding = GetEncodingFromName(parsed.Encoding ?? "UTF-8");
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
                AttachmentRate = parsed.AttachmentRate
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
                _ => null
            };
        }

        /// <summary>
        /// Gets Encoding from string name.
        /// </summary>
        private static Encoding? GetEncodingFromName(string name) => EncodingHelper.GetEncoding(name);

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
        }
    }
}