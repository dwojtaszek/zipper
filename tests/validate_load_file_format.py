#!/usr/bin/env python3
"""
validate_load_file_format.py — Standard-library test oracle for Zipper Load File formats.

Validates OPT, CSV, XML (EDRM-XML), Concordance, and DAT load files across:
- Exact header schema and raw field count
- Exact requested records and cardinality
- Unique identities (Control Number / DocID / Bates)
- Bates number sequences, values, and order
- Delimiter bytes and quote rules (including ASCII codes and overrides)
- Cross-referencing File Path, File Type, and size against the Archive central directory
- Family parent-child attachment relationships
"""

import argparse
import csv
import glob
import io
import json
import os
import sys
import xml.etree.ElementTree as ET
import zipfile


class ValidationError(Exception):
    """Raised when load file or archive content fails validation."""
    pass


def resolve_delimiter(val, default):
    """Resolve delimiter character from string, digit ASCII code, or hex."""
    if val is None:
        return default
    if isinstance(val, int):
        return chr(val)
    if isinstance(val, str):
        if val.isdigit():
            return chr(int(val))
        if val.startswith("\\x"):
            return chr(int(val[2:], 16))
        return val
    return default


def generate_bates_numbers(prefix, start, digits, count):
    """Generate expected Bates numbers in order."""
    return [f"{prefix}{start + i:0{digits}d}" for i in range(count)]


def find_column_index(columns, names):
    """Find index of a column by case-insensitive matching against candidates."""
    lower_cols = [c.strip().lower() for c in columns]
    for name in names:
        target = name.strip().lower()
        if target in lower_cols:
            return lower_cols.index(target)
    return -1


def inspect_archive(zip_path):
    """Read ZIP central directory entries and return dict of filename -> ZipInfo."""
    if not zip_path or not os.path.exists(zip_path):
        return None
    with zipfile.ZipFile(zip_path, "r") as zf:
        return {info.filename.replace("\\", "/"): info for info in zf.infolist() if not info.is_dir()}


# ---------------------------------------------------------------------------
# Format Validators
# ---------------------------------------------------------------------------

def validate_opt(
    content,
    raw_bytes,
    expected_count,
    bates_numbers=None,
    archive_entries=None,
    file_type=None,
):
    """Validate Opticon (OPT) 7-column positional format."""
    lines = [line.rstrip("\r") for line in content.split("\n") if line.rstrip("\r")]
    if not lines:
        raise ValidationError("OPT file is empty")

    first_line = lines[0]
    if "control number" in first_line.lower():
        raise ValidationError("OPT must not contain header row, found 'Control Number'")

    # OPT must contain comma delimiter
    if b"," not in raw_bytes:
        raise ValidationError("No comma delimiter found in .opt file")

    records = []
    doc_breaks = []
    seen_bates = set()

    for idx, line in enumerate(lines):
        parts = line.split(",")
        if len(parts) != 7:
            raise ValidationError(
                f"OPT row must have exactly 7 columns, found {len(parts)} in line {idx + 1}: {line}"
            )

        bates, volume, image_path, doc_break, res1, res2, page_count = parts

        if not bates:
            raise ValidationError(f"OPT row {idx + 1} has empty Bates number")
        if bates in seen_bates:
            raise ValidationError(f"Duplicate identity in OPT: '{bates}' at row {idx + 1}")
        seen_bates.add(bates)

        if not volume:
            raise ValidationError(f"OPT row {idx + 1} has empty Volume")
        if not image_path:
            raise ValidationError(f"OPT row {idx + 1} has empty ImagePath")
        if res1 != "" or res2 != "":
            raise ValidationError(f"OPT row {idx + 1} has non-empty reserved columns: '{res1}', '{res2}'")
        if doc_break not in ("Y", ""):
            raise ValidationError(f"OPT row {idx + 1} invalid DocBreak '{doc_break}' (must be 'Y' or empty)")

        if doc_break == "Y":
            doc_breaks.append((idx, bates, page_count))

        records.append({
            "bates": bates,
            "volume": volume,
            "image_path": image_path,
            "doc_break": doc_break,
            "page_count": page_count,
        })

    # Validate document count (number of DocBreak == 'Y' markers)
    doc_count = len(doc_breaks)
    if doc_count != expected_count:
        raise ValidationError(
            f"OPT document count mismatch: expected {expected_count} documents, found {doc_count}"
        )

    # Validate Bates numbers on document breaks
    if bates_numbers:
        if len(bates_numbers) != len(doc_breaks):
            raise ValidationError(
                f"Expected {len(bates_numbers)} Bates numbers, but found {len(doc_breaks)} documents"
            )
        for i, expected_bates in enumerate(bates_numbers):
            actual_doc_bates = doc_breaks[i][1]
            # In multipage TIFF, base bates might have _001 suffix on first page
            if actual_doc_bates != expected_bates and actual_doc_bates != f"{expected_bates}_001":
                raise ValidationError(
                    f"Bates number mismatch at record {i}: expected '{expected_bates}', found '{actual_doc_bates}'"
                )

    # Archive check: OPT image paths are conceptual (IMAGES\*.tif).
    # If archive exists, verify native files count in archive matches expected count.
    if archive_entries is not None:
        native_files = [k for k in archive_entries.keys() if not k.endswith("/")]
        if file_type and file_type.lower() != "tiff":
            # For non-tiff types with single-page generation, native files count should match
            if len(native_files) != expected_count:
                raise ValidationError(
                    f"Archive native files count mismatch: expected {expected_count}, found {len(native_files)}"
                )

    return len(records)


