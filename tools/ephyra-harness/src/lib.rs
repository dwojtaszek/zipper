use ephyra::{Gen, GenExt, gens};
use std::collections::BTreeSet;
use std::io::{Read, Seek};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

/// Resolves the pre-built Release Zipper binary relative to CARGO_MANIFEST_DIR.
pub fn zipper_bin() -> PathBuf {
    if let Ok(p) = std::env::var("ZIPPER_BIN") {
        return PathBuf::from(p);
    }
    let manifest = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    let release = manifest
        .parent()
        .and_then(|p| p.parent())
        .expect("tools/ephyra-harness inside repo root")
        .join("src/bin/Release/net10.0")
        .join(format!("Zipper{}", std::env::consts::EXE_SUFFIX));
    assert!(
        release.exists(),
        "Zipper binary not found at {}; build it first with `dotnet build src/Zipper.csproj -c Release`",
        release.display()
    );
    release
}

#[derive(Debug)]
pub struct CliOutput {
    pub code: Option<i32>,
    pub stdout: String,
    pub stderr: String,
    pub timed_out: bool,
}

/// Runs Zipper in `work_dir` with `args` and a hard timeout.
pub fn run_zipper(work_dir: &Path, args: &[String]) -> CliOutput {
    // File-backed capture lets the child keep writing while the timeout is polled.
    let mut stdout = tempfile::tempfile().expect("create stdout capture");
    let mut stderr = tempfile::tempfile().expect("create stderr capture");
    let mut child = Command::new(zipper_bin())
        .args(args)
        .current_dir(work_dir)
        .stdout(Stdio::from(
            stdout.try_clone().expect("clone stdout capture"),
        ))
        .stderr(Stdio::from(
            stderr.try_clone().expect("clone stderr capture"),
        ))
        .spawn()
        .expect("spawn Zipper binary");
    let timeout = Duration::from_secs(15);
    let start = Instant::now();
    let (status, timed_out) = loop {
        if let Some(status) = child.try_wait().expect("try_wait Zipper") {
            break (status, false);
        }
        if start.elapsed() >= timeout {
            if let Err(error) = child.kill() {
                let status = child
                    .try_wait()
                    .expect("poll Zipper after termination error");
                break (
                    status.unwrap_or_else(|| panic!("Could not terminate Zipper: {error}")),
                    true,
                );
            }
            break (child.wait().expect("wait for Zipper after timeout"), true);
        }
        std::thread::sleep(Duration::from_millis(10));
    };
    let read_capture = |file: &mut std::fs::File| {
        let mut bytes = Vec::new();
        file.rewind().expect("rewind CLI capture");
        file.read_to_end(&mut bytes).expect("read CLI capture");
        String::from_utf8_lossy(&bytes).into_owned()
    };
    CliOutput {
        code: status.code(),
        stdout: read_capture(&mut stdout),
        stderr: read_capture(&mut stderr),
        timed_out,
    }
}

pub fn assert_no_crash(out: &CliOutput, context: &str) {
    assert!(!out.timed_out, "Zipper timed out: {context}");
    assert!(
        !out.stderr.contains("Unhandled exception"),
        "Unhandled exception in Zipper ({context}):\n{}",
        out.stderr
    );
    assert!(
        out.code == Some(0) || out.code == Some(1),
        "Unexpected exit code {:?} ({context}):\nstderr:\n{}\nstdout:\n{}",
        out.code,
        out.stderr,
        out.stdout
    );
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, ephyra::Arbitrary)]
pub enum LoadFormat {
    Dat,
    Opt,
    Csv,
    Concordance,
    EdrmXml,
}

impl LoadFormat {
    pub fn flag_value(self) -> &'static str {
        match self {
            LoadFormat::Dat => "dat",
            LoadFormat::Opt => "opt",
            LoadFormat::Csv => "csv",
            LoadFormat::Concordance => "concordance",
            LoadFormat::EdrmXml => "edrm-xml",
        }
    }

    pub fn ext(self) -> &'static str {
        match self {
            LoadFormat::Dat | LoadFormat::Concordance => "dat",
            LoadFormat::Opt => "opt",
            LoadFormat::Csv => "csv",
            LoadFormat::EdrmXml => "xml",
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, ephyra::Arbitrary)]
pub enum EolKind {
    Crlf,
    Lf,
    Cr,
}

