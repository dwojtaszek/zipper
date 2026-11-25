use ephyra_harness::*;
use std::fs;
use std::io::Read;
use std::path::Path;

fn delimiter_cli_value(ch: char) -> String {
    format!("ascii:{}", ch as u32)
}

#[ephyra::test(gen = loadfile_cases(), tests = 80, failures = 10)]
fn prop_loadfile_only_invariants(c: LoadfileCase) {
    let dir = tempfile::tempdir().expect("tempdir");
    let out_dir = dir.path().join("out");
    let mut args = vec![
        "--loadfile-only".to_string(),
        "--loadfile-format".to_string(),
        c.format.flag_value().to_string(),
        "--type".to_string(),
        c.doc_type.flag_value().to_string(),
        "--count".to_string(),
        c.count.to_string(),
        "--output-path".to_string(),
        out_dir.to_string_lossy().into_owned(),
        "--eol".to_string(),
        c.eol.flag_value().to_string(),
        "--encoding".to_string(),
        c.encoding.flag_value().to_string(),
        "--seed".to_string(),
        c.seed.to_string(),
    ];

    if matches!(c.format, LoadFormat::Dat | LoadFormat::Concordance) {
        args.push("--col-delim".to_string());
        args.push(delimiter_cli_value(c.col_char()));
        args.push("--quote-delim".to_string());
        match c.quote_char() {
            Some(q) => args.push(delimiter_cli_value(q)),
            None => args.push("none".to_string()),
        }
    }

    match c.profile {
        ProfileKind::None => {}
        ProfileKind::WithMetadata => args.push("--with-metadata".to_string()),
        ProfileKind::Minimal => {
            args.push("--column-profile".to_string());
            args.push("minimal".to_string());
        }
        ProfileKind::Standard => {
            args.push("--column-profile".to_string());
            args.push("standard".to_string());
        }
        ProfileKind::Litigation => {
            args.push("--column-profile".to_string());
            args.push("litigation".to_string());
        }
        ProfileKind::Full => {
            args.push("--column-profile".to_string());
            args.push("full".to_string());
        }
    }

    if c.with_collection_metadata {
        args.push("--with-collection-metadata".to_string());
    }
    if c.with_families {
        args.push("--with-families".to_string());
    }
    if c.with_threading {
        args.push("--with-threading".to_string());
    }
    if c.attachment_rate > 0 {
        args.push("--attachment-rate".to_string());
        args.push(c.attachment_rate.to_string());
    }
    if c.simulated_hash {
        args.push("--hash-mode".to_string());
        args.push("simulated".to_string());
    }

    let out = run_zipper(dir.path(), &args);
    assert_no_crash(&out, &format!("loadfile_only {args:?}"));

    if out.code != Some(0) {
        ephyra::signature(&format!("loadfile_nonzero_exit_{:?}", c.format));
        panic!(
            "loadfile-only failed unexpectedly (exit {:?}): args={args:?}\nstderr:\n{}",
            out.code, out.stderr
        );
    }

    let loadfile = find_first_with_ext(&out_dir, c.format.ext()).unwrap_or_else(|| {
        panic!(
            "Missing .{} output in {}",
            c.format.ext(),
            out_dir.display()
        )
    });
    let raw = fs::read(&loadfile).expect("read loadfile");

    let expected_enc = if c.format == LoadFormat::EdrmXml {
        EncodingKind::Utf8
    } else {
        c.encoding
    };
    let text = decode_loadfile_bytes(&raw, expected_enc).unwrap_or_else(|e| {
        ephyra::signature(&format!(
            "loadfile_encoding_{:?}_{:?}",
            c.format, expected_enc
        ));
        panic!(
            "Encoding decode error for {:?} ({:?}): {e}",
            c.format, expected_enc
        );
    });

    // EOL check
    match c.eol {
        EolKind::Crlf => assert!(text.contains("\r\n"), "Expected CRLF line endings"),
        EolKind::Lf => {
            if c.format != LoadFormat::EdrmXml {
                assert!(
                    text.contains('\n') && !text.contains('\r'),
                    "Expected LF-only"
                );
            }
        }
        EolKind::Cr => {
            if c.format != LoadFormat::EdrmXml {
                assert!(
                    !text.contains('\n'),
                    "Expected CR-only for {:?}, found LF",
                    c.format
                );
            }
        }
    }

    match c.format {
        LoadFormat::Dat | LoadFormat::Concordance => {
            if c.quote_style == QuoteStyle::None {
                let first_line = text
                    .split('\n')
                    .flat_map(|s| s.split('\r'))
                    .next()
                    .unwrap_or("");
                if first_line.contains('\u{fe}') {
                    ephyra::signature(&format!("quote_none_still_quoted_{:?}", c.format));
                    panic!(
                        "{:?} emitted thorn quote delimiter despite --quote-delim none:\n{first_line}",
                        c.format
                    );
                }
            }
            let parsed = parse_delimited_records(&text, c.col_char(), c.quote_char())
                .unwrap_or_else(|e| {
                    ephyra::signature(&format!(
                        "delimited_parse_fail_{:?}_{:?}_{:?}",
                        c.format, c.col_style, c.quote_style
                    ));
                    panic!(
                        "Failed to parse {:?} (col={:?}, quote={:?}): {e}\ncontent:\n{}",
                        c.format,
                        c.col_char(),
                        c.quote_char(),
                        text.chars().take(500).collect::<String>()
                    );
                });
            assert!(
                parsed.len() >= 1 + usize::from(c.count),
                "Expected at least {} rows, got {}",
                1 + usize::from(c.count),
                parsed.len()
            );
            let header_cols = parsed[0].len();
            for (idx, row) in parsed.iter().enumerate() {
                if row.len() != header_cols {
                    ephyra::signature(&format!("delimited_col_mismatch_{:?}", c.format));
                    panic!(
                        "Row {idx} has {} cols, header has {header_cols} (format={:?}, col={:?}, quote={:?})",
                        row.len(),
                        c.format,
                        c.col_char(),
                        c.quote_char()
                    );
                }
            }
        }
        LoadFormat::Csv => {
            let mut rdr = csv::ReaderBuilder::new()
                .has_headers(true)
                .from_reader(text.as_bytes());
            let headers = rdr
                .headers()
                .unwrap_or_else(|e| panic!("CSV header parse failed: {e}"))
                .clone();
            let mut row_count = 0usize;
            for rec in rdr.records() {
                let rec = rec.unwrap_or_else(|e| {
                    ephyra::signature("csv_record_parse_fail");
                    panic!("CSV record parse failed: {e}");
                });
                assert_eq!(rec.len(), headers.len(), "CSV column count mismatch");
                row_count += 1;
            }
            assert!(row_count >= usize::from(c.count));
        }
        LoadFormat::Opt => {
            let lines: Vec<&str> = text
                .split('\n')
                .flat_map(|s| s.split('\r'))
                .filter(|l| !l.is_empty())
                .collect();
            assert!(lines.len() >= usize::from(c.count));
            for (idx, line) in lines.iter().enumerate() {
                let cols: Vec<&str> = line.split(',').collect();
                assert_eq!(
                    cols.len(),
                    7,
                    "OPT line {idx} expected 7 columns, got {}: {line}",
                    cols.len()
                );
            }
        }
        LoadFormat::EdrmXml => {
            let doc = roxmltree::Document::parse(&text).unwrap_or_else(|e| {
                ephyra::signature("edrm_xml_parse_fail");
                panic!("Invalid EDRM-XML output: {e}");
            });
            let doc_nodes = doc
                .descendants()
                .filter(|n| n.has_tag_name("Document"))
                .count();
            assert!(
                doc_nodes >= usize::from(c.count),
                "Expected >= {} <Document> nodes, got {doc_nodes}",
                c.count
            );
        }
    }

    // Determinism check with identical seed
    let out_dir2 = dir.path().join("out2");
    let mut args2 = args.clone();
    let idx = args2.iter().position(|a| a == "--output-path").unwrap() + 1;
    args2[idx] = out_dir2.to_string_lossy().into_owned();
    let out2 = run_zipper(dir.path(), &args2);
    assert_no_crash(&out2, "second deterministic run");
    assert_eq!(out2.code, Some(0), "Second deterministic run failed");
    let loadfile2 = find_first_with_ext(&out_dir2, c.format.ext()).expect("second loadfile");
    let raw2 = fs::read(&loadfile2).expect("read loadfile2");
    if raw != raw2 {
        ephyra::signature(&format!("loadfile_nondeterministic_{:?}", c.format));
        panic!(
            "Non-deterministic loadfile output for {:?} with seed {}",
            c.format, c.seed
        );
    }
}

