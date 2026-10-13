#!/usr/bin/env python3
"""
Unit and regression tests for validate_load_file_format.py oracle.

Tests all formats (OPT, CSV, XML, Concordance, DAT) across valid and malformed
scenarios, ensuring exact record counts, schema, central-directory archive
cross-referencing, Bates numbering, and delimiter verification.
"""

import io
import json
import os
import sys
import tempfile
import unittest
import zipfile

# Allow importing from tests directory
sys.path.insert(0, os.path.dirname(__file__))
import validate_load_file_format as oracle


class TestValidateLoadFileFormatOracle(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.dir_path = self.temp_dir.name

    def tearDown(self):
        self.temp_dir.cleanup()

    def _create_zip(self, filenames, prefix="folder_001/"):
        zip_path = os.path.join(self.dir_path, "archive_test.zip")
        with zipfile.ZipFile(zip_path, "w") as zf:
            for name in filenames:
                entry_name = f"{prefix}{name}" if prefix and not name.startswith(prefix) else name
                zf.writestr(entry_name, b"dummy content for testing")
        return zip_path

    # --- OPT Tests ---

    def test_valid_opt_format(self):
        """Test OPT validation with native PDF archive members and conceptual image paths.

        Per Issue #1115 Item 3: Do not require conceptual OPT image paths (IMAGES\\*.tif)
        to exist as Native Files where the contract does not promise them.
        """
        opt_path = os.path.join(self.dir_path, "archive.opt")
        lines = [
            f"DOC{i:08d},VOL001,IMAGES\\DOC{i:08d}.tif,Y,,,1\n"
            for i in range(1, 11)
        ]
        with open(opt_path, "w", encoding="utf-8") as f:
            f.writelines(lines)

        # ZIP contains native PDF files; OPT image paths are conceptual per Issue #1115
        self._create_zip([f"{i:08d}.pdf" for i in range(1, 11)])

        result = oracle.validate_load_file(
            load_file_path=opt_path,
            format_name="opt",
            expected_count=10,
            file_type="pdf",
            dir_path=self.dir_path,
        )
        self.assertTrue(result["valid"])
        self.assertEqual(result["record_count"], 10)

    def test_valid_opt_format_tiff(self):
        """Test OPT validation where archive contains native TIFF images."""
        opt_path = os.path.join(self.dir_path, "archive_tiff.opt")
        lines = [
            f"DOC{i:08d},VOL001,IMAGES\\DOC{i:08d}.tif,Y,,,1\n"
            for i in range(1, 6)
        ]
        with open(opt_path, "w", encoding="utf-8") as f:
            f.writelines(lines)

        self._create_zip([f"{i:08d}.tif" for i in range(1, 6)])

        result = oracle.validate_load_file(
            load_file_path=opt_path,
            format_name="opt",
            expected_count=5,
            file_type="tiff",
            dir_path=self.dir_path,
        )
        self.assertTrue(result["valid"])
        self.assertEqual(result["record_count"], 5)

    def test_opt_header_row_rejected(self):
        opt_path = os.path.join(self.dir_path, "archive.opt")
        lines = [
            "Control Number,Volume,Image Path,Doc Break,Res1,Res2,Page Count\n",
            "DOC00000001,VOL001,IMAGES\\DOC00000001.tif,Y,,,1\n",
        ]
        with open(opt_path, "w", encoding="utf-8") as f:
            f.writelines(lines)

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=opt_path,
                format_name="opt",
                expected_count=1,
            )
        self.assertIn("OPT must not contain header row", str(ctx.exception))

    def test_opt_invalid_column_count_rejected(self):
        opt_path = os.path.join(self.dir_path, "archive.opt")
        # Only 2 columns (comma-only line)
        with open(opt_path, "w", encoding="utf-8") as f:
            f.write("DOC00000001,VOL001\n")

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=opt_path,
                format_name="opt",
                expected_count=1,
            )
        self.assertIn("exactly 7 columns", str(ctx.exception))

    # --- CSV Tests ---

    def test_valid_csv_format(self):
        csv_path = os.path.join(self.dir_path, "archive.csv")
        content = (
            "\ufeffCONTROL NUMBER,FILE PATH\n"
            + "\n".join(f"DOC{i:08d},folder_001/{i:08d}.pdf" for i in range(1, 11))
            + "\n"
        )
        with open(csv_path, "w", encoding="utf-8") as f:
            f.write(content)

        self._create_zip([f"{i:08d}.pdf" for i in range(1, 11)])

        result = oracle.validate_load_file(
            load_file_path=csv_path,
            format_name="csv",
            expected_count=10,
            file_type="pdf",
            dir_path=self.dir_path,
        )
        self.assertTrue(result["valid"])
        self.assertEqual(result["record_count"], 10)

    def test_header_only_csv_rejected(self):
        csv_path = os.path.join(self.dir_path, "archive.csv")
        with open(csv_path, "w", encoding="utf-8") as f:
            f.write("CONTROL NUMBER,FILE PATH\n")

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=csv_path,
                format_name="csv",
                expected_count=10,
            )
        self.assertIn("Header-only CSV", str(ctx.exception))

    def test_csv_record_count_mismatch_rejected(self):
        csv_path = os.path.join(self.dir_path, "archive.csv")
        content = (
            "CONTROL NUMBER,FILE PATH\n"
            "DOC00000001,folder_001/00000001.pdf\n"
            "DOC00000002,folder_001/00000002.pdf\n"
        )
        with open(csv_path, "w", encoding="utf-8") as f:
            f.write(content)

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=csv_path,
                format_name="csv",
                expected_count=5,
            )
        self.assertIn("record count mismatch", str(ctx.exception).lower())

    def test_csv_duplicate_identity_rejected(self):
        csv_path = os.path.join(self.dir_path, "archive.csv")
        content = (
            "CONTROL NUMBER,FILE PATH\n"
            "DOC00000001,folder_001/00000001.pdf\n"
            "DOC00000001,folder_001/00000002.pdf\n"
        )
        with open(csv_path, "w", encoding="utf-8") as f:
            f.write(content)

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=csv_path,
                format_name="csv",
                expected_count=2,
            )
        self.assertIn("Duplicate identity", str(ctx.exception))

    def test_csv_wrong_archive_path_rejected(self):
        csv_path = os.path.join(self.dir_path, "archive.csv")
        content = (
            "CONTROL NUMBER,FILE PATH\n"
            "DOC00000001,folder_001/nonexistent.pdf\n"
        )
        with open(csv_path, "w", encoding="utf-8") as f:
            f.write(content)

        self._create_zip(["00000001.pdf"])

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=csv_path,
                format_name="csv",
                expected_count=1,
                dir_path=self.dir_path,
            )
        self.assertIn("not found in Archive", str(ctx.exception))

    # --- XML (EDRM-XML) Tests ---

    def test_valid_xml_format(self):
        xml_path = os.path.join(self.dir_path, "archive.xml")
        docs = "\n".join(
            f'    <Document DocID="DOC{i:08d}">\n'
            f'      <Files>\n'
            f'        <File FileType="Native">\n'
            f'          <ExternalFile FilePath="folder_001/{i:08d}.pdf" FileName="{i:08d}.pdf" FileSize="312" Hash="" />\n'
            f'        </File>\n'
            f'      </Files>\n'
            f'    </Document>'
            for i in range(1, 6)
        )
        content = (
            '<?xml version="1.0" encoding="utf-8" standalone="yes"?>\n'
            '<Root DataInterchangeType="Export" MajorVersion="1" MinorVersion="2">\n'
            '  <Batch>\n'
            f'{docs}\n'
            '  </Batch>\n'
            '</Root>\n'
        )
        with open(xml_path, "w", encoding="utf-8") as f:
            f.write(content)

        self._create_zip([f"{i:08d}.pdf" for i in range(1, 6)])

        result = oracle.validate_load_file(
            load_file_path=xml_path,
            format_name="xml",
            expected_count=5,
            file_type="pdf",
            dir_path=self.dir_path,
        )
        self.assertTrue(result["valid"])
        self.assertEqual(result["record_count"], 5)

    def test_malformed_xml_rejected(self):
        xml_path = os.path.join(self.dir_path, "archive.xml")
        content = (
            '<?xml version="1.0" encoding="utf-8"?>\n'
            '<Root DataInterchangeType="Export">\n'
            '  <Batch>\n'
            '    <Document DocID="DOC00000001">\n'
            '  </Batch>\n'
            '</Root>\n'
        )
        with open(xml_path, "w", encoding="utf-8") as f:
            f.write(content)

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=xml_path,
                format_name="xml",
                expected_count=1,
            )
        self.assertIn("Malformed XML", str(ctx.exception))

    def test_xml_plural_documents_tag_rejected(self):
        xml_path = os.path.join(self.dir_path, "archive.xml")
        content = (
            '<?xml version="1.0" encoding="utf-8"?>\n'
            '<Root DataInterchangeType="Export" MajorVersion="1" MinorVersion="2">\n'
            '  <Batch>\n'
            '    <Documents>\n'
            '      <Document DocID="DOC00000001" />\n'
            '    </Documents>\n'
            '  </Batch>\n'
            '</Root>\n'
        )
        with open(xml_path, "w", encoding="utf-8") as f:
            f.write(content)

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=xml_path,
                format_name="xml",
                expected_count=1,
            )
        self.assertIn("Plural wrapper <Documents>", str(ctx.exception))

    def test_xml_duplicate_docid_rejected(self):
        xml_path = os.path.join(self.dir_path, "archive.xml")
        content = (
            '<?xml version="1.0" encoding="utf-8"?>\n'
            '<Root DataInterchangeType="Export" MajorVersion="1" MinorVersion="2">\n'
            '  <Batch>\n'
            '    <Document DocID="DOC00000001"><Files><File FileType="Native"><ExternalFile FilePath="f/1.pdf" FileName="1.pdf" FileSize="10" Hash="" /></File></Files></Document>\n'
            '    <Document DocID="DOC00000001"><Files><File FileType="Native"><ExternalFile FilePath="f/2.pdf" FileName="2.pdf" FileSize="10" Hash="" /></File></Files></Document>\n'
            '  </Batch>\n'
            '</Root>\n'
        )
        with open(xml_path, "w", encoding="utf-8") as f:
            f.write(content)

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=xml_path,
                format_name="xml",
                expected_count=2,
            )
        self.assertIn("Duplicate DocID", str(ctx.exception))

    # --- Concordance & DAT Tests ---

    def test_valid_concordance_format(self):
        dat_path = os.path.join(self.dir_path, "archive.dat")
        col_sep = "\x14"
        quote = "\xfe"
        header = f"{quote}BEGATTY{quote}{col_sep}{quote}ENDATTY{quote}{col_sep}{quote}CONTROLNUMBER{quote}{col_sep}{quote}PATH{quote}\n"
        rows = [
            f"{quote}{quote}{col_sep}{quote}{quote}{col_sep}{quote}DOC{i:08d}{quote}{col_sep}{quote}folder_001/{i:08d}.pdf{quote}\n"
            for i in range(1, 6)
        ]
        with open(dat_path, "w", encoding="utf-8") as f:
            f.write("\ufeff" + header + "".join(rows))

        self._create_zip([f"{i:08d}.pdf" for i in range(1, 6)])

        result = oracle.validate_load_file(
            load_file_path=dat_path,
            format_name="concordance",
            expected_count=5,
            file_type="pdf",
            dir_path=self.dir_path,
        )
        self.assertTrue(result["valid"])
        self.assertEqual(result["record_count"], 5)

    def test_valid_dat_custom_delimiters(self):
        dat_path = os.path.join(self.dir_path, "archive.dat")
        content = (
            "^Control Number^|^File Path^\n"
            + "\n".join(f"^DOC{i:08d}^|^folder_001/{i:08d}.pdf^" for i in range(1, 6))
            + "\n"
        )
        with open(dat_path, "w", encoding="utf-8") as f:
            f.write(content)

        self._create_zip([f"{i:08d}.pdf" for i in range(1, 6)])

        result = oracle.validate_load_file(
            load_file_path=dat_path,
            format_name="dat",
            expected_count=5,
            file_type="pdf",
            delimiter_column="|",
            delimiter_quote="^",
            dir_path=self.dir_path,
        )
        self.assertTrue(result["valid"])

    def test_dat_with_delimiter_inside_quoted_field(self):
        dat_path = os.path.join(self.dir_path, "archive.dat")
        content = (
            "^Control Number^|^File Path^\n"
            "^DOC00000001^|^folder_001/doc|with|pipes.pdf^\n"
        )
        with open(dat_path, "w", encoding="utf-8") as f:
            f.write(content)

        self._create_zip(["doc|with|pipes.pdf"])

        result = oracle.validate_load_file(
            load_file_path=dat_path,
            format_name="dat",
            expected_count=1,
            file_type="pdf",
            delimiter_column="|",
            delimiter_quote="^",
            dir_path=self.dir_path,
        )
        self.assertTrue(result["valid"])
        self.assertEqual(result["record_count"], 1)

    def test_dat_ignored_delimiter_override_rejected(self):
        dat_path = os.path.join(self.dir_path, "archive.dat")
        # File written with comma instead of expected pipe delimiter
        content = (
            '"Control Number","File Path"\n'
            '"DOC00000001","folder_001/00000001.pdf"\n'
        )
        with open(dat_path, "w", encoding="utf-8") as f:
            f.write(content)

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=dat_path,
                format_name="dat",
                expected_count=1,
                delimiter_column="|",
                delimiter_quote='"',
            )
        self.assertIn("delimiter", str(ctx.exception).lower())

    # --- Bates Numbering Tests ---

    def test_bates_numbering_validation_success(self):
        csv_path = os.path.join(self.dir_path, "archive.csv")
        content = (
            "CONTROL NUMBER,FILE PATH,BATES NUMBER\n"
            "DOC00000001,folder_001/00000001.pdf,TEST000001\n"
            "DOC00000002,folder_001/00000002.pdf,TEST000002\n"
            "DOC00000003,folder_001/00000003.pdf,TEST000003\n"
        )
        with open(csv_path, "w", encoding="utf-8") as f:
            f.write(content)

        self._create_zip([f"{i:08d}.pdf" for i in range(1, 4)])

        result = oracle.validate_load_file(
            load_file_path=csv_path,
            format_name="csv",
            expected_count=3,
            bates_prefix="TEST",
            bates_start=1,
            bates_digits=6,
            dir_path=self.dir_path,
        )
        self.assertTrue(result["valid"])

    def test_bates_numbering_mismatch_rejected(self):
        csv_path = os.path.join(self.dir_path, "archive.csv")
        content = (
            "CONTROL NUMBER,FILE PATH,BATES NUMBER\n"
            "DOC00000001,folder_001/00000001.pdf,TEST000001\n"
            "DOC00000002,folder_001/00000002.pdf,WRONG000002\n"
            "DOC00000003,folder_001/00000003.pdf,TEST000003\n"
        )
        with open(csv_path, "w", encoding="utf-8") as f:
            f.write(content)

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=csv_path,
                format_name="csv",
                expected_count=3,
                bates_prefix="TEST",
                bates_start=1,
                bates_digits=6,
            )
        self.assertIn("Bates number mismatch", str(ctx.exception))

    # --- Families Tests ---

    def test_families_csv_validation(self):
        csv_path = os.path.join(self.dir_path, "archive.csv")
        lines = [
            "CONTROL NUMBER,FILE PATH,BEGATTACH,ENDATTACH,PARENTDOCID\n",
            "DOC00000001,folder_001/00000001.eml,DOC00000001,DOC00000001_A001,\n",
            "DOC00000001_A001,folder_001/1_attach.tiff,DOC00000001,DOC00000001_A001,DOC00000001\n",
        ]
        with open(csv_path, "w", encoding="utf-8") as f:
            f.writelines(lines)

        self._create_zip(["00000001.eml", "1_attach.tiff"])

        result = oracle.validate_load_file(
            load_file_path=csv_path,
            format_name="csv",
            expected_count=1,  # 1 primary doc, expands to 2 with 100% attachment rate
            file_type="eml",
            with_families=True,
            attachment_rate=100,
            dir_path=self.dir_path,
        )
        self.assertTrue(result["valid"])
        self.assertEqual(result["record_count"], 2)


    # --- Additional Malformed & Negative Tests ---

    def test_xml_document_count_mismatch_rejected(self):
        xml_path = os.path.join(self.dir_path, "archive.xml")
        content = (
            '<?xml version="1.0" encoding="utf-8"?>\n'
            '<Root DataInterchangeType="Export" MajorVersion="1" MinorVersion="2">\n'
            '  <Batch>\n'
            '    <Document DocID="DOC00000001"><Files><File FileType="Native"><ExternalFile FilePath="f/1.pdf" FileName="1.pdf" FileSize="10" Hash="" /></File></Files></Document>\n'
            '  </Batch>\n'
            '</Root>\n'
        )
        with open(xml_path, "w", encoding="utf-8") as f:
            f.write(content)

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=xml_path,
                format_name="xml",
                expected_count=3,
            )
        self.assertIn("Document count mismatch", str(ctx.exception))

    def test_xml_wrong_archive_path_rejected(self):
        xml_path = os.path.join(self.dir_path, "archive.xml")
        content = (
            '<?xml version="1.0" encoding="utf-8"?>\n'
            '<Root DataInterchangeType="Export" MajorVersion="1" MinorVersion="2">\n'
            '  <Batch>\n'
            '    <Document DocID="DOC00000001"><Files><File FileType="Native"><ExternalFile FilePath="folder_001/missing.pdf" FileName="missing.pdf" FileSize="10" Hash="" /></File></Files></Document>\n'
            '  </Batch>\n'
            '</Root>\n'
        )
        with open(xml_path, "w", encoding="utf-8") as f:
            f.write(content)

        self._create_zip(["00000001.pdf"])

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=xml_path,
                format_name="xml",
                expected_count=1,
                dir_path=self.dir_path,
            )
        self.assertIn("not found in Archive", str(ctx.exception))

    def test_xml_bates_mismatch_rejected(self):
        xml_path = os.path.join(self.dir_path, "archive.xml")
        content = (
            '<?xml version="1.0" encoding="utf-8"?>\n'
            '<Root DataInterchangeType="Export" MajorVersion="1" MinorVersion="2">\n'
            '  <Batch>\n'
            '    <Document DocID="WRONG000001"><Files><File FileType="Native"><ExternalFile FilePath="f/1.pdf" FileName="1.pdf" FileSize="10" Hash="" /></File></Files></Document>\n'
            '  </Batch>\n'
            '</Root>\n'
        )
        with open(xml_path, "w", encoding="utf-8") as f:
            f.write(content)

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=xml_path,
                format_name="xml",
                expected_count=1,
                bates_prefix="TEST",
                bates_start=1,
                bates_digits=6,
            )
        self.assertIn("Bates number mismatch", str(ctx.exception))

    def test_dat_record_count_mismatch_rejected(self):
        dat_path = os.path.join(self.dir_path, "archive.dat")
        content = (
            "\xfeControl Number\xfe\x14\xfeFile Path\xfe\n"
            "\xfeDOC00000001\xfe\x14\xfefolder_001/00000001.pdf\xfe\n"
        )
        with open(dat_path, "w", encoding="utf-8") as f:
            f.write(content)

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=dat_path,
                format_name="dat",
                expected_count=3,
            )
        self.assertIn("record count mismatch", str(ctx.exception).lower())

    def test_dat_duplicate_identity_rejected(self):
        dat_path = os.path.join(self.dir_path, "archive.dat")
        content = (
            "\xfeControl Number\xfe\x14\xfeFile Path\xfe\n"
            "\xfeDOC00000001\xfe\x14\xfefolder_001/00000001.pdf\xfe\n"
            "\xfeDOC00000001\xfe\x14\xfefolder_001/00000002.pdf\xfe\n"
        )
        with open(dat_path, "w", encoding="utf-8") as f:
            f.write(content)

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=dat_path,
                format_name="dat",
                expected_count=2,
            )
        self.assertIn("Duplicate identity", str(ctx.exception))

    def test_dat_wrong_archive_path_rejected(self):
        dat_path = os.path.join(self.dir_path, "archive.dat")
        content = (
            "\xfeControl Number\xfe\x14\xfeFile Path\xfe\n"
            "\xfeDOC00000001\xfe\x14\xfefolder_001/missing.pdf\xfe\n"
        )
        with open(dat_path, "w", encoding="utf-8") as f:
            f.write(content)

        self._create_zip(["00000001.pdf"])

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=dat_path,
                format_name="dat",
                expected_count=1,
                dir_path=self.dir_path,
            )
        self.assertIn("not found in Archive", str(ctx.exception))

    def test_opt_bates_mismatch_rejected(self):
        opt_path = os.path.join(self.dir_path, "archive.opt")
        with open(opt_path, "w", encoding="utf-8") as f:
            f.write("WRONG000001,VOL001,IMAGES\\WRONG000001.tif,Y,,,1\n")

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=opt_path,
                format_name="opt",
                expected_count=1,
                bates_prefix="TEST",
                bates_start=1,
                bates_digits=6,
            )
        self.assertIn("Bates number mismatch", str(ctx.exception))

    def test_dat_unquoted_field_rejected(self):
        dat_path = os.path.join(self.dir_path, "archive.dat")
        content = (
            "\xfeControl Number\xfe\x14\xfeFile Path\xfe\n"
            "DOC00000001\x14folder_001/00000001.pdf\n"
        )
        with open(dat_path, "w", encoding="utf-8") as f:
            f.write(content)

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=dat_path,
                format_name="dat",
                expected_count=1,
            )
        self.assertIn("quote delimiter", str(ctx.exception).lower())

    def test_properties_missing_total_records_rejected(self):
        dat_path = os.path.join(self.dir_path, "loadfile.dat")
        prop_path = os.path.join(self.dir_path, "loadfile_properties.json")
        content = (
            "\xfeControl Number\xfe\x14\xfeFile Path\xfe\n"
            "\xfeDOC00000001\xfe\x14\xfefolder_001/00000001.pdf\xfe\n"
        )
        with open(dat_path, "w", encoding="utf-8") as f:
            f.write(content)
        with open(prop_path, "w", encoding="utf-8") as f:
            json.dump({"fileName": "loadfile.dat"}, f)

        with self.assertRaises(oracle.ValidationError) as ctx:
            oracle.validate_load_file(
                load_file_path=dat_path,
                format_name="dat",
                expected_count=1,
                loadfile_only=True,
                dir_path=self.dir_path,
            )
        self.assertIn("missing totalRecords", str(ctx.exception))


if __name__ == "__main__":
    unittest.main()

