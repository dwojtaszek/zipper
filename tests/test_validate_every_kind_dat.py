#!/usr/bin/env python3
"""
Regression tests for validate_every_kind_dat.py.

Each test mutates one aspect of otherwise-valid DAT content and confirms
the oracle exits with a specific diagnostic.  Valid content must pass.

Run with: python3 -m unittest tests/test_validate_every_kind_dat.py
"""
import io
import sys
import os
import unittest

# Allow importing from the same directory
sys.path.insert(0, os.path.dirname(__file__))
import validate_every_kind_dat as oracle

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

COL_SEP = "\u0014"
QUOTE = "\u00fe"

HEADERS = [
    "DOCID", "TEXTFIELD", "LONGTEXTFIELD", "DATEFIELD", "DATETIMEFIELD",
    "NUMBERFIELD", "NUMBERGAUSSIAN", "NUMBEREXPONENTIAL", "NUMBERPARETO",
    "BOOLEANFIELD", "CODEDFIELD", "CODEDWEIGHTED", "EMAILFIELD", "EMAILMULTI",
]

VALID_CODED = ["Active", "Inactive", "Pending", "Closed", "Archived"]
VALID_WEIGHTED = ["CategoryA", "CategoryB", "CategoryC", "CategoryD"]

FIXTURE_SPEC = oracle.FIXTURE_SPEC


def _q(v: str) -> str:
    """Wrap a field value in DAT quoting characters."""
    return f"{QUOTE}{v}{QUOTE}"


def _make_row(n: int, **overrides) -> dict:
    """Return a dict representing a valid data row."""
    defaults = {
        "DOCID": f"DOC{n}",
        "TEXTFIELD": "some text",
        "LONGTEXTFIELD": "some long text value here",
        "DATEFIELD": "2021-06-15",
        "DATETIMEFIELD": "2021-06-15T12:00:00Z",
        "NUMBERFIELD": "5000",
        "NUMBERGAUSSIAN": "5000",
        "NUMBEREXPONENTIAL": "2500",
        "NUMBERPARETO": "25000",
        "BOOLEANFIELD": "Y",
        "CODEDFIELD": "Active",
        "CODEDWEIGHTED": "CategoryA",
        "EMAILFIELD": "user@example.com",
        "EMAILMULTI": "a@b.com;c@d.com",
    }
    defaults.update(overrides)
    return defaults


def _build_dat(rows: list[dict], headers: list[str] = None) -> str:
    """Serialise rows (list of dicts) to a DAT string."""
    if headers is None:
        headers = HEADERS
    header_line = COL_SEP.join(_q(h) for h in headers)
    lines = [header_line]
    for row in rows:
        fields = [_q(row.get(h, "")) for h in headers]
        lines.append(COL_SEP.join(fields))
    return "\n".join(lines) + "\n"


def _run(dat_content: str, expected_count: int = 3) -> tuple[bool, str]:
    """
    Run the oracle against dat_content (as a string).
    Returns (passed: bool, message: str).
    """
    f = io.StringIO(dat_content)
    try:
        oracle.validate(f, expected_count=expected_count, spec=FIXTURE_SPEC)
        return True, ""
    except SystemExit as exc:
        return False, str(exc)


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------


class TestValidEveryKindDat(unittest.TestCase):
    """Valid data must pass without errors."""

    def _valid_rows(self, n=3):
        return [_make_row(i + 1) for i in range(n)]

    def test_valid_data_passes(self):
        dat = _build_dat(self._valid_rows())
        passed, msg = _run(dat, expected_count=3)
        self.assertTrue(passed, f"Valid data should pass but got: {msg}")

    def test_valid_data_with_many_rows(self):
        dat = _build_dat(self._valid_rows(10))
        passed, msg = _run(dat, expected_count=10)
        self.assertTrue(passed, f"Valid 10-row data should pass but got: {msg}")


class TestRowCountValidation(unittest.TestCase):
    """Exact row count must be enforced."""

    def test_too_few_rows_fails(self):
        # Only 2 rows when 3 expected
        dat = _build_dat([_make_row(1), _make_row(2)])
        passed, msg = _run(dat, expected_count=3)
        self.assertFalse(passed)
        self.assertIn("2", msg)

    def test_too_many_rows_fails(self):
        # 4 rows when 3 expected
        dat = _build_dat([_make_row(i) for i in range(1, 5)])
        passed, msg = _run(dat, expected_count=3)
        self.assertFalse(passed)
        self.assertIn("4", msg)

    def test_blank_rows_not_counted(self):
        """Blank lines between data rows should be ignored, not counted."""
        rows = [_make_row(1), _make_row(2), _make_row(3)]
        dat = _build_dat(rows)
        # Inject a blank line in the middle
        lines = dat.split("\n")
        lines.insert(2, "")
        dat_with_blank = "\n".join(lines)
        passed, msg = _run(dat_with_blank, expected_count=3)
        self.assertTrue(passed, f"Blank lines should be ignored: {msg}")