#[ephyra::test(gen = chaos_cases(), tests = 60, failures = 10)]
fn prop_chaos_invariants(c: ChaosCase) {
    let dir = tempfile::tempdir().expect("tempdir");
    let out_dir = dir.path().join("out");
    let mut args: Vec<String> = Vec::new();
    let mut expect_sidecar = true;

    match c.mode {
        ChaosModeCase::RealisticImportFailures => {
            args.extend([
                "--loadfile-only".into(),
                "--loadfile-format".into(),
                "dat".into(),
                "--count".into(),
                c.count.to_string(),
                "--chaos-mode".into(),
                "--chaos-scenario".into(),
                "realistic-import-failures".into(),
                "--chaos-amount".into(),
                format!("{}%", c.amount_pct),
            ]);
        }
        ChaosModeCase::CsvEscapingFailures => {
            args.extend([
                "--loadfile-only".into(),
                "--loadfile-format".into(),
                "csv".into(),
                "--count".into(),
                c.count.to_string(),
                "--chaos-mode".into(),
                "--chaos-scenario".into(),
                "csv-escaping-failures".into(),
                "--chaos-amount".into(),
                format!("{}%", c.amount_pct),
            ]);
        }
        ChaosModeCase::ConcordanceImportFailures => {
            args.extend([
                "--loadfile-only".into(),
                "--loadfile-format".into(),
                "concordance".into(),
                "--count".into(),
                c.count.to_string(),
                "--chaos-mode".into(),
                "--chaos-scenario".into(),
                "concordance-import-failures".into(),
                "--chaos-amount".into(),
                format!("{}%", c.amount_pct),
            ]);
        }
        ChaosModeCase::EdrmXmlStructuralFailures => {
            args.extend([
                "--loadfile-only".into(),
                "--loadfile-format".into(),
                "edrm-xml".into(),
                "--count".into(),
                c.count.to_string(),
                "--chaos-mode".into(),
                "--chaos-scenario".into(),
                "edrm-xml-structural-failures".into(),
                "--chaos-amount".into(),
                format!("{}%", c.amount_pct),
            ]);
        }
        ChaosModeCase::AdversarialValues { count, comma_delim } => {
            let eff_count = if count % 5 == 0 { 0 } else { c.count };
            args.extend([
                "--loadfile-only".into(),
                "--loadfile-format".into(),
                "dat".into(),
                "--count".into(),
                eff_count.to_string(),
                "--with-metadata".into(),
                "--chaos-mode".into(),
                "--chaos-scenario".into(),
                "adversarial-values".into(),
            ]);
            if comma_delim {
                args.extend(["--dat-delimiters".into(), "csv".into()]);
            }
            expect_sidecar = false;
        }
        ChaosModeCase::CrossFormatDesync => {
            args.extend([
                "--loadfile-only".into(),
                "--load-file-formats".into(),
                "dat,opt".into(),
                "--count".into(),
                c.count.to_string(),
                "--chaos-mode".into(),
                "--chaos-scenario".into(),
                "cross-format-desync".into(),
                "--chaos-amount".into(),
                format!("{}%", c.amount_pct),
            ]);
        }
        ChaosModeCase::BrokenFamilies => {
            args.extend([
                "--production-set".into(),
                "--bates-prefix".into(),
                "TEST".into(),
                "--type".into(),
                "eml".into(),
                "--with-families".into(),
                "--attachment-rate".into(),
                "100".into(),
                "--count".into(),
                c.count.to_string(),
                "--chaos-mode".into(),
                "--chaos-scenario".into(),
                "broken-families".into(),
                "--chaos-amount".into(),
                format!("{}%", c.amount_pct),
            ]);
        }
        ChaosModeCase::EmailMimeChaos => {
            args.extend([
                "--type".into(),
                "eml".into(),
                "--count".into(),
                c.count.to_string(),
                "--chaos-mode".into(),
                "--chaos-types".into(),
                "mime-boundary,encoded-word,header-fold,rf822-depth".into(),
                "--chaos-amount".into(),
                format!("{}%", c.amount_pct),
            ]);
        }
        ChaosModeCase::TiffChaos { big_endian } => {
            args.extend([
                "--type".into(),
                "tiff".into(),
                "--count".into(),
                c.count.to_string(),
                "--chaos-mode".into(),
                "--chaos-types".into(),
                "tiff-dimension-lie,tiff-truncated-ifd,tiff-tag-count-lie,tiff-strip-offset".into(),
                "--chaos-amount".into(),
                format!("{}%", c.amount_pct),
            ]);
            if big_endian {
                args.extend([
                    "--with-tiff-realism".into(),
                    "--tiff-byte-order".into(),
                    "mm".into(),
                ]);
            }
        }
    }

    args.extend([
        "--output-path".into(),
        out_dir.to_string_lossy().into_owned(),
        "--seed".into(),
        c.seed.to_string(),
    ]);

    let out = run_zipper(dir.path(), &args);
    assert_no_crash(&out, &format!("chaos {args:?}"));

    if out.code != Some(0) {
        ephyra::signature(&format!(
            "chaos_nonzero_{:?}",
            std::mem::discriminant(&c.mode)
        ));
        panic!(
            "Chaos run failed (exit {:?}): args={args:?}\nstderr:\n{}\nstdout:\n{}",
            out.code, out.stderr, out.stdout
        );
    }

    if expect_sidecar {
        let mut found_sidecar = false;
        fn walk_sidecars(p: &Path, found: &mut bool) {
            if let Ok(rd) = fs::read_dir(p) {
                for e in rd.flatten() {
                    let path = e.path();
                    if path.is_dir() {
                        walk_sidecars(&path, found);
                    } else if let Some(name) = path.file_name().and_then(|n| n.to_str()) {
                        if name.ends_with("_chaos_expectations.json") {
                            let bytes = fs::read(&path).expect("read sidecar");
                            let _: serde_json::Value = serde_json::from_slice(&bytes)
                                .unwrap_or_else(|err| {
                                    panic!("Invalid sidecar JSON {}: {err}", path.display())
                                });
                            *found = true;
                        }
                    }
                }
            }
        }
        walk_sidecars(&out_dir, &mut found_sidecar);
        assert!(
            found_sidecar,
            "Expected *_chaos_expectations.json sidecar in {}",
            out_dir.display()
        );
    }
}