impl EolKind {
    pub fn flag_value(self) -> &'static str {
        match self {
            EolKind::Crlf => "CRLF",
            EolKind::Lf => "LF",
            EolKind::Cr => "CR",
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, ephyra::Arbitrary)]
pub enum EncodingKind {
    Utf8,
    Utf16,
    Ansi,
}

impl EncodingKind {
    pub fn flag_value(self) -> &'static str {
        match self {
            EncodingKind::Utf8 => "UTF-8",
            EncodingKind::Utf16 => "UTF-16",
            EncodingKind::Ansi => "ANSI",
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, ephyra::Arbitrary)]
pub enum DocType {
    Pdf,
    Jpg,
    Tiff,
    Eml,
    Docx,
    Xlsx,
}

impl DocType {
    pub fn flag_value(self) -> &'static str {
        match self {
            DocType::Pdf => "pdf",
            DocType::Jpg => "jpg",
            DocType::Tiff => "tiff",
            DocType::Eml => "eml",
            DocType::Docx => "docx",
            DocType::Xlsx => "xlsx",
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, ephyra::Arbitrary)]
pub enum QuoteStyle {
    DefaultThorn,
    DoubleQuote,
    SingleQuote,
    None,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, ephyra::Arbitrary)]
pub enum ColStyle {
    DefaultDc4,
    Comma,
    Pipe,
    Tab,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, ephyra::Arbitrary)]
pub enum ProfileKind {
    None,
    WithMetadata,
    Minimal,
    Standard,
    Litigation,
    Full,
}

#[derive(Clone, Debug, PartialEq, Eq, ephyra::Arbitrary)]
pub struct LoadfileCase {
    pub format: LoadFormat,
    pub doc_type: DocType,
    #[ephyra(gen = gens::int(1u8..=15))]
    pub count: u8,
    pub eol: EolKind,
    pub encoding: EncodingKind,
    pub col_style: ColStyle,
    pub quote_style: QuoteStyle,
    pub profile: ProfileKind,
    pub with_collection_metadata: bool,
    pub with_families: bool,
    pub with_threading: bool,
    #[ephyra(gen = gens::int(0u8..=100))]
    pub attachment_rate: u8,
    pub simulated_hash: bool,
    #[ephyra(gen = gens::int(0u32..=10_000))]
    pub seed: u32,
}

impl LoadfileCase {
    pub fn col_char(&self) -> char {
        match self.col_style {
            ColStyle::DefaultDc4 => '\x14',
            ColStyle::Comma => ',',
            ColStyle::Pipe => '|',
            ColStyle::Tab => '\t',
        }
    }

    pub fn quote_char(&self) -> Option<char> {
        match self.quote_style {
            QuoteStyle::DefaultThorn => Some('\u{fe}'),
            QuoteStyle::DoubleQuote => Some('"'),
            QuoteStyle::SingleQuote => Some('\''),
            QuoteStyle::None => None,
        }
    }
}

#[ephyra::generator]
pub fn valid_loadfile_case(c: &LoadfileCase) -> bool {
    c.count >= 1
        && c.count <= 15
        && c.attachment_rate <= 100
        && Some(c.col_char()) != c.quote_char()
}

#[ephyra::generator(valid_loadfile_case, metrics = loadfile_case_metrics)]
pub fn loadfile_cases() -> impl Gen<Value = LoadfileCase> {
    ephyra::any::<LoadfileCase>().filter("col_char != quote_char", valid_loadfile_case)
}