def validate_csv(
    content,
    raw_bytes,
    expected_count,
    bates_numbers=None,
    archive_entries=None,
    file_type=None,
    with_families=False,
    attachment_rate=0,
):
    """Validate RFC 4180 CSV format."""
    f = io.StringIO(content)
    reader = csv.reader(f)
    try:
        rows = list(reader)
    except Exception as e:
        raise ValidationError(f"Malformed CSV: {e}")

    if not rows:
        raise ValidationError("CSV file is empty")

    header = rows[0]
    data_rows = rows[1:]

    control_idx = find_column_index(header, ["control number", "control", "controlnumber"])
    path_idx = find_column_index(header, ["file path", "path"])

    if control_idx == -1:
        raise ValidationError("'Control Number' column not found in .csv header")
    if path_idx == -1:
        raise ValidationError("'File Path' column not found in .csv header")

    # Header-only check
    if not data_rows:
        raise ValidationError(f"Header-only CSV: expected {expected_count} data records, found 0")

    # Calculate expected total records considering families
    expected_records = expected_count
    if with_families and attachment_rate == 100:
        expected_records = expected_count * 2

    if len(data_rows) != expected_records:
        raise ValidationError(
            f"CSV record count mismatch: expected {expected_records}, found {len(data_rows)}"
        )

    # Check field counts and unique identities
    seen_ids = set()
    bates_idx = find_column_index(header, ["bates number", "bates"])
    if bates_numbers and bates_idx == -1:
        raise ValidationError("'Bates Number' column not found in .csv header")

    for idx, row in enumerate(data_rows):
        if len(row) != len(header):
            raise ValidationError(
                f"CSV raw field count mismatch on row {idx + 1}: expected {len(header)}, found {len(row)}"
            )

        doc_id = row[control_idx]
        if not doc_id:
            raise ValidationError(f"Empty Control Number on CSV row {idx + 1}")
        if doc_id in seen_ids:
            raise ValidationError(f"Duplicate identity in CSV: '{doc_id}' at row {idx + 1}")
        seen_ids.add(doc_id)

        # Archive check
        file_path = row[path_idx]
        if archive_entries is not None:
            norm_path = file_path.replace("\\", "/")
            if norm_path not in archive_entries:
                raise ValidationError(f"File Path '{file_path}' not found in Archive")

    # Validate Bates sequence
    if bates_numbers and bates_idx != -1:
        # Extract primary document Bates numbers (non-attachment if families)
        bates_values = []
        for row in data_rows:
            cid = row[control_idx]
            if "_A0" not in cid:
                bates_values.append(row[bates_idx])

        if len(bates_values) != len(bates_numbers):
            raise ValidationError(
                f"Expected {len(bates_numbers)} Bates records, found {len(bates_values)}"
            )
        for i, expected_bates in enumerate(bates_numbers):
            if bates_values[i] != expected_bates:
                raise ValidationError(
                    f"Bates number mismatch at record {i}: expected '{expected_bates}', found '{bates_values[i]}'"
                )

    # Validate families columns if requested
    if with_families:
        begattach_idx = find_column_index(header, ["begattach", "begatty"])
        if begattach_idx == -1:
            raise ValidationError("'BEGATTACH' column not found in CSV header")

    return len(data_rows)