#[ephyra::test(gen = production_cases(), tests = 50, failures = 10)]
fn prop_production_set_invariants(c: ProductionCase) {
    let dir = tempfile::tempdir().expect("tempdir");
    let out_dir = dir.path().join("prod");
    let mut args = vec![
        "--production-set".to_string(),
        "--bates-prefix".to_string(),
        "PROD".to_string(),
        "--bates-start".to_string(),
        c.bates_start.to_string(),
        "--bates-digits".to_string(),
        c.bates_digits.to_string(),
        "--type".to_string(),
        c.doc_type.flag_value().to_string(),
        "--count".to_string(),
        c.count.to_string(),
        "--volume-size".to_string(),
        c.volume_size.to_string(),
        "--rolling-count".to_string(),
        c.rolling_count.to_string(),
        "--rolling-bates-mode".to_string(),
        if c.restart_bates {
            "restart"
        } else {
            "continuous"
        }
        .to_string(),
        "--output-path".to_string(),
        out_dir.to_string_lossy().into_owned(),
        "--seed".to_string(),
        c.seed.to_string(),
    ];

    if c.page_suffix {
        args.push("--bates-page-suffix".to_string());
    }
    if c.redacted {
        args.push("--redacted-production".to_string());
    }
    if c.production_zip {
        args.push("--production-zip".to_string());
    }
    if c.with_families {
        args.push("--with-families".to_string());
    }
    if c.with_text {
        args.push("--with-text".to_string());
    }
    if c.attachment_rate > 0 {
        args.push("--attachment-rate".to_string());
        args.push(c.attachment_rate.to_string());
    }
    if c.tiff_big_endian && c.doc_type == DocType::Tiff {
        args.push("--with-tiff-realism".to_string());
        args.push("--tiff-byte-order".to_string());
        args.push("mm".to_string());
    }

    let out = run_zipper(dir.path(), &args);
    assert_no_crash(&out, &format!("production {args:?}"));

    if out.code != Some(0) {
        ephyra::signature("production_nonzero_exit");
        panic!(
            "Production set failed (exit {:?}): args={args:?}\nstderr:\n{}\nstdout:\n{}",
            out.code, out.stderr, out.stdout
        );
    }

    let mut manifests = Vec::new();
    fn collect_manifests(p: &Path, out: &mut Vec<std::path::PathBuf>) {
        if let Ok(rd) = fs::read_dir(p) {
            for e in rd.flatten() {
                let path = e.path();
                if path.is_dir() {
                    collect_manifests(&path, out);
                } else if path.file_name().and_then(|n| n.to_str()) == Some("_manifest.json") {
                    out.push(path);
                }
            }
        }
    }
    collect_manifests(&out_dir, &mut manifests);
    assert_eq!(
        manifests.len(),
        usize::from(c.rolling_count),
        "Expected {} _manifest.json files, found {}",
        c.rolling_count,
        manifests.len()
    );

    if c.production_zip {
        for manifest in &manifests {
            let production = manifest.parent().unwrap().file_name().unwrap();
            assert!(out_dir.join(production).with_extension("zip").is_file());
        }
    }

    // Verify self-comparison passes on the first manifest
    let cmp_out = dir.path().join("compare.json");
    let m0 = manifests[0].to_string_lossy().into_owned();
    let cmp_args = vec![
        "--compare-production-manifests".to_string(),
        format!("{m0},{m0}"),
        "--comparison-mode".to_string(),
        "reproduction".to_string(),
        "--comparison-output".to_string(),
        cmp_out.to_string_lossy().into_owned(),
    ];
    let cmp_res = run_zipper(dir.path(), &cmp_args);
    assert_no_crash(&cmp_res, "manifest self-compare");
    if cmp_res.code != Some(0) {
        ephyra::signature("manifest_self_compare_failed");
        panic!(
            "Manifest self-compare failed on {}: stderr={}\nstdout={}",
            m0, cmp_res.stderr, cmp_res.stdout
        );
    }
}