pub fn loadfile_case_metrics(c: &LoadfileCase, _size: usize) -> Vec<(&'static str, f64)> {
    // count: requested document count; is_none_quote: 1.0 when --quote-delim none; is_utf16: 1.0 for UTF-16.
    vec![
        ("count", f64::from(c.count)),
        (
            "is_none_quote",
            if c.quote_style == QuoteStyle::None {
                1.0
            } else {
                0.0
            },
        ),
        (
            "is_utf16",
            if c.encoding == EncodingKind::Utf16 {
                1.0
            } else {
                0.0
            },
        ),
    ]
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, ephyra::Arbitrary)]
pub enum ChaosModeCase {
    RealisticImportFailures,
    CsvEscapingFailures,
    ConcordanceImportFailures,
    EdrmXmlStructuralFailures,
    AdversarialValues { count: u8, comma_delim: bool },
    CrossFormatDesync,
    BrokenFamilies,
    EmailMimeChaos,
    TiffChaos { big_endian: bool },
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ChaosCase {
    pub mode: ChaosModeCase,
    pub count: u8,
    pub amount_pct: u8,
    pub seed: u32,
}

#[ephyra::generator]
pub fn valid_chaos_case(c: &ChaosCase) -> bool {
    c.count >= 1 && c.count <= 12 && c.amount_pct >= 1 && c.amount_pct <= 100
}

#[ephyra::generator(valid_chaos_case, metrics = chaos_case_metrics)]
pub fn chaos_cases() -> impl Gen<Value = ChaosCase> {
    (
        ephyra::any::<ChaosModeCase>(),
        gens::int(1u8..=12),
        gens::int(1u8..=100),
        gens::int(0u32..=10_000),
    )
        .iso(
            |(mode, count, amount_pct, seed)| ChaosCase {
                mode,
                count,
                amount_pct,
                seed,
            },
            |c: &ChaosCase| (c.mode, c.count, c.amount_pct, c.seed),
        )
}

pub fn chaos_case_metrics(c: &ChaosCase, _size: usize) -> Vec<(&'static str, f64)> {
    // count: requested document count; amount_pct: percentage passed to --chaos-amount.
    vec![
        ("count", f64::from(c.count)),
        ("amount_pct", f64::from(c.amount_pct)),
    ]
}

#[derive(Clone, Debug, PartialEq, Eq, ephyra::Arbitrary)]
pub struct ProductionCase {
    pub doc_type: DocType,
    #[ephyra(gen = gens::int(1u8..=12))]
    pub count: u8,
    #[ephyra(gen = gens::int(1u8..=15))]
    pub volume_size: u8,
    #[ephyra(gen = gens::int(1u8..=3))]
    pub rolling_count: u8,
    pub restart_bates: bool,
    #[ephyra(gen = gens::int(0u32..=50_000))]
    pub bates_start: u32,
    #[ephyra(gen = gens::int(1u8..=12))]
    pub bates_digits: u8,
    pub page_suffix: bool,
    pub redacted: bool,
    pub production_zip: bool,
    pub with_families: bool,
    pub with_text: bool,
    #[ephyra(gen = gens::int(0u8..=100))]
    pub attachment_rate: u8,
    pub tiff_big_endian: bool,
    #[ephyra(gen = gens::int(0u32..=10_000))]
    pub seed: u32,
}

#[ephyra::generator]
pub fn valid_production_case(c: &ProductionCase) -> bool {
    c.count >= 1
        && c.count <= 12
        && c.volume_size >= 1
        && c.volume_size <= 15
        && c.rolling_count >= 1
        && c.rolling_count <= 3
        && c.bates_digits >= 1
        && c.bates_digits <= 12
        && c.attachment_rate <= 100
}

#[ephyra::generator(valid_production_case, metrics = production_case_metrics)]
pub fn production_cases() -> impl Gen<Value = ProductionCase> {
    ephyra::any::<ProductionCase>()
}

pub fn production_case_metrics(c: &ProductionCase, _size: usize) -> Vec<(&'static str, f64)> {
    vec![
        ("count", f64::from(c.count)),
        ("volume_size", f64::from(c.volume_size)),
        ("rolling_count", f64::from(c.rolling_count)),
    ]
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SourceCsvRow {
    pub path: String,
    pub doc_type: DocType,
    pub custodian: String,
    pub note: String,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SourceCsvCase {
    pub rows: Vec<SourceCsvRow>,
    pub format: LoadFormat,
    pub production_set: bool,
    pub loadfile_only: bool,
    pub seed: u32,
}

#[ephyra::generator]
pub fn valid_source_csv_case(c: &SourceCsvCase) -> bool {
    if c.rows.is_empty() || c.rows.len() > 8 {
        return false;
    }
    if c.production_set && c.loadfile_only {
        return false;
    }
    let mut seen = BTreeSet::new();
    for r in &c.rows {
        if r.path.is_empty() || !seen.insert(&r.path) {
            return false;
        }
    }
    true
}

fn safe_token_gen() -> impl Gen<Value = String> {
    gens::string_len(gens::alphanumeric(), 1..=10)
}

fn csv_note_gen() -> impl Gen<Value = String> {
    gens::one_of(vec![
        gens::constant(String::new()).boxed(),
        gens::constant("simple note".to_string()).boxed(),
        gens::constant("comma, inside".to_string()).boxed(),
        gens::constant("quote \"here\"".to_string()).boxed(),
        gens::constant("line1\nline2".to_string()).boxed(),
        gens::string_len(gens::alphanumeric(), 0..=16).boxed(),
    ])
}

#[ephyra::generator(valid_source_csv_case, metrics = source_csv_case_metrics)]
pub fn source_csv_cases() -> impl Gen<Value = SourceCsvCase> {
    let row_gen = (
        safe_token_gen(),
        ephyra::any::<DocType>(),
        safe_token_gen(),
        csv_note_gen(),
    )
        .iso(
            |(stem, doc_type, custodian, note)| SourceCsvRow {
                path: format!("docs/{stem}.{}", doc_type.flag_value()),
                doc_type,
                custodian,
                note,
            },
            |r: &SourceCsvRow| {
                let stem = r
                    .path
                    .strip_prefix("docs/")
                    .and_then(|s| s.split('.').next())
                    .unwrap_or("doc")
                    .to_string();
                (stem, r.doc_type, r.custodian.clone(), r.note.clone())
            },
        );

    (
        gens::vec(row_gen).len(1..=8),
        ephyra::any::<LoadFormat>(),
        gens::bool(),
        gens::bool(),
        gens::int(0u32..=10_000),
    )
        .iso(
            |(rows, format, production_set, loadfile_only, seed)| SourceCsvCase {
                rows,
                format,
                production_set,
                loadfile_only: if production_set { false } else { loadfile_only },
                seed,
            },
            |c: &SourceCsvCase| {
                (
                    c.rows.clone(),
                    c.format,
                    c.production_set,
                    c.loadfile_only,
                    c.seed,
                )
            },
        )
        .filter(
            "unique non-empty paths and valid mode",
            valid_source_csv_case,
        )
}

pub fn source_csv_case_metrics(c: &SourceCsvCase, _size: usize) -> Vec<(&'static str, f64)> {
    vec![("rows", c.rows.len() as f64)]
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ArchiveCase {
    pub doc_type: DocType,
    pub count: u8,
    pub folders: u8,
    pub deflate: bool,
    pub include_load_file: bool,
    pub with_text: bool,
    pub seed: u32,
}

#[ephyra::generator]
pub fn valid_archive_case(c: &ArchiveCase) -> bool {
    c.count >= 1 && c.count <= 15 && c.folders >= 1 && c.folders <= 10
}

#[ephyra::generator(valid_archive_case, metrics = archive_case_metrics)]
pub fn archive_cases() -> impl Gen<Value = ArchiveCase> {
    (
        ephyra::any::<DocType>(),
        gens::int(1u8..=15),
        gens::int(1u8..=10),
        gens::bool(),
        gens::bool(),
        gens::bool(),
        gens::int(0u32..=10_000),
    )
        .iso(
            |(doc_type, count, folders, deflate, include_load_file, with_text, seed)| ArchiveCase {
                doc_type,
                count,
                folders,
                deflate,
                include_load_file,
                with_text,
                seed,
            },
            |c: &ArchiveCase| {
                (
                    c.doc_type,
                    c.count,
                    c.folders,
                    c.deflate,
                    c.include_load_file,
                    c.with_text,
                    c.seed,
                )
            },
        )
}

pub fn archive_case_metrics(c: &ArchiveCase, _size: usize) -> Vec<(&'static str, f64)> {
    vec![
        ("count", f64::from(c.count)),
        ("folders", f64::from(c.folders)),
    ]
}

#[derive(Clone, Debug, PartialEq, Eq, ephyra::Arbitrary)]
pub enum CliFuzzOp {
    Type(DocType),
    Types(String),
    Count(
        #[ephyra(gen = gens::one_of(vec![
        gens::constant(-1i64).boxed(),
        gens::constant(0i64).boxed(),
        gens::constant(1i64).boxed(),
        gens::constant(5i64).boxed(),
        gens::constant(i64::MAX).boxed(),
        gens::constant(i64::MIN).boxed(),
    ]))]
        i64,
    ),
    LoadfileOnly,
    LoadFileFormat(LoadFormat),
    LoadFileFormats(Vec<LoadFormat>),
    DatDelimitersCsv,
    ColDelim(String),
    QuoteDelim(String),
    NewlineDelim(String),
    Eol(EolKind),
    Encoding(EncodingKind),
    ProductionSet,
    ProductionZip,
    BatesPrefix(String),
    BatesStart(i64),
    BatesDigits(i32),
    BatesPageSuffix,
    VolumeSize(i32),
    RollingCount(i32),
    RollingRestart,
    RedactedProduction,
    WithheldPolicy(u8),
    WithMetadata,
    WithCollectionMetadata,
    WithFamilies,
    WithThreading,
    AttachmentRate(i32),
    ChaosMode,
    ChaosAmount(String),
    ChaosScenario(String),
    ChaosTypes(String),
    TiffPages(String),
    TiffRealism(String),
    TiffByteOrder(String),
    WithText,
    HashMode(String),
    HashAlgorithms(String),
    ArchiveSuite(String),
    DateFormat(String),
    EmptyPct(i32),
    CustodianCount(i32),
    Seed(i64),
}