class TestFieldCountValidation(unittest.TestCase):
    """Raw field count before header-mapping must catch surplus/missing columns."""

    def test_surplus_field_fails(self):
        """A row with 15 fields (one extra) must fail before header mapping."""
        rows = [_make_row(1)]
        # Build a row with an extra field appended
        header_line = COL_SEP.join(_q(h) for h in HEADERS)
        fields = [_q(rows[0].get(h, "")) for h in HEADERS] + [_q("extra")]
        data_line = COL_SEP.join(fields)
        dat = header_line + "\n" + data_line + "\n"
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)
        self.assertIn("15", msg)

    def test_missing_field_fails(self):
        """A row with 13 fields (one missing) must fail."""
        rows = [_make_row(1)]
        header_line = COL_SEP.join(_q(h) for h in HEADERS)
        # Drop last field
        fields = [_q(rows[0].get(h, "")) for h in HEADERS[:-1]]
        data_line = COL_SEP.join(fields)
        dat = header_line + "\n" + data_line + "\n"
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)
        self.assertIn("13", msg)

    def test_duplicate_header_fails(self):
        """Duplicate column names in the header must be rejected."""
        dupe_headers = HEADERS[:-1] + ["DOCID"]  # DOCID appears twice
        dat = _build_dat([_make_row(1)], headers=dupe_headers)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)
        self.assertRegex(msg.lower(), r"duplicate|header")

    def test_wrong_header_order_fails(self):
        """Headers in wrong order must fail."""
        wrong_order = list(reversed(HEADERS))
        dat = _build_dat([_make_row(1)], headers=wrong_order)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)


class TestDocidValidation(unittest.TestCase):

    def test_duplicate_docid_fails(self):
        rows = [_make_row(1), _make_row(1)]  # same DOCID
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=2)
        self.assertFalse(passed)
        self.assertRegex(msg.lower(), r"duplicate|docid")

    def test_invalid_docid_pattern_fails(self):
        rows = [_make_row(1, DOCID="BADID")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)
        self.assertRegex(msg.lower(), r"docid")

    def test_docid_boundary_values(self):
        """First DOCID should be DOC1, last should match count."""
        rows = [_make_row(i + 1) for i in range(3)]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=3)
        self.assertTrue(passed, f"Boundary DOCID check should pass: {msg}")

    def test_docid_out_of_order_fails(self):
        """DOCIDs must be in ascending order DOC1..DOCn."""
        rows = [_make_row(3), _make_row(2), _make_row(1)]  # reversed
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=3)
        self.assertFalse(passed)