#[ephyra::test(gen = source_csv_cases(), tests = 50, failures = 10)]
fn prop_source_csv_invariants(c: SourceCsvCase) {
    let dir = tempfile::tempdir().expect("tempdir");
    let csv_path = dir.path().join("source.csv");
    {
        let mut wtr = csv::Writer::from_path(&csv_path).expect("csv writer");
        wtr.write_record(["FilePath", "FileType", "Custodian", "Note"])
            .expect("header");
        for row in &c.rows {
            wtr.write_record([
                row.path.as_str(),
                row.doc_type.flag_value(),
                row.custodian.as_str(),
                row.note.as_str(),
            ])
            .expect("record");
        }
        wtr.flush().expect("flush");
    }

    let out_dir = dir.path().join("out");
    let mut args = vec![
        "--input-csv".to_string(),
        "source.csv".to_string(),
        "--output-path".to_string(),
        out_dir.to_string_lossy().into_owned(),
        "--seed".to_string(),
        c.seed.to_string(),
    ];
    if c.production_set {
        args.push("--production-set".to_string());
        args.push("--bates-prefix".to_string());
        args.push("SRC".to_string());
    } else if c.loadfile_only {
        args.push("--loadfile-only".to_string());
        args.push("--loadfile-format".to_string());
        args.push(c.format.flag_value().to_string());
    } else {
        args.push("--load-file-format".to_string());
        args.push(c.format.flag_value().to_string());
    }

    let out = run_zipper(dir.path(), &args);
    assert_no_crash(&out, &format!("source_csv {args:?}"));
    if out.code != Some(0) {
        ephyra::signature(&format!("source_csv_nonzero_{:?}", c.format));
        panic!(
            "Source CSV run failed (exit {:?}): args={args:?}\nstderr:\n{}",
            out.code, out.stderr
        );
    }

    // Verify CSV outputs parse cleanly and retain the right row count.
    if !c.production_set && c.format == LoadFormat::Csv {
        let csv_out = find_first_with_ext(&out_dir, "csv").expect("Source CSV output");
        let raw = fs::read_to_string(&csv_out).expect("read csv output");
        let trimmed = raw.strip_prefix('\u{feff}').unwrap_or(&raw);
        let mut rdr = csv::ReaderBuilder::new()
            .has_headers(true)
            .from_reader(trimmed.as_bytes());
        let hdr_len = rdr.headers().expect("csv headers").len();
        let mut count = 0usize;
        for rec in rdr.records() {
            let rec = rec.unwrap_or_else(|e| {
                ephyra::signature("source_csv_output_parse_fail");
                panic!("Source CSV output parse failed: {e}");
            });
            assert_eq!(rec.len(), hdr_len, "Source CSV output column mismatch");
            count += 1;
        }
        assert_eq!(count, c.rows.len(), "Source CSV row count mismatch");
    }
}

