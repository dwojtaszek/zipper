#!/usr/bin/env python3
"""Validate generated EML with Python's standard email/MIME parser."""

from __future__ import annotations

import sys
import zipfile
from email import policy
from email.parser import BytesParser
from pathlib import Path


QUOTE = "\xfe"
DELIMITER = "\x14"
MAX_NESTED_DEPTH = 2


def decode_dat_field(value: str) -> str:
    if value.startswith(QUOTE) and value.endswith(QUOTE):
        value = value[1:-1]
    return value.replace(QUOTE + QUOTE, QUOTE)


def read_subjects(dat_path: Path) -> tuple[dict[str, str], list[str]]:
    lines = dat_path.read_text(encoding="utf-8-sig").splitlines()
    if not lines:
        raise AssertionError(f"Load File is empty: {dat_path}")

    headers = [decode_dat_field(value).casefold() for value in lines[0].split(DELIMITER)]
    try:
        path_index = headers.index("file path")
        subject_index = headers.index("subject")
    except ValueError as error:
        raise AssertionError(f"Load File is missing File Path or Subject columns: {headers}") from error

    subjects: dict[str, str] = {}
    paths: list[str] = []
    for line_number, line in enumerate(lines[1:], start=2):
        values = [decode_dat_field(value) for value in line.split(DELIMITER)]
        if len(values) <= max(path_index, subject_index):
            raise AssertionError(f"Load File row {line_number} has too few fields")
        path = values[path_index].replace("\\", "/")
        subjects[path] = values[subject_index]
        paths.append(path)
    return subjects, paths


def nested_depth(message) -> int:
    if not message.is_multipart():
        return 0

    depth = 0
    for part in message.iter_parts():
        if part.get_content_type() == "message/rfc822":
            payload = part.get_payload()
            if not isinstance(payload, list) or not payload:
                raise AssertionError("message/rfc822 part did not contain a parsed message")
            depth = max(depth, 1 + max(nested_depth(child) for child in payload))
        else:
            depth = max(depth, nested_depth(part))
    return depth


def raw_subject_header(message_bytes: bytes) -> bytes:
    header_lines = message_bytes.split(b"\r\n\r\n", 1)[0].split(b"\r\n")
    for index, line in enumerate(header_lines):
        if line.lower().startswith(b"subject:"):
            value = line.split(b":", 1)[1].lstrip()
            continuation = []
            for next_line in header_lines[index + 1 :]:
                if not next_line.startswith((b" ", b"\t")):
                    break
                continuation.append(next_line.strip())
            return b" ".join([value, *continuation])
    raise AssertionError("EML file is missing a Subject header")


def verify_text_payload_lines(parts, path: str) -> None:
    for part in parts:
        if part.get_content_type() not in {"text/plain", "text/html"}:
            continue
        payload = part.get_payload()
        if not isinstance(payload, str):
            raise AssertionError(f"Text part did not have a text payload in {path}")
        if any(len(line) > 76 for line in payload.splitlines()):
            raise AssertionError(f"Encoded text body line exceeded 76 characters in {path}")
        if not isinstance(part.get_content(), str):
            raise AssertionError(f"Text part did not decode to text in {path}")


def normalize_body_line_endings(text: str) -> str:
    # MIME Realism uses canonical CRLF; legacy templates may contain bare LF.
    normalized = text.replace("\r\n", "\n").replace("\r", "\n")
    # Ignore the single terminal line ending used to delimit the MIME body part.
    return normalized[:-1] if normalized.endswith("\n") else normalized


def verify_body_fidelity(
    messages: list[tuple[str, bytes]],
    baseline_messages: list[tuple[str, bytes]],
    mode: str,
) -> None:
    baseline_by_path = dict(baseline_messages)
    for path, contents in messages:
        baseline_contents = baseline_by_path.get(path)
        if baseline_contents is None:
            raise AssertionError(f"No matching non-realism EML file for {path}")

        message = BytesParser(policy=policy.default).parsebytes(contents)
        baseline = BytesParser(policy=policy.default).parsebytes(baseline_contents)
        body = message.get_body(preferencelist=("plain",))
        baseline_body = baseline.get_body(preferencelist=("plain",))
        if body is None or baseline_body is None:
            raise AssertionError(f"Could not find a plain-text body for {path}")
        if normalize_body_line_endings(body.get_content()) != normalize_body_line_endings(
            baseline_body.get_content()
        ):
            raise AssertionError(f"{mode} changed the decoded plain-text body in {path}")


