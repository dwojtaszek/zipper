using System.Diagnostics;
using Zipper.ArchiveTests;

namespace Zipper.Cli;

internal static class HelpTextGenerator
{
    public static void Show()
    {
        var exeName = Process.GetCurrentProcess().ProcessName;
        // CLI help text is display-only output, not deterministic Load File content.
#pragma warning disable MA0136
        var help = $"""
            Error: Missing required arguments.
            Usage: {exeName} --type <pdf|jpg|tiff|eml|docx|xlsx> --count <number> --output-path <directory> [options]
                   {exeName} (--types <type:weight,...> | --input-csv <path> | --directory-template <path>) --output-path <directory> [options]

            Required Arguments:
              --type <string>          File type: pdf, jpg, tiff, eml, docx, xlsx
              --count <number>         Number of files to generate
              --output-path <path>     Output directory path

            Optional Arguments:
              --types <list>           File Type Mix alternative to --type (e.g., "pdf:50,eml:30,tiff:20")
              --folders <number>       Number of folders (1-100, default: 1)
              --encoding <string>      Encoding: {EncodingHelper.SupportedNames} (default: UTF-8, OPT: ANSI when omitted)
              --distribution <string>  Distribution: proportional, gaussian, exponential
              --with-metadata          Include metadata columns in load file
              --with-collection-metadata Include collection metadata columns in load file
              --with-text              Generate extracted text files
              --attachment-rate <n>    EML attachment percentage (0-100, default: 0)
              --target-zip-size <size> Target ZIP size (e.g., 500MB, 10GB)
              --include-load-file      Include load file in ZIP archive
              --compression <method>   ZIP compression method: store, deflate (default: deflate; deflate64, bzip2 not yet supported)

            Source-Driven Generation Options:
              --input-csv <path>       Drive records from a Source CSV (FilePath and FileType columns required;
                                       ControlNumber, BatesNumber (requires --bates-prefix), and extra
                                       Source Metadata columns optional)
              --directory-template <path>
                                       Mirror a directory structure; File Types inferred from extensions
                                       (source bytes are never copied; placeholder content is generated)

            Load File Options:
              --load-file-format <fmt> Load file format: dat, opt, csv, edrm-xml, xml, concordance (default: dat)
              --load-file-formats <f>  Multiple formats comma-separated (e.g., dat,opt,csv)
              --dat-delimiters <type>  DAT delimiter style: standard, csv (default: standard)
              --delimiter-column <c>   Custom column delimiter (char or ASCII code)
              --delimiter-quote <c>    Custom quote delimiter (char or ASCII code)
              --delimiter-newline <c>  Custom newline replacement (char or ASCII code)
              --eol <CRLF|LF|CR>      Line Ending for Loadfile-Only or Production Set (default: CRLF)
              --hash-mode <mode>       Cryptographic hash mode: actual, simulated, none (default: none)
              --hash-algorithms <list> Hash algorithms: md5, sha1, sha256 (default: md5 when hashes are enabled)

            Loadfile-Only Options:
              --loadfile-only          Skip ZIP/native generation, stream directly to load file
              --loadfile-format <f>    Load file schema: dat, opt, csv, edrm-xml, xml, concordance (default: dat)
              --col-delim <value>      Column delimiter (ascii:<N> or char:<c>)
              --quote-delim <value>    Quote delimiter (ascii:<N>, char:<c>, or none)
              --newline-delim <value>  In-cell newline replacement (ascii:<N> or char:<c>)
              --multi-delim <value>    Multi-value delimiter (ascii:<N> or char:<c>)
              --nested-delim <value>   Nested-value delimiter (ascii:<N> or char:<c>)

            Chaos Engine Options:
              --chaos-mode             Enable Chaos Engine processing
              --chaos-amount <value>   Target count: percentage (1%) or exact (500)
              --chaos-types <list>     DAT: mixed-delimiters,quotes,columns,eol,encoding,path-pointer,field-length,date-format,null-byte,trailing-empties
                                       OPT: opt-boundary,opt-columns,opt-pagecount,opt-path,opt-batesnumber,opt-encoding
                                       CSV: csv-quote-escape,csv-embedded-newline,csv-bom
                                       Concordance: concordance-delimiter,concordance-quote-escape
                                       EDRM-XML: xml-unclosed-tag,xml-bad-entity,xml-encoding-lie
                                       EML (--type eml): mime-boundary,encoded-word,header-fold,rf822-depth
                                       TIFF (--type tiff): tiff-dimension-lie,tiff-truncated-ifd,tiff-tag-count-lie,tiff-strip-offset
                                       DAT record anomalies are opt-in; full-chaos enables all types for the selected format
                                       OPT opt-encoding is opt-in; full-chaos enables it
              --chaos-scenario <name>  Predefined scenario; conflicts with --chaos-types; adversarial-values emits valid DAT values
              --chaos-list             List available chaos scenarios and exit

            Production Set Options:
              --production-set         Generate structured production with DATA/IMAGES/NATIVES/TEXT
              --production-id <string> Configurable production ID (requires --production-set; non-empty; supports lists, defaults to auto-incrementing/timestamp), max 250 UTF-8 bytes per generated element, single safe path segment per element
              --rolling-count <number> Generate multiple rolling production sets (default: 1)
              --rolling-bates-mode <continuous|restart> Bates numbering mode across rolling sets (default: continuous)
              --source-path-mode <bates|preserve|originals> Source Path Mode for Source-Driven Production Sets (default: bates)
              --production-zip         Wrap production set output in a ZIP archive
              --volume-size <number>   Max files per volume subfolder (default: 5000)
              --redacted-production    Generate redacted image/text placeholders
              --withheld-native-policy <keep-native|omit-native-path|replace-with-placeholder> Native path policy for redacted productions (default: keep-native)
              --supplemental-production Enable supplemental production set generation mode
              --prior-manifest <paths>  Comma-separated list of paths to prior production manifest files
              --supplemental-gap-policy <reject|allow> Gap policy for supplemental mode: reject or allow (default: reject)

            Production Manifest Comparison Options:
              --compare-production-manifests <paths> Compare two or more production manifests (bypasses file generation)
              --comparison-mode <replacement|supplemental|reproduction> Comparison ruleset mode (required with --compare-production-manifests)
              --comparison-output <path> Output path for the comparison JSON report (required with --compare-production-manifests)

            Bates Numbering:
              --bates-prefix <string>  Bates Number prefix, max 200 UTF-8 bytes per list element (e.g., CLIENT001)
              --bates-start <number>   Bates start number (default: 1)
              --bates-digits <number>  Bates digit count (default: 8)
              --bates-page-suffix      Page-level Bates numbers (<prefix><number>_NNN) for every document, including one-page

            TIFF Options:
              --tiff-pages <min-max>   TIFF page range (e.g., 1-20, default: 1-1)
              --tiff-realism <mode>    Enable realistic TIFF emission (standard, group4, uncompressed, none)
              --with-tiff-realism      Enable standard realistic TIFF emission with Bates endorsement band (Group 4 unless --tiff-compression uncompressed)
              --realistic-tiff         Alias for --with-tiff-realism
              --tiff-compression <cmp> Compression scheme for realistic TIFFs (group4, uncompressed; requires realism)
              --tiff-byte-order <ii|mm> Byte order for realistic TIFFs: ii (little-endian, default) or mm (big-endian); requires realism

            Column Profile Options:
              --column-profile <name>  Built-in profile: minimal, standard, litigation, full
                                       Or path to custom JSON profile file (within working directory)
              --seed <number>          Random seed for reproducible output
              --date-format <fmt>      Override date format (e.g., yyyy-MM-dd)
              --empty-percentage <n>   Override empty value percentage (0-100)
              --custodian-count <n>    Override custodian count (max: 1000)
              --with-families          Generate parent-child document relationships
              --with-threading         Add RFC 5322 Message-ID, In-Reply-To, and References headers plus thread metadata Columns
              --with-email-threading   Alias for --with-threading
              --mime-realism <level>   Enable MIME realism emission modes (standard, full, or comma-separated tokens)
              --email-mime-realism <level> Alias for --mime-realism
              --with-mime-realism      Enable standard MIME realism

            Archive Test Options:
              --archive-test-suite <s>  Generate Archive Test Fixture pairs for one suite:
                                       {string.Join(", ", ArchiveTestCatalog.SupportedSuites)}
              --archive-test-cases <k>  Comma-separated Case Keys from the selected suite
                                       (default: all members of the suite)
              --seed <number>           Fixture Seed (non-negative integer, default: 42)
              --output-path <path>      New output directory for the fixture pairs (required)
                                       Only these flags may be combined with --archive-test-suite.

            Utility Options:
              --benchmark              Run performance benchmark suite and exit
              {PerformanceBenchmarkRunner.ZipWorkloadUsage}
                                       Generate a deterministic benchmark ZIP and exit
              --version                Print the version string and exit
            """;
#pragma warning restore MA0136
        Console.Error.WriteLine(help.ReplaceLineEndings(Console.Error.NewLine));
    }
}