#[ephyra::test(gen = archive_cases(), tests = 40, failures = 10)]
fn prop_archive_invariants(c: ArchiveCase) {
    let dir = tempfile::tempdir().expect("tempdir");
    let out_dir = dir.path().join("out");
    let mut args = vec![
        "--type".to_string(),
        c.doc_type.flag_value().to_string(),
        "--count".to_string(),
        c.count.to_string(),
        "--folders".to_string(),
        c.folders.to_string(),
        "--compression".to_string(),
        if c.deflate { "deflate" } else { "store" }.to_string(),
        "--output-path".to_string(),
        out_dir.to_string_lossy().into_owned(),
        "--seed".to_string(),
        c.seed.to_string(),
    ];
    if c.include_load_file {
        args.push("--include-load-file".to_string());
    }
    if c.with_text {
        args.push("--with-text".to_string());
    }

    let out = run_zipper(dir.path(), &args);
    assert_no_crash(&out, &format!("archive {args:?}"));
    if out.code != Some(0) {
        ephyra::signature("archive_nonzero_exit");
        panic!("Archive run failed: stderr={}", out.stderr);
    }

    let zip_path = find_first_with_ext(&out_dir, "zip")
        .unwrap_or_else(|| panic!("Missing .zip output in {}", out_dir.display()));
    let entries = validate_zip_entries(&zip_path).unwrap_or_else(|e| {
        ephyra::signature("zip_invalid");
        panic!("Invalid ZIP {}: {e}", zip_path.display());
    });
    let native_count = entries
        .iter()
        .filter(|name| {
            let extension = Path::new(name).extension().and_then(|ext| ext.to_str());
            extension == Some(c.doc_type.flag_value())
                || (c.doc_type == DocType::Tiff && extension == Some("tif"))
        })
        .count();
    assert_eq!(native_count, usize::from(c.count));
}