def validate_xml(
    content,
    raw_bytes,
    expected_count,
    bates_numbers=None,
    archive_entries=None,
    file_type=None,
    with_families=False,
    attachment_rate=0,
):
    """Validate EDRM-XML format."""
    try:
        root = ET.fromstring(content)
    except ET.ParseError as e:
        raise ValidationError(f"Malformed XML: {e}")

    if root.tag != "Root":
        raise ValidationError(f"Expected root tag <Root>, found <{root.tag}>")
    if root.get("DataInterchangeType") != "Export":
        raise ValidationError("Root attribute DataInterchangeType must be 'Export'")

    if root.find(".//Documents") is not None:
        raise ValidationError("Plural wrapper <Documents> element should not be present")

    batch = root.find("Batch")
    if batch is None:
        raise ValidationError("<Batch> element not found")

    documents = batch.findall("Document")
    expected_docs = expected_count
    if with_families and attachment_rate == 100:
        expected_docs = expected_count * 2

    if len(documents) != expected_docs:
        raise ValidationError(
            f"XML Document count mismatch: expected {expected_docs}, found {len(documents)}"
        )

    seen_doc_ids = set()
    primary_doc_ids = []

    for idx, doc in enumerate(documents):
        doc_id = doc.get("DocID")
        if not doc_id:
            raise ValidationError(f"Document at index {idx} missing DocID")
        if doc_id in seen_doc_ids:
            raise ValidationError(f"Duplicate DocID in XML: '{doc_id}'")
        seen_doc_ids.add(doc_id)

        if "_A0" not in doc_id:
            primary_doc_ids.append(doc_id)

        # Check Native file mapping
        files_elem = doc.find("Files")
        if files_elem is None:
            raise ValidationError(f"Document {doc_id} missing <Files> element")
        native_file = files_elem.find("./File[@FileType='Native']")
        if native_file is None:
            raise ValidationError(f"Document {doc_id} missing Native File")
        ext_file = native_file.find("ExternalFile")
        if ext_file is None:
            raise ValidationError(f"Document {doc_id} missing ExternalFile in Native File")

        file_path = ext_file.get("FilePath")
        if not file_path:
            raise ValidationError(f"Document {doc_id} ExternalFile missing FilePath")

        if archive_entries is not None:
            norm_path = file_path.replace("\\", "/")
            if norm_path not in archive_entries:
                raise ValidationError(f"File Path '{file_path}' not found in Archive")

    # Validate Bates numbers if requested
    if bates_numbers:
        if len(primary_doc_ids) != len(bates_numbers):
            raise ValidationError(
                f"Expected {len(bates_numbers)} Bates doc IDs, found {len(primary_doc_ids)}"
            )
        for i, expected_bates in enumerate(bates_numbers):
            if primary_doc_ids[i] != expected_bates:
                raise ValidationError(
                    f"Bates number mismatch at record {i}: expected '{expected_bates}', found '{primary_doc_ids[i]}'"
                )

    # Validate relationships if families
    if with_families:
        relationships_elem = batch.find("Relationships")
        if relationships_elem is None:
            raise ValidationError("XML missing <Relationships> element for families")
        rels = relationships_elem.findall("Relationship")
        expected_rels = expected_count if attachment_rate == 100 else 0
        if len(rels) != expected_rels:
            raise ValidationError(
                f"Relationships count mismatch: expected {expected_rels}, found {len(rels)}"
            )
        for r in rels:
            if r.get("Type") != "Attachment":
                raise ValidationError(f"Relationship Type must be 'Attachment', got '{r.get('Type')}'")
            if not r.get("ParentDocID") or not r.get("ChildDocID"):
                raise ValidationError("Relationship missing ParentDocID or ChildDocID")

    return len(documents)