fn fuzz_flag_string_gen() -> impl Gen<Value = String> {
    gens::one_of(vec![
        gens::constant(String::new()).boxed(),
        gens::constant("none".to_string()).boxed(),
        gens::constant("ascii:0".to_string()).boxed(),
        gens::constant("ascii:10".to_string()).boxed(),
        gens::constant("ascii:20".to_string()).boxed(),
        gens::constant("ascii:254".to_string()).boxed(),
        gens::constant("ascii:999".to_string()).boxed(),
        gens::constant("char:,".to_string()).boxed(),
        gens::constant("char:|".to_string()).boxed(),
        gens::constant("char:".to_string()).boxed(),
        gens::constant("pdf:50,eml:50".to_string()).boxed(),
        gens::constant("pdf:0,eml:0".to_string()).boxed(),
        gens::constant("1-3".to_string()).boxed(),
        gens::constant("5-1".to_string()).boxed(),
        gens::constant("0-0".to_string()).boxed(),
        gens::constant("mm".to_string()).boxed(),
        gens::constant("ii".to_string()).boxed(),
        gens::constant("standard".to_string()).boxed(),
        gens::constant("group4".to_string()).boxed(),
        gens::constant("smoke".to_string()).boxed(),
        gens::constant("malformed".to_string()).boxed(),
        gens::constant("adversarial-values".to_string()).boxed(),
        gens::constant("realistic-import-failures".to_string()).boxed(),
        gens::constant("1%".to_string()).boxed(),
        gens::constant("0".to_string()).boxed(),
        gens::constant("-5".to_string()).boxed(),
        gens::constant("A,B".to_string()).boxed(),
        gens::constant("../escape".to_string()).boxed(),
        gens::constant("yyyy-MM-dd".to_string()).boxed(),
        gens::constant("bad{fmt".to_string()).boxed(),
        gens::constant("simulated".to_string()).boxed(),
        gens::constant("actual".to_string()).boxed(),
        gens::constant("md5,sha256".to_string()).boxed(),
        gens::string_len(gens::alphanumeric(), 1..=8).boxed(),
    ])
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct CliFuzzCase {
    pub ops: Vec<CliFuzzOp>,
}

#[ephyra::generator]
pub fn valid_cli_fuzz_case(c: &CliFuzzCase) -> bool {
    c.ops.len() <= 14
}

#[ephyra::generator(valid_cli_fuzz_case, metrics = cli_fuzz_metrics)]
pub fn cli_fuzz_cases() -> impl Gen<Value = CliFuzzCase> {
    let op_gen = gens::one_of(vec![
        ephyra::any::<DocType>().map(CliFuzzOp::Type).boxed(),
        fuzz_flag_string_gen().map(CliFuzzOp::Types).boxed(),
        gens::one_of(vec![
            gens::constant(-1i64).boxed(),
            gens::constant(0i64).boxed(),
            gens::constant(1i64).boxed(),
            gens::constant(3i64).boxed(),
            gens::constant(i64::MAX).boxed(),
        ])
        .map(CliFuzzOp::Count)
        .boxed(),
        gens::constant(CliFuzzOp::LoadfileOnly).boxed(),
        ephyra::any::<LoadFormat>()
            .map(CliFuzzOp::LoadFileFormat)
            .boxed(),
        gens::vec(ephyra::any::<LoadFormat>())
            .len(1..=4)
            .map(CliFuzzOp::LoadFileFormats)
            .boxed(),
        gens::constant(CliFuzzOp::DatDelimitersCsv).boxed(),
        fuzz_flag_string_gen().map(CliFuzzOp::ColDelim).boxed(),
        fuzz_flag_string_gen().map(CliFuzzOp::QuoteDelim).boxed(),
        fuzz_flag_string_gen().map(CliFuzzOp::NewlineDelim).boxed(),
        ephyra::any::<EolKind>().map(CliFuzzOp::Eol).boxed(),
        ephyra::any::<EncodingKind>()
            .map(CliFuzzOp::Encoding)
            .boxed(),
        gens::constant(CliFuzzOp::ProductionSet).boxed(),
        gens::constant(CliFuzzOp::ProductionZip).boxed(),
        fuzz_flag_string_gen().map(CliFuzzOp::BatesPrefix).boxed(),
        gens::int(-2i64..=10).map(CliFuzzOp::BatesStart).boxed(),
        gens::int(-1i32..=22).map(CliFuzzOp::BatesDigits).boxed(),
        gens::constant(CliFuzzOp::BatesPageSuffix).boxed(),
        gens::int(-1i32..=5).map(CliFuzzOp::VolumeSize).boxed(),
        gens::int(0i32..=3).map(CliFuzzOp::RollingCount).boxed(),
        gens::constant(CliFuzzOp::RollingRestart).boxed(),
        gens::constant(CliFuzzOp::RedactedProduction).boxed(),
        gens::int(0u8..=3).map(CliFuzzOp::WithheldPolicy).boxed(),
        gens::constant(CliFuzzOp::WithMetadata).boxed(),
        gens::constant(CliFuzzOp::WithCollectionMetadata).boxed(),
        gens::constant(CliFuzzOp::WithFamilies).boxed(),
        gens::constant(CliFuzzOp::WithThreading).boxed(),
        gens::int(-1i32..=101)
            .map(CliFuzzOp::AttachmentRate)
            .boxed(),
        gens::constant(CliFuzzOp::ChaosMode).boxed(),
        fuzz_flag_string_gen().map(CliFuzzOp::ChaosAmount).boxed(),
        fuzz_flag_string_gen().map(CliFuzzOp::ChaosScenario).boxed(),
        fuzz_flag_string_gen().map(CliFuzzOp::ChaosTypes).boxed(),
        fuzz_flag_string_gen().map(CliFuzzOp::TiffPages).boxed(),
        fuzz_flag_string_gen().map(CliFuzzOp::TiffRealism).boxed(),
        fuzz_flag_string_gen().map(CliFuzzOp::TiffByteOrder).boxed(),
        gens::constant(CliFuzzOp::WithText).boxed(),
        fuzz_flag_string_gen().map(CliFuzzOp::HashMode).boxed(),
        fuzz_flag_string_gen()
            .map(CliFuzzOp::HashAlgorithms)
            .boxed(),
        fuzz_flag_string_gen().map(CliFuzzOp::ArchiveSuite).boxed(),
        fuzz_flag_string_gen().map(CliFuzzOp::DateFormat).boxed(),
        gens::int(-1i32..=101).map(CliFuzzOp::EmptyPct).boxed(),
        gens::int(0i32..=10).map(CliFuzzOp::CustodianCount).boxed(),
        gens::int(-1i64..=100).map(CliFuzzOp::Seed).boxed(),
    ]);

    gens::vec(op_gen)
        .len(1..=14)
        .iso(|ops| CliFuzzCase { ops }, |c: &CliFuzzCase| c.ops.clone())
}

pub fn cli_fuzz_metrics(c: &CliFuzzCase, _size: usize) -> Vec<(&'static str, f64)> {
    vec![("ops", c.ops.len() as f64)]
}

#[derive(Clone, Debug, PartialEq, Eq, ephyra::Arbitrary)]
pub struct StandardModeCase {
    pub mixed_types: bool,
    pub doc_type: DocType,
    #[ephyra(gen = gens::int(1u8..=8))]
    pub count: u8,
    pub include_load_file: bool,
    pub with_text: bool,
    pub with_families: bool,
    pub with_threading: bool,
    #[ephyra(gen = gens::int(0u8..=100))]
    pub attachment_rate: u8,
    pub tiff_realism: bool,
    pub tiff_big_endian: bool,
    pub with_bates: bool,
    pub page_suffix: bool,
    pub quote_none: bool,
    #[ephyra(gen = gens::int(0u32..=10_000))]
    pub seed: u32,
}

#[ephyra::generator]
pub fn valid_standard_mode_case(c: &StandardModeCase) -> bool {
    c.count >= 1 && c.count <= 8 && c.attachment_rate <= 100
}

#[ephyra::generator(valid_standard_mode_case, metrics = standard_mode_metrics)]
pub fn standard_mode_cases() -> impl Gen<Value = StandardModeCase> {
    ephyra::any::<StandardModeCase>()
}

pub fn standard_mode_metrics(c: &StandardModeCase, _size: usize) -> Vec<(&'static str, f64)> {
    vec![
        ("count", f64::from(c.count)),
        ("mixed", if c.mixed_types { 1.0 } else { 0.0 }),
    ]
}

#[derive(Clone, Debug, PartialEq, Eq, ephyra::Arbitrary)]
pub struct ProductionDeepCase {
    pub doc_type: DocType,
    #[ephyra(gen = gens::int(1u8..=6))]
    pub count: u8,
    #[ephyra(gen = gens::int(1u8..=4))]
    pub volume_size: u8,
    pub two_rolling: bool,
    pub restart_bates: bool,
    pub with_text: bool,
    pub with_families: bool,
    #[ephyra(gen = gens::int(0u8..=100))]
    pub attachment_rate: u8,
    #[ephyra(gen = gens::int(0u32..=10_000))]
    pub seed: u32,
}

#[ephyra::generator]
pub fn valid_production_deep_case(c: &ProductionDeepCase) -> bool {
    c.count >= 1
        && c.count <= 6
        && c.volume_size >= 1
        && c.volume_size <= 4
        && c.attachment_rate <= 100
}

#[ephyra::generator(valid_production_deep_case, metrics = production_deep_metrics)]
pub fn production_deep_cases() -> impl Gen<Value = ProductionDeepCase> {
    ephyra::any::<ProductionDeepCase>()
}

pub fn production_deep_metrics(c: &ProductionDeepCase, _size: usize) -> Vec<(&'static str, f64)> {
    vec![
        ("count", f64::from(c.count)),
        ("two_rolling", if c.two_rolling { 1.0 } else { 0.0 }),
    ]
}

/// Decodes raw file bytes according to Zipper's --encoding setting.
pub fn decode_loadfile_bytes(bytes: &[u8], encoding: EncodingKind) -> Result<String, String> {
    match encoding {
        EncodingKind::Utf8 => {
            let s = std::str::from_utf8(bytes).map_err(|e| format!("Invalid UTF-8 bytes: {e}"))?;
            Ok(s.strip_prefix('\u{feff}').unwrap_or(s).to_string())
        }
        EncodingKind::Utf16 => {
            if bytes.len() < 2 || bytes[0] != 0xFF || bytes[1] != 0xFE {
                return Err("Missing UTF-16 LE BOM (FF FE)".to_string());
            }
            let (cow, had_errors) = encoding_rs::UTF_16LE.decode_without_bom_handling(&bytes[2..]);
            if had_errors {
                return Err("UTF-16 LE decode had replacement errors".to_string());
            }
            Ok(cow.into_owned())
        }
        EncodingKind::Ansi => {
            if encoding_rs::Encoding::for_bom(bytes).is_some() {
                return Err("Unexpected Unicode BOM in ANSI output".to_string());
            }
            let (cow, _) = encoding_rs::WINDOWS_1252.decode_without_bom_handling(bytes);
            Ok(cow.into_owned())
        }
    }
}

#[test]
fn ansi_decoder_rejects_unicode_boms() {
    assert_eq!(
        decode_loadfile_bytes(b"\x80", EncodingKind::Ansi).unwrap(),
        "€"
    );
    for bytes in [b"\xef\xbb\xbf".as_slice(), b"\xff\xfe", b"\xfe\xff"] {
        assert!(decode_loadfile_bytes(bytes, EncodingKind::Ansi).is_err());
    }
}

/// Parses a DAT/Concordance file with a single-char column delimiter and optional quote char.
pub fn parse_delimited_records(
    text: &str,
    col_delim: char,
    quote_delim: Option<char>,
) -> Result<Vec<Vec<String>>, String> {
    let mut records = Vec::new();
    let mut row = Vec::new();
    let mut field = String::new();
    let chars: Vec<char> = text.chars().collect();
    let mut i = 0;

    if let Some(q) = quote_delim {
        let mut in_quotes = false;
        let mut at_field_start = true;
        let mut after_close_quote = false;
        while i < chars.len() {
            let ch = chars[i];
            if in_quotes {
                if ch == q {
                    if i + 1 < chars.len() && chars[i + 1] == q {
                        field.push(q);
                        i += 2;
                        continue;
                    }
                    in_quotes = false;
                    after_close_quote = true;
                    i += 1;
                    continue;
                }
                field.push(ch);
                i += 1;
                continue;
            }

            if at_field_start && ch == q {
                in_quotes = true;
                at_field_start = false;
                i += 1;
                continue;
            }

            if ch == col_delim {
                row.push(std::mem::take(&mut field));
                at_field_start = true;
                after_close_quote = false;
                i += 1;
                continue;
            }

            if ch == '\r' || ch == '\n' {
                if ch == '\r' && i + 1 < chars.len() && chars[i + 1] == '\n' {
                    i += 1;
                }
                if !(row.is_empty() && field.is_empty() && !after_close_quote) {
                    row.push(std::mem::take(&mut field));
                    records.push(std::mem::take(&mut row));
                }
                at_field_start = true;
                after_close_quote = false;
                i += 1;
                continue;
            }

            if after_close_quote {
                return Err(format!(
                    "Unexpected character {:?} after closing quote in row {}",
                    ch,
                    records.len()
                ));
            }
            at_field_start = false;
            field.push(ch);
            i += 1;
        }
        if in_quotes {
            return Err("Unclosed quote at EOF".to_string());
        }
        if !row.is_empty() || !field.is_empty() || after_close_quote {
            row.push(field);
            records.push(row);
        }
    } else {
        // Split on CRLF, LF, or standalone CR so --eol CR is parsed record-by-record.
        for line in text.split('\n').flat_map(|s| s.split('\r')) {
            if line.is_empty() {
                continue;
            }
            records.push(line.split(col_delim).map(|s| s.to_string()).collect());
        }
    }

    Ok(records)
}

/// Validates a ZIP file with the `zip` crate and returns entry names.
pub fn validate_zip_entries(zip_path: &Path) -> Result<Vec<String>, String> {
    let f = std::fs::File::open(zip_path).map_err(|e| format!("open zip: {e}"))?;
    let mut archive = zip::ZipArchive::new(f).map_err(|e| format!("ZipArchive::new: {e}"))?;
    if archive.has_duplicate_names() {
        return Err("Duplicate zip entry names".to_string());
    }
    let mut names = Vec::with_capacity(archive.len());
    for idx in 0..archive.len() {
        let mut entry = archive
            .by_index(idx)
            .map_err(|e| format!("entry {idx}: {e}"))?;
        let name = entry
            .name()
            .map_err(|e| format!("entry {idx} name: {e}"))?
            .into_owned();
        if entry.enclosed_name().is_none() {
            return Err(format!("Invalid zip entry path: {name}"));
        }
        let mut buf = Vec::new();
        entry
            .read_to_end(&mut buf)
            .map_err(|e| format!("read entry {name}: {e}"))?;
        names.push(name);
    }
    Ok(names)
}

pub fn find_first_with_ext(dir: &Path, ext: &str) -> Option<PathBuf> {
    let entries = std::fs::read_dir(dir).ok()?;
    for entry in entries.flatten() {
        let path = entry.path();
        if path.extension().and_then(|e| e.to_str()) == Some(ext) {
            return Some(path);
        }
    }
    None
}
