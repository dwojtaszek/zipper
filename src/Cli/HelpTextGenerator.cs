using System.Diagnostics;

namespace Zipper.Cli;

internal static class HelpTextGenerator
{
    public static void Show()
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
        Console.Error.WriteLine("  --chaos-scenario <name>  Use a predefined chaos scenario (conflicts with --chaos-types)");
        Console.Error.WriteLine("  --chaos-list             List available chaos scenarios and exit");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Production Set Options:");
        Console.Error.WriteLine("  --production-set         Generate structured production with DATA/IMAGES/NATIVES/TEXT");
        Console.Error.WriteLine("  --production-zip         Wrap production set output in a ZIP archive");
        Console.Error.WriteLine("  --volume-size <number>   Max files per volume subfolder (default: 5000)");
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
        Console.Error.WriteLine();
        Console.Error.WriteLine("Utility Options:");
        Console.Error.WriteLine("  --benchmark              Run performance benchmark suite and exit");
    }
}