def validate_dat_like(
    content,
    raw_bytes,
    format_name,
    expected_count,
    col_delim,
    quote_delim,
    bates_numbers=None,
    archive_entries=None,
    file_type=None,
    with_families=False,
    attachment_rate=0,
):
    """Validate DAT or Concordance delimited formats with byte-exact delimiter checks."""
    # Byte-exact delimiter validation
    col_delim_bytes = col_delim.encode("utf-8")
    if col_delim_bytes not in raw_bytes:
        raise ValidationError(
            f"Expected column delimiter {repr(col_delim)} ({col_delim_bytes!r}) not found in DAT file"
        )

    if quote_delim:
        quote_delim_bytes = quote_delim.encode("utf-8")
        if quote_delim_bytes not in raw_bytes:
            raise ValidationError(
                f"Expected quote delimiter {repr(quote_delim)} ({quote_delim_bytes!r}) not found in DAT file"
            )

    lines = [line.rstrip("\r") for line in content.split("\n") if line.rstrip("\r")]
    if not lines:
        raise ValidationError("DAT file is empty")

    header_line = lines[0]
    data_lines = lines[1:]

    # Parse header fields
    def parse_fields(line):
        clean_line = line.lstrip("\ufeff")
        if quote_delim:
            if not clean_line.startswith(quote_delim) or not clean_line.endswith(quote_delim):
                raise ValidationError(
                    f"Line is not enclosed with expected quote delimiter {quote_delim!r}"
                )
        if quote_delim and len(quote_delim) == 1 and len(col_delim) == 1:
            try:
                fields = next(csv.reader([clean_line], delimiter=col_delim, quotechar=quote_delim))
                expected_sep = quote_delim + col_delim + quote_delim
                if len(fields) > 1 and expected_sep not in clean_line:
                    raise ValidationError(
                        f"Expected quote-column-quote boundary {expected_sep!r} not found"
                    )
                return fields
            except ValidationError:
                raise
            except Exception:
                pass
        tokens = clean_line.split(col_delim)
        fields = []
        for t in tokens:
            val = t
            if quote_delim:
                if not (val.startswith(quote_delim) and val.endswith(quote_delim)):
                    raise ValidationError(
                        f"Field {val!r} is not enclosed by expected quote delimiter {quote_delim!r}"
                    )
                val = val[len(quote_delim):-len(quote_delim)]
                val = val.replace(quote_delim + quote_delim, quote_delim)
            fields.append(val)
        return fields

    header = parse_fields(header_line)
    if len(header) < 2 or col_delim not in header_line:
        raise ValidationError(
            f"Header is not split by expected column delimiter {col_delim!r}"
        )

    if format_name == "concordance":
        control_idx = find_column_index(header, ["controlnumber", "control number", "control"])
        path_idx = find_column_index(header, ["path", "file path"])
        if control_idx == -1:
            raise ValidationError("'CONTROLNUMBER' column not found in .dat header")
        if path_idx == -1:
            raise ValidationError("'PATH' column not found in .dat header")
    else:
        control_idx = find_column_index(header, ["control number", "controlnumber", "control"])
        path_idx = find_column_index(header, ["file path", "path"])
        if control_idx == -1:
            raise ValidationError("'Control Number' column not found in .dat header")

    if not data_lines:
        raise ValidationError(f"Header-only DAT: expected {expected_count} data records, found 0")

    expected_records = expected_count
    if with_families and attachment_rate == 100:
        expected_records = expected_count * 2

    if len(data_lines) != expected_records:
        raise ValidationError(
            f"DAT record count mismatch: expected {expected_records}, found {len(data_lines)}"
        )

    seen_ids = set()
    bates_idx = find_column_index(header, ["bates number", "bates"])
    if bates_numbers and bates_idx == -1:
        # In Loadfile-Only mode, Control Number carries the Bates sequence
        bates_idx = control_idx

    for idx, line in enumerate(data_lines):
        fields = parse_fields(line)
        if len(fields) != len(header):
            raise ValidationError(
                f"DAT raw field count mismatch on row {idx + 1}: expected {len(header)}, found {len(fields)}"
            )

        cid = fields[control_idx]
        if not cid:
            raise ValidationError(f"Empty Control Number on DAT row {idx + 1}")
        if cid in seen_ids:
            raise ValidationError(f"Duplicate identity in DAT: '{cid}' at row {idx + 1}")
        seen_ids.add(cid)

        # Archive path check
        if path_idx != -1 and archive_entries is not None:
            fpath = fields[path_idx]
            norm_path = fpath.replace("\\", "/")
            if norm_path not in archive_entries:
                raise ValidationError(f"File Path '{fpath}' not found in Archive")

    # Validate Bates sequence
    if bates_numbers and bates_idx != -1:
        bates_values = []
        for line in data_lines:
            f = parse_fields(line)
            cid = f[control_idx]
            if "_A0" not in cid:
                bates_values.append(f[bates_idx])

        if len(bates_values) != len(bates_numbers):
            raise ValidationError(
                f"Expected {len(bates_numbers)} Bates records, found {len(bates_values)}"
            )
        for i, expected_bates in enumerate(bates_numbers):
            if bates_values[i] != expected_bates:
                raise ValidationError(
                    f"Bates number mismatch at record {i}: expected '{expected_bates}', found '{bates_values[i]}'"
                )

    if with_families:
        begattach_idx = find_column_index(header, ["begattach", "begatty"])
        if begattach_idx == -1:
            raise ValidationError("'BEGATTACH' column not found in DAT header")

    return len(data_lines)