#[ephyra::test(gen = cli_fuzz_cases(), tests = 150, failures = 10)]
fn prop_cli_fuzz_no_crash(c: CliFuzzCase) {
    let dir = tempfile::tempdir().expect("tempdir");
    let out_dir = dir.path().join("fuzz_out");
    let mut args = vec![
        "--output-path".to_string(),
        out_dir.to_string_lossy().into_owned(),
    ];

    for op in &c.ops {
        match op {
            CliFuzzOp::Type(t) => {
                args.push("--type".into());
                args.push(t.flag_value().into());
            }
            CliFuzzOp::Types(s) => {
                args.push("--types".into());
                args.push(s.clone());
            }
            CliFuzzOp::Count(n) => {
                args.push("--count".into());
                args.push(n.to_string());
            }
            CliFuzzOp::LoadfileOnly => args.push("--loadfile-only".into()),
            CliFuzzOp::LoadFileFormat(f) => {
                args.push("--loadfile-format".into());
                args.push(f.flag_value().into());
            }
            CliFuzzOp::LoadFileFormats(fs) => {
                args.push("--load-file-formats".into());
                args.push(
                    fs.iter()
                        .map(|f| f.flag_value())
                        .collect::<Vec<_>>()
                        .join(","),
                );
            }
            CliFuzzOp::DatDelimitersCsv => {
                args.push("--dat-delimiters".into());
                args.push("csv".into());
            }
            CliFuzzOp::ColDelim(s) => {
                args.push("--col-delim".into());
                args.push(s.clone());
            }
            CliFuzzOp::QuoteDelim(s) => {
                args.push("--quote-delim".into());
                args.push(s.clone());
            }
            CliFuzzOp::NewlineDelim(s) => {
                args.push("--newline-delim".into());
                args.push(s.clone());
            }
            CliFuzzOp::Eol(e) => {
                args.push("--eol".into());
                args.push(e.flag_value().into());
            }
            CliFuzzOp::Encoding(e) => {
                args.push("--encoding".into());
                args.push(e.flag_value().into());
            }
            CliFuzzOp::ProductionSet => args.push("--production-set".into()),
            CliFuzzOp::ProductionZip => args.push("--production-zip".into()),
            CliFuzzOp::BatesPrefix(s) => {
                args.push("--bates-prefix".into());
                args.push(s.clone());
            }
            CliFuzzOp::BatesStart(n) => {
                args.push("--bates-start".into());
                args.push(n.to_string());
            }
            CliFuzzOp::BatesDigits(n) => {
                args.push("--bates-digits".into());
                args.push(n.to_string());
            }
            CliFuzzOp::BatesPageSuffix => args.push("--bates-page-suffix".into()),
            CliFuzzOp::VolumeSize(n) => {
                args.push("--volume-size".into());
                args.push(n.to_string());
            }
            CliFuzzOp::RollingCount(n) => {
                args.push("--rolling-count".into());
                args.push(n.to_string());
            }
            CliFuzzOp::RollingRestart => {
                args.push("--rolling-bates-mode".into());
                args.push("restart".into());
            }
            CliFuzzOp::RedactedProduction => args.push("--redacted-production".into()),
            CliFuzzOp::WithheldPolicy(p) => {
                let pol = match p % 4 {
                    0 => "keep-native",
                    1 => "omit-native-path",
                    2 => "replace-with-placeholder",
                    _ => "bogus-policy",
                };
                args.push("--withheld-native-policy".into());
                args.push(pol.into());
            }
            CliFuzzOp::WithMetadata => args.push("--with-metadata".into()),
            CliFuzzOp::WithCollectionMetadata => args.push("--with-collection-metadata".into()),
            CliFuzzOp::WithFamilies => args.push("--with-families".into()),
            CliFuzzOp::WithThreading => args.push("--with-threading".into()),
            CliFuzzOp::AttachmentRate(r) => {
                args.push("--attachment-rate".into());
                args.push(r.to_string());
            }
            CliFuzzOp::ChaosMode => args.push("--chaos-mode".into()),
            CliFuzzOp::ChaosAmount(s) => {
                args.push("--chaos-amount".into());
                args.push(s.clone());
            }
            CliFuzzOp::ChaosScenario(s) => {
                args.push("--chaos-scenario".into());
                args.push(s.clone());
            }
            CliFuzzOp::ChaosTypes(s) => {
                args.push("--chaos-types".into());
                args.push(s.clone());
            }
            CliFuzzOp::TiffPages(s) => {
                args.push("--tiff-pages".into());
                args.push(s.clone());
            }
            CliFuzzOp::TiffRealism(s) => {
                args.push("--tiff-realism".into());
                args.push(s.clone());
            }
            CliFuzzOp::TiffByteOrder(s) => {
                args.push("--tiff-byte-order".into());
                args.push(s.clone());
            }
            CliFuzzOp::WithText => args.push("--with-text".into()),
            CliFuzzOp::HashMode(s) => {
                args.push("--hash-mode".into());
                args.push(s.clone());
            }
            CliFuzzOp::HashAlgorithms(s) => {
                args.push("--hash-algorithms".into());
                args.push(s.clone());
            }
            CliFuzzOp::ArchiveSuite(s) => {
                args.push("--archive-test-suite".into());
                args.push(s.clone());
            }
            CliFuzzOp::DateFormat(s) => {
                args.push("--date-format".into());
                args.push(s.clone());
            }
            CliFuzzOp::EmptyPct(n) => {
                args.push("--empty-percentage".into());
                args.push(n.to_string());
            }
            CliFuzzOp::CustodianCount(n) => {
                args.push("--custodian-count".into());
                args.push(n.to_string());
            }
            CliFuzzOp::Seed(s) => {
                args.push("--seed".into());
                args.push(s.to_string());
            }
        }
    }

    let out = run_zipper(dir.path(), &args);
    if out.timed_out {
        ephyra::signature("cli_fuzz_timeout");
        panic!("Zipper timed out on fuzzed args: {args:?}");
    }
    if out.stderr.contains("Unhandled exception") {
        let first_exc = out
            .stderr
            .lines()
            .find(|l| l.contains("Unhandled exception") || l.contains("Exception:"))
            .unwrap_or("Unhandled exception")
            .to_string();
        ephyra::signature(&format!("cli_fuzz_unhandled: {first_exc}"));
        panic!("Unhandled exception on args {args:?}:\n{}", out.stderr);
    }
    if out.code != Some(0) && out.code != Some(1) {
        ephyra::signature(&format!("cli_fuzz_exit_{:?}", out.code));
        panic!(
            "Unexpected exit {:?} on args {args:?}:\nstderr:\n{}\nstdout:\n{}",
            out.code, out.stderr, out.stdout
        );
    }
}