class TestDatetimeValidation(unittest.TestCase):

    def test_invalid_datetime_format_fails(self):
        """DATETIMEFIELD containing only 'T' but otherwise malformed must fail."""
        rows = [_make_row(1, DATETIMEFIELD="invalidTdate")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)
        self.assertRegex(msg.lower(), r"datetime|datetimefield")

    def test_missing_T_in_datetime_fails(self):
        """DATETIMEFIELD without 'T' must fail."""
        rows = [_make_row(1, DATETIMEFIELD="2021-06-15 12:00:00")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)

    def test_valid_datetime_passes(self):
        rows = [_make_row(1, DATETIMEFIELD="2021-06-15T12:00:00Z")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertTrue(passed, msg)

    def test_datetime_out_of_range_fails(self):
        """A datetime outside the configured range must fail."""
        rows = [_make_row(1, DATETIMEFIELD="2030-01-01T00:00:00Z")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)


class TestNumericValidation(unittest.TestCase):

    def test_numberfield_out_of_range_fails(self):
        rows = [_make_row(1, NUMBERFIELD="99999")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)
        self.assertRegex(msg.lower(), r"numberfield")

    def test_numberfield_nonnumeric_fails(self):
        rows = [_make_row(1, NUMBERFIELD="garbage")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)

    def test_numbergaussian_out_of_range_fails(self):
        rows = [_make_row(1, NUMBERGAUSSIAN="99999")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)
        self.assertRegex(msg.lower(), r"numbergaussian")

    def test_numbergaussian_nonnumeric_fails(self):
        rows = [_make_row(1, NUMBERGAUSSIAN="garbage")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)

    def test_numberexponential_out_of_range_fails(self):
        rows = [_make_row(1, NUMBEREXPONENTIAL="99999")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)
        self.assertRegex(msg.lower(), r"numberexponential")

    def test_numberexponential_nonnumeric_fails(self):
        rows = [_make_row(1, NUMBEREXPONENTIAL="garbage")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)

    def test_numberpareto_out_of_range_fails(self):
        rows = [_make_row(1, NUMBERPARETO="99999999")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)
        self.assertRegex(msg.lower(), r"numberpareto")

    def test_numberpareto_nonnumeric_fails(self):
        rows = [_make_row(1, NUMBERPARETO="garbage")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)

    def test_all_numeric_fields_valid(self):
        rows = [_make_row(1, NUMBERFIELD="0", NUMBERGAUSSIAN="100",
                          NUMBEREXPONENTIAL="0", NUMBERPARETO="0")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertTrue(passed, msg)


class TestCodedWeightedValidation(unittest.TestCase):

    def test_unknown_weighted_code_fails(self):
        rows = [_make_row(1, CODEDWEIGHTED="garbage")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)
        self.assertRegex(msg.lower(), r"codedweighted")

    def test_valid_weighted_codes_pass(self):
        for code in VALID_WEIGHTED:
            rows = [_make_row(1, CODEDWEIGHTED=code)]
            dat = _build_dat(rows)
            passed, msg = _run(dat, expected_count=1)
            self.assertTrue(passed, f"Code '{code}' should be valid but got: {msg}")


class TestEmailMultiValidation(unittest.TestCase):

    def test_malformed_email_in_multi_fails(self):
        """Each recipient in EMAILMULTI must be a valid email address."""
        rows = [_make_row(1, EMAILMULTI="bad;bad")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)
        self.assertRegex(msg.lower(), r"emailmulti|email")

    def test_single_valid_recipient_in_multi_passes(self):
        """EMAILMULTI with one valid recipient (no ';') must pass."""
        rows = [_make_row(1, EMAILMULTI="user@example.com")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertTrue(passed, msg)

    def test_multiple_valid_recipients_pass(self):
        rows = [_make_row(1, EMAILMULTI="a@b.com;c@d.org;e@f.net")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertTrue(passed, msg)

    def test_malformed_second_recipient_fails(self):
        """Even if first recipient is valid, a bad second must fail."""
        rows = [_make_row(1, EMAILMULTI="a@b.com;notanemail")]
        dat = _build_dat(rows)
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)


class TestEscapedDelimiterAndQuote(unittest.TestCase):
    """Values containing the delimiter or quote character must be handled."""

    def test_text_with_delimiter_in_value(self):
        """TEXTFIELD with COL_SEP inside would look like an extra field — oracle must catch it."""
        # Manually build a row where TEXTFIELD contains the separator unquoted
        header_line = COL_SEP.join(_q(h) for h in HEADERS)
        # Build fields: TEXTFIELD gets the separator char without quoting wrapping it
        # This creates 15 raw fields which should fail field count check
        bad_fields = []
        for h in HEADERS:
            if h == "TEXTFIELD":
                bad_fields.append(f"{QUOTE}part1{QUOTE}{COL_SEP}{QUOTE}part2{QUOTE}")
            else:
                bad_fields.append(_q(_make_row(1).get(h, "")))
        data_line = COL_SEP.join(bad_fields)
        # This line now has one extra separator, so 15 fields
        dat = header_line + "\n" + data_line + "\n"
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)
        self.assertIn("15", msg)


class TestMalformedQuotes(unittest.TestCase):

    def test_unclosed_quote_treated_as_raw_field(self):
        """Unclosed QUOTE at start of field; oracle parses via strip so this is edge case."""
        header_line = COL_SEP.join(_q(h) for h in HEADERS)
        # Build row where one field value has a lone leading QUOTE char (unclosed)
        row = _make_row(1)
        fields = []
        for h in HEADERS:
            v = row.get(h, "")
            if h == "EMAILFIELD":
                # leading QUOTE but no closing QUOTE — still parseable by strip
                fields.append(f"{QUOTE}bad-email-no-at")
            else:
                fields.append(_q(v))
        data_line = COL_SEP.join(fields)
        dat = header_line + "\n" + data_line + "\n"
        passed, msg = _run(dat, expected_count=1)
        self.assertFalse(passed)


if __name__ == "__main__":
    unittest.main()