def verify_standard(messages: list[tuple[str, bytes]], subjects: dict[str, str]) -> None:
    encoded_non_ascii_subjects = 0
    alternative_count = 0
    quoted_printable_count = 0
    multi_attachment_count = 0

    for path, contents in messages:
        message = BytesParser(policy=policy.default).parsebytes(contents)
        expected_subject = subjects.get(path)
        if expected_subject is None:
            raise AssertionError(f"No matching Load File record for {path}")
        actual_subject = str(message["Subject"])
        if actual_subject != expected_subject:
            raise AssertionError(
                f"Decoded subject mismatch for {path}: {actual_subject!r} != {expected_subject!r}"
            )

        raw_subject = raw_subject_header(contents)
        if any(ord(character) > 127 for character in expected_subject):
            if b"=?" not in raw_subject:
                raise AssertionError(f"Non-ASCII subject was not RFC 2047 encoded for {path}")
            encoded_non_ascii_subjects += 1

        parts = list(message.walk())
        verify_text_payload_lines(parts, path)
        if any(part.get_content_type() == "multipart/alternative" for part in parts):
            alternative_count += 1
        if any(
            part.get_content_type() in {"text/plain", "text/html"}
            and part.get("Content-Transfer-Encoding", "").casefold() == "quoted-printable"
            for part in parts
        ):
            quoted_printable_count += 1

        top_level_parts = list(message.iter_parts()) if message.is_multipart() else []
        attachments = [
            part
            for part in top_level_parts
            if part.get_content_disposition() == "attachment"
            and part.get_content_type() != "message/rfc822"
        ]
        if len(attachments) >= 2:
            multi_attachment_count += 1

        if nested_depth(message) != 0:
            raise AssertionError(f"Standard mode unexpectedly emitted nested message parts in {path}")

    if encoded_non_ascii_subjects == 0:
        raise AssertionError("No non-ASCII Subject exercised RFC 2047 decoding")
    if alternative_count == 0:
        raise AssertionError("Standard mode did not emit multipart/alternative")
    if quoted_printable_count == 0:
        raise AssertionError("Standard mode did not emit a quoted-printable body")
    if multi_attachment_count == 0:
        raise AssertionError("Standard mode did not emit a message with multiple attachments")


def verify_family_children(
    messages: list[tuple[str, bytes]],
    subjects: dict[str, str],
    record_paths: list[str],
    archive_paths: list[str],
) -> None:
    expected_children = 0
    for path, contents in messages:
        message = BytesParser(policy=policy.default).parsebytes(contents)
        top_level_parts = list(message.iter_parts()) if message.is_multipart() else []
        expected_children += sum(part.get_content_disposition() == "attachment" for part in top_level_parts)

    parent_paths = {path for path in record_paths if subjects.get(path)}
    record_child_paths = {path for path in record_paths if not subjects.get(path)}
    if len(record_child_paths) != expected_children:
        raise AssertionError(
            f"Document Family contains {len(record_child_paths)} Attachment child records, "
            f"but the EML files contain {expected_children} MIME Attachments"
        )
    archive_child_paths = {
        path
        for path in archive_paths
        if not path.casefold().endswith(".txt") and path not in parent_paths
    }
    if archive_child_paths != record_child_paths:
        raise AssertionError(
            "Archive Attachment files do not match the Document Family child paths: "
            f"archive={sorted(archive_child_paths)}, load-file={sorted(record_child_paths)}"
        )
    text_sidecars = sum(path.casefold().endswith(".txt") for path in archive_paths)
    expected_text_sidecars = len(messages) + expected_children
    if text_sidecars != expected_text_sidecars:
        raise AssertionError(
            f"Archive contains {text_sidecars} Extracted Text sidecars, "
            f"but expected {expected_text_sidecars} for the Email documents and MIME Attachments"
        )