#[ephyra::test(gen = standard_mode_cases(), tests = 50, failures = 10)]
fn prop_standard_multiformat_invariants(c: StandardModeCase) {
    let dir = tempfile::tempdir().expect("tempdir");
    let out_dir = dir.path().join("std");
    let mut args = vec![
        "--output-path".to_string(),
        out_dir.to_string_lossy().into_owned(),
        "--count".to_string(),
        c.count.to_string(),
        "--load-file-formats".to_string(),
        "dat,opt,csv,concordance,edrm-xml".to_string(),
        "--seed".to_string(),
        c.seed.to_string(),
    ];

    if c.mixed_types {
        args.push("--types".into());
        args.push("pdf:40,eml:30,tiff:30".into());
    } else {
        args.push("--type".into());
        args.push(c.doc_type.flag_value().into());
    }
    if c.include_load_file {
        args.push("--include-load-file".into());
    }
    if c.with_text {
        args.push("--with-text".into());
    }
    if c.with_families {
        args.push("--with-families".into());
    }
    if c.with_threading {
        args.push("--with-threading".into());
    }
    if c.attachment_rate > 0 {
        args.push("--attachment-rate".into());
        args.push(c.attachment_rate.to_string());
    }
    if c.tiff_realism && (c.mixed_types || c.doc_type == DocType::Tiff) {
        args.push("--with-tiff-realism".into());
        if c.tiff_big_endian {
            args.push("--tiff-byte-order".into());
            args.push("mm".into());
        }
    }
    if c.with_bates {
        args.push("--bates-prefix".into());
        args.push("STD".into());
        if c.page_suffix {
            args.push("--bates-page-suffix".into());
        }
    }
    if c.quote_none {
        args.push("--quote-delim".into());
        args.push("none".into());
    }

    let out = run_zipper(dir.path(), &args);
    assert_no_crash(&out, &format!("standard_multiformat {args:?}"));
    if out.code != Some(0) {
        ephyra::signature("standard_multiformat_nonzero");
        panic!(
            "Standard multiformat failed (exit {:?}): args={args:?}\nstderr:\n{}",
            out.code, out.stderr
        );
    }

    let zip_path = find_first_with_ext(&out_dir, "zip").expect("zip output");
    let entries = validate_zip_entries(&zip_path).unwrap_or_else(|e| {
        ephyra::signature("standard_multiformat_bad_zip");
        panic!("Bad zip: {e}");
    });
    assert!(entries.len() >= usize::from(c.count));

    let stem = zip_path.file_stem().unwrap().to_str().unwrap();
    let mut archive = zip::ZipArchive::new(fs::File::open(&zip_path).unwrap()).unwrap();
    let mut read_load_file = |extension: &str| {
        let name = format!("{stem}.{extension}");
        if c.include_load_file {
            let mut content = Vec::new();
            archive
                .by_name(&name)
                .expect(&name)
                .read_to_end(&mut content)
                .unwrap();
            content
        } else {
            assert!(!entries.contains(&name), "Unexpected Archive entry {name}");
            fs::read(out_dir.join(&name)).expect(&name)
        }
    };
    let concordance =
        String::from_utf8(read_load_file("concordance.dat")).expect("UTF-8 Concordance");
    assert!(concordance.lines().next().unwrap().contains("BEGATTY"));
    for extension in ["dat", "opt"] {
        assert!(!read_load_file(extension).is_empty(), "empty {extension}");
    }
    let csv_raw = String::from_utf8(read_load_file("csv")).expect("UTF-8 CSV");
    let csv_trimmed = csv_raw.strip_prefix('\u{feff}').unwrap_or(&csv_raw);
    let mut rdr = csv::ReaderBuilder::new()
        .has_headers(true)
        .from_reader(csv_trimmed.as_bytes());
    let hdr_len = rdr.headers().expect("csv headers").len();
    let mut csv_rows = 0usize;
    for rec in rdr.records() {
        let rec = rec.unwrap_or_else(|e| {
            ephyra::signature("standard_csv_parse_fail");
            panic!("Standard CSV parse error: {e}");
        });
        assert_eq!(rec.len(), hdr_len);
        csv_rows += 1;
    }
    assert!(csv_rows >= usize::from(c.count));

    let xml_text = String::from_utf8(read_load_file("xml")).expect("UTF-8 XML");
    let doc = roxmltree::Document::parse(&xml_text).unwrap_or_else(|e| {
        ephyra::signature("standard_xml_parse_fail");
        panic!("Invalid EDRM-XML: {e}");
    });
    let doc_nodes = doc
        .descendants()
        .filter(|n| n.has_tag_name("Document"))
        .count();
    assert!(doc_nodes >= usize::from(c.count));
}