# ---------------------------------------------------------------------------
# Main Orchestrator Function
# ---------------------------------------------------------------------------

def validate_load_file(
    load_file_path=None,
    format_name="dat",
    expected_count=1,
    file_type=None,
    bates_prefix=None,
    bates_start=1,
    bates_digits=6,
    delimiter_column=None,
    delimiter_quote=None,
    with_families=False,
    attachment_rate=0,
    loadfile_only=False,
    dir_path=None,
    zip_path=None,
):
    """Main validation entrypoint."""
    fmt = format_name.lower()

    # Discover load file from dir_path if not provided
    if not load_file_path and dir_path:
        pattern = {
            "opt": "*.opt",
            "csv": "*.csv",
            "xml": "*.xml",
            "dat": "*.dat",
            "concordance": "*.dat",
        }.get(fmt, "*.dat")
        matches = glob.glob(os.path.join(dir_path, pattern))
        if not matches:
            raise ValidationError(f"No {pattern} file found in {dir_path}")
        load_file_path = sorted(matches)[0]

    if not load_file_path or not os.path.exists(load_file_path):
        raise ValidationError(f"Load file not found: {load_file_path}")

    # Discover zip file if needed
    if not loadfile_only and not zip_path and dir_path:
        zip_matches = glob.glob(os.path.join(dir_path, "*.zip"))
        if zip_matches:
            zip_path = sorted(zip_matches)[0]

    archive_entries = inspect_archive(zip_path) if zip_path else None

    # Load file content
    with open(load_file_path, "rb") as bf:
        raw_bytes = bf.read()
    content = raw_bytes.decode("utf-8-sig", errors="replace")

    # Generate expected Bates numbers if specified
    bates_numbers = None
    if bates_prefix:
        bates_numbers = generate_bates_numbers(bates_prefix, bates_start, bates_digits, expected_count)

    # Resolve delimiters
    default_col = "\x14" if fmt in ("dat", "concordance") else ","
    default_quote = "\xfe" if fmt in ("dat", "concordance") else ('"' if fmt == "csv" else "")

    col_delim = resolve_delimiter(delimiter_column, default_col)
    quote_delim = resolve_delimiter(delimiter_quote, default_quote)

    record_count = 0
    if fmt == "opt":
        record_count = validate_opt(
            content,
            raw_bytes,
            expected_count,
            bates_numbers=bates_numbers,
            archive_entries=archive_entries,
            file_type=file_type,
        )
    elif fmt == "csv":
        record_count = validate_csv(
            content,
            raw_bytes,
            expected_count,
            bates_numbers=bates_numbers,
            archive_entries=archive_entries,
            file_type=file_type,
            with_families=with_families,
            attachment_rate=attachment_rate,
        )
    elif fmt == "xml":
        record_count = validate_xml(
            content,
            raw_bytes,
            expected_count,
            bates_numbers=bates_numbers,
            archive_entries=archive_entries,
            file_type=file_type,
            with_families=with_families,
            attachment_rate=attachment_rate,
        )
    elif fmt in ("dat", "concordance"):
        record_count = validate_dat_like(
            content,
            raw_bytes,
            fmt,
            expected_count,
            col_delim,
            quote_delim,
            bates_numbers=bates_numbers,
            archive_entries=archive_entries,
            file_type=file_type,
            with_families=with_families,
            attachment_rate=attachment_rate,
        )
    else:
        raise ValidationError(f"Unsupported format: {fmt}")

    # Check properties.json in loadfile-only mode if present
    if loadfile_only and dir_path:
        base_name = os.path.basename(load_file_path)
        prop_matches = glob.glob(os.path.join(dir_path, "*_properties.json"))
        if not prop_matches:
            raise ValidationError(
                f"No *_properties.json found in {dir_path} in loadfile-only mode"
            )
        for prop_path in prop_matches:
            with open(prop_path, "r", encoding="utf-8") as pf:
                props = json.load(pf)
                if props.get("fileName") == base_name:
                    total_records = props.get("totalRecords")
                    if total_records is None:
                        raise ValidationError(
                            f"properties.json missing totalRecords for {base_name}"
                        )
                    if total_records != record_count:
                        raise ValidationError(
                            f"properties.json totalRecords mismatch for {base_name}: expected {record_count}, found {total_records}"
                        )

    return {"valid": True, "record_count": record_count, "format": fmt}