def verify_full(messages: list[tuple[str, bytes]]) -> None:
    nested_depths = []
    alternative_count = 0
    multi_attachment_count = 0
    body_transfer_encodings = set()

    for path, contents in messages:
        message = BytesParser(policy=policy.default).parsebytes(contents)
        parts = list(message.walk())
        verify_text_payload_lines(parts, path)
        if any(part.get_content_type() == "multipart/alternative" for part in parts):
            alternative_count += 1

        nested_depths.append(nested_depth(message))
        top_level_parts = list(message.iter_parts()) if message.is_multipart() else []
        attachments = [
            part
            for part in top_level_parts
            if part.get_content_disposition() == "attachment"
            and part.get_content_type() != "message/rfc822"
        ]
        if len(attachments) >= 2:
            multi_attachment_count += 1

        for part in parts:
            if part.get_content_type() in {"text/plain", "text/html"}:
                encoding = part.get("Content-Transfer-Encoding", "").casefold()
                if encoding in {"quoted-printable", "base64"}:
                    body_transfer_encodings.add(encoding)

    if alternative_count == 0:
        raise AssertionError("Full mode did not emit multipart/alternative")
    if multi_attachment_count == 0:
        raise AssertionError("Full mode did not emit a message with multiple attachments")
    if not any(depth > 0 for depth in nested_depths):
        raise AssertionError("Full mode did not emit a nested message/rfc822 part")
    if max(nested_depths, default=0) > MAX_NESTED_DEPTH:
        raise AssertionError(f"Nested message depth exceeded {MAX_NESTED_DEPTH}: {nested_depths}")
    if body_transfer_encodings != {"quoted-printable", "base64"}:
        raise AssertionError(f"Full mode did not emit both body encodings: {body_transfer_encodings}")


def main() -> int:
    if len(sys.argv) not in {5, 6} or sys.argv[1] not in {"standard", "full"}:
        print(
            "Usage: validate_mime_realism.py <standard|full> <archive.zip> <load-file.dat> "
            "<baseline.zip> [--families]",
            file=sys.stderr,
        )
        return 2

    mode = sys.argv[1]
    archive_path = Path(sys.argv[2])
    dat_path = Path(sys.argv[3])
    baseline_path = Path(sys.argv[4])
    if len(sys.argv) == 6 and sys.argv[5] != "--families":
        print("The only supported optional argument is --families.", file=sys.stderr)
        return 2
    with_families = len(sys.argv) == 6
    subjects, record_paths = read_subjects(dat_path)

    with zipfile.ZipFile(archive_path) as archive:
        archive_paths = archive.namelist()
        messages = [
            (name, archive.read(name))
            for name in archive_paths
            if name.casefold().endswith(".eml") and subjects.get(name)
        ]
    with zipfile.ZipFile(baseline_path) as baseline_archive:
        baseline_messages = [
            (name, baseline_archive.read(name))
            for name in baseline_archive.namelist()
            if name.casefold().endswith(".eml")
        ]

    if not messages:
        raise AssertionError(f"Archive contains no EML files: {archive_path}")

    verify_body_fidelity(messages, baseline_messages, mode)
    if mode == "standard":
        verify_standard(messages, subjects)
        if with_families:
            verify_family_children(messages, subjects, record_paths, archive_paths)
    else:
        verify_full(messages)
        if with_families:
            verify_family_children(messages, subjects, record_paths, archive_paths)

    print(
        f"Validated {len(messages)} {mode} EML files with Python's standard MIME parser"
        f"{' and their Document Family Attachments' if with_families else ''}."
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (AssertionError, OSError, UnicodeError, zipfile.BadZipFile) as error:
        print(f"MIME realism verification failed: {error}", file=sys.stderr)
        raise SystemExit(1) from error