#[ephyra::test(gen = production_deep_cases(), tests = 40, failures = 10)]
fn prop_production_deep_invariants(c: ProductionDeepCase) {
    let dir = tempfile::tempdir().expect("tempdir");
    let out_dir = dir.path().join("prod");
    let mut args = vec![
        "--production-set".to_string(),
        "--type".to_string(),
        c.doc_type.flag_value().to_string(),
        "--count".to_string(),
        c.count.to_string(),
        "--volume-size".to_string(),
        c.volume_size.to_string(),
        "--output-path".to_string(),
        out_dir.to_string_lossy().into_owned(),
        "--seed".to_string(),
        c.seed.to_string(),
    ];
    if c.two_rolling {
        args.extend([
            "--rolling-count".into(),
            "2".into(),
            "--bates-prefix".into(),
            "ROLLA,ROLLB".into(),
            "--bates-start".into(),
            "1,100".into(),
            "--production-id".into(),
            "SET_ONE,SET_TWO".into(),
            "--rolling-bates-mode".into(),
            if c.restart_bates {
                "restart"
            } else {
                "continuous"
            }
            .into(),
        ]);
    } else {
        args.extend([
            "--bates-prefix".into(),
            "PROD".into(),
            "--bates-start".into(),
            "1".into(),
        ]);
    }
    if c.with_text {
        args.push("--with-text".into());
    }
    if c.with_families {
        args.push("--with-families".into());
    }
    if c.attachment_rate > 0 {
        args.push("--attachment-rate".into());
        args.push(c.attachment_rate.to_string());
    }

    let out = run_zipper(dir.path(), &args);
    assert_no_crash(&out, &format!("production_deep {args:?}"));
    if out.code != Some(0) {
        ephyra::signature("production_deep_nonzero");
        panic!(
            "Production deep failed (exit {:?}): args={args:?}\nstderr:\n{}",
            out.code, out.stderr
        );
    }

    let set_dirs: Vec<_> = fs::read_dir(&out_dir)
        .expect("read prod out")
        .map(|entry| entry.expect("read production entry").path())
        .filter(|path| path.is_dir())
        .collect();
    assert_eq!(set_dirs.len(), if c.two_rolling { 2 } else { 1 });
    if c.two_rolling {
        assert!(set_dirs.contains(&out_dir.join("SET_ONE")));
        assert!(set_dirs.contains(&out_dir.join("SET_TWO")));
    }

    for set_dir in &set_dirs {
        let text_root = set_dir.join("TEXT");
        let has_text_file = fs::read_dir(&text_root)
            .expect("TEXT directory")
            .any(|entry| {
                let path = entry.expect("TEXT entry").path();
                path.is_file()
                    || (path.is_dir()
                        && fs::read_dir(path)
                            .expect("TEXT volume")
                            .any(|file| file.expect("TEXT volume entry").path().is_file()))
            });
        assert!(
            has_text_file,
            "Missing text files under {}",
            text_root.display()
        );
        let dat_path = set_dir.join("DATA/loadfile.dat");
        assert!(dat_path.exists(), "Missing {}", dat_path.display());
        let raw = fs::read(&dat_path).expect("read loadfile.dat");
        let text = decode_loadfile_bytes(&raw, EncodingKind::Utf8).expect("decode utf8");
        let rows = parse_delimited_records(&text, '\x14', Some('\u{fe}')).expect("parse dat");
        assert!(rows.len() >= 1 + usize::from(c.count));
        let hdr = &rows[0];
        let native_idx = hdr.iter().position(|h| h == "NATIVE_PATH");
        let text_idx = hdr.iter().position(|h| h == "TEXT_PATH");
        let img_idx = hdr.iter().position(|h| h == "IMAGE_PATH");

        for row in &rows[1..] {
            assert_eq!(row.len(), hdr.len());
            for idx_opt in [native_idx, text_idx, img_idx] {
                if let Some(idx) = idx_opt {
                    let rel = row[idx].trim();
                    if !rel.is_empty() {
                        let norm = rel.replace('\\', "/");
                        let full = set_dir.join(&norm);
                        if !full.exists() {
                            ephyra::signature("production_missing_referenced_file");
                            panic!("DAT referenced missing file: {}", full.display());
                        }
                    }
                }
            }
        }
    }
}