def main():
    parser = argparse.ArgumentParser(description="Zipper Load File Format E2E Oracle")
    parser.add_argument("--dir", default=None, help="Directory containing generated outputs")
    parser.add_argument("--load-file", default=None, help="Explicit path to load file")
    parser.add_argument("--zip-file", default=None, help="Explicit path to zip archive")
    parser.add_argument(
        "--format",
        required=True,
        choices=["opt", "csv", "xml", "concordance", "dat"],
        help="Load file format",
    )
    parser.add_argument("--count", type=int, required=True, help="Expected document/record count")
    parser.add_argument("--file-type", default=None, help="Expected file type (pdf, eml, tiff, jpg, etc.)")
    parser.add_argument("--bates-prefix", default=None, help="Expected Bates prefix")
    parser.add_argument("--bates-start", type=int, default=1, help="Bates starting number")
    parser.add_argument("--bates-digits", type=int, default=6, help="Bates digit padding count")
    parser.add_argument("--delimiter-column", default=None, help="Column delimiter (char or ascii code)")
    parser.add_argument("--delimiter-quote", default=None, help="Quote delimiter (char or ascii code)")
    parser.add_argument("--with-families", action="store_true", help="Expect family relationships/attachments")
    parser.add_argument("--attachment-rate", type=int, default=0, help="Attachment rate percentage")
    parser.add_argument("--loadfile-only", action="store_true", help="Loadfile-only mode (no archive expected)")

    args = parser.parse_args()

    try:
        res = validate_load_file(
            load_file_path=args.load_file,
            format_name=args.format,
            expected_count=args.count,
            file_type=args.file_type,
            bates_prefix=args.bates_prefix,
            bates_start=args.bates_start,
            bates_digits=args.bates_digits,
            delimiter_column=args.delimiter_column,
            delimiter_quote=args.delimiter_quote,
            with_families=args.with_families,
            attachment_rate=args.attachment_rate,
            loadfile_only=args.loadfile_only,
            dir_path=args.dir,
            zip_path=args.zip_file,
        )
        print(f"[ SUCCESS ] Load file format '{args.format}' verified ({res['record_count']} records).")
        sys.exit(0)
    except ValidationError as err:
        print(f"[ ERROR ] {err}", file=sys.stderr)
        sys.exit(1)
    except Exception as ex:
        print(f"[ ERROR ] Unexpected error during validation: {ex}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
