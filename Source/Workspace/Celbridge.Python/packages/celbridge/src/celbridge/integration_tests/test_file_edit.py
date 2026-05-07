"""File-content edit tools. These write straight to disk; if a document is
open, its buffer reloads from disk as a side effect.
"""
import base64
import json

import pytest

from celbridge.cel_proxy import CelError

from .helpers import close_if_open, delete_if_exists


@pytest.fixture(autouse=True)
def workspace(explorer, file, document):
    delete_if_exists(explorer, "TestFileEdit")
    explorer.create_folder("TestFileEdit")
    file.write(
        "TestFileEdit/hello.txt",
        "Hello, World!\nLine 2\nLine 3\nLine 4\nLine 5\n",
    )
    yield
    close_if_open(document, "TestFileEdit/hello.txt")
    close_if_open(document, "TestFileEdit/test.bin")
    close_if_open(document, "TestFileEdit/new_file.txt")
    delete_if_exists(explorer, "TestFileEdit")


class TestFileEdit:

    def test_write_and_read(self, file):
        result = file.read("TestFileEdit/hello.txt")
        assert "Hello, World!" in result["content"]

    def test_apply_edits(self, file):
        edits = [
            {
                "line": 1,
                "column": 8,
                "endLine": 1,
                "endColumn": 13,
                "newText": "Celbridge",
            }
        ]
        file.apply_edits("TestFileEdit/hello.txt", json.dumps(edits))
        result = file.read("TestFileEdit/hello.txt")
        assert "Celbridge" in result["content"]

    def test_apply_edits_on_closed_document_writes_to_disk(self, file):
        # Edits to a closed document write straight to disk and the disk
        # immediately reflects the edit.
        edits = [
            {
                "line": 1,
                "column": 8,
                "endLine": 1,
                "endColumn": 13,
                "newText": "Celbridge",
            }
        ]
        file.apply_edits("TestFileEdit/hello.txt", json.dumps(edits))
        disk = file.read("TestFileEdit/hello.txt")
        assert "Celbridge" in disk["content"]

    def test_apply_edits_open_document_persists_via_disk(self, file, document):
        # When the document is open, edits land on disk and the open buffer
        # reloads from disk. The response describes the post-edit document and
        # the file on disk reflects it immediately.
        document.open("TestFileEdit/hello.txt")
        edits = [
            {
                "line": 1,
                "column": 1,
                "endLine": 1,
                "endColumn": -1,
                "newText": "Regression line 1\nRegression line 2",
            }
        ]
        result = file.apply_edits(
            "TestFileEdit/hello.txt", json.dumps(edits)
        )

        disk = file.read("TestFileEdit/hello.txt")
        assert "Regression line 1" in disk["content"]
        assert "Regression line 2" in disk["content"]

        disk_line_count = len(disk["content"].splitlines())
        assert result["totalLineCount"] == disk_line_count

        affected = result["affectedLines"][0]
        context_text = "\n".join(affected["contextLines"])
        assert "Regression line 1" in context_text

    def test_find_replace(self, file):
        result = file.find_replace(
            "TestFileEdit/hello.txt",
            search_text="Line 2",
            replace_text="Second Line",
        )
        assert result["replacementCount"] >= 1
        result = file.read("TestFileEdit/hello.txt")
        assert "Second Line" in result["content"]

    def test_find_replace_open_document_followup_read_sees_replacement(self, file, document):
        # When the document is open, find_replace writes to disk and a
        # follow-up file_read must see the replacement, not the pre-replace
        # editor buffer.
        document.open("TestFileEdit/hello.txt")
        result = file.find_replace(
            "TestFileEdit/hello.txt",
            search_text="Line 2",
            replace_text="Second Line",
        )
        assert result["replacementCount"] >= 1
        disk = file.read("TestFileEdit/hello.txt")
        assert "Second Line" in disk["content"]

    def test_delete_lines(self, file):
        result = file.delete_lines(
            "TestFileEdit/hello.txt", start_line=2, end_line=3
        )
        assert "deletedFrom" in result
        assert "totalLineCount" in result
        result = file.read("TestFileEdit/hello.txt")
        assert "Line 2" not in result["content"]
        assert "Line 3" not in result["content"]

    def test_write_binary(self, file):
        content = base64.b64encode(b"BINARY_TEST_DATA_12345").decode("ascii")
        file.write_binary("TestFileEdit/test.bin", content)
        result = file.read_binary("TestFileEdit/test.bin")
        decoded = base64.b64decode(result["base64"])
        assert b"BINARY_TEST_DATA_12345" in decoded

    def test_write_replaces_open_document_content(self, file, document):
        # When the document is open, write replaces the disk content and
        # the open buffer reloads from disk.
        document.open("TestFileEdit/hello.txt")
        file.write("TestFileEdit/hello.txt", "completely new content")
        disk = file.read("TestFileEdit/hello.txt")
        assert disk["content"].strip() == "completely new content"

    def test_apply_edits_invalid_resource_key(self, file):
        with pytest.raises(CelError):
            file.apply_edits("\\invalid", "[]")

    def test_apply_edits_invalid_json(self, file):
        with pytest.raises(CelError):
            file.apply_edits("TestFileEdit/hello.txt", "not json")

    def test_apply_edits_empty_array(self, file):
        # Empty edits should succeed without error.
        file.apply_edits("TestFileEdit/hello.txt", "[]")

    def test_apply_edits_auto_serialized_list(self, file):
        edits = [{"line": 1, "endLine": 1, "newText": "Replaced first line"}]
        file.apply_edits("TestFileEdit/hello.txt", edits)
        result = file.read("TestFileEdit/hello.txt")
        assert "Replaced first line" in result["content"]

    def test_delete_lines_invalid_resource_key(self, file):
        with pytest.raises(CelError):
            file.delete_lines("\\invalid", start_line=1, end_line=1)

    def test_delete_lines_start_less_than_one(self, file):
        with pytest.raises(CelError):
            file.delete_lines(
                "TestFileEdit/hello.txt", start_line=0, end_line=1
            )

    def test_delete_lines_end_before_start(self, file):
        with pytest.raises(CelError):
            file.delete_lines(
                "TestFileEdit/hello.txt", start_line=3, end_line=1
            )

    def test_find_replace_no_matches(self, file):
        result = file.find_replace(
            "TestFileEdit/hello.txt",
            search_text="NONEXISTENT_STRING_XYZ",
            replace_text="replacement",
        )
        assert result["replacementCount"] == 0

    def test_find_replace_regex(self, file):
        result = file.find_replace(
            "TestFileEdit/hello.txt",
            search_text=r"Line \d+",
            replace_text="Replaced",
            use_regex=True,
        )
        assert result["replacementCount"] >= 1

    def test_find_replace_case_sensitive(self, file):
        result = file.find_replace(
            "TestFileEdit/hello.txt",
            search_text="hello",
            replace_text="Goodbye",
            match_case=True,
        )
        assert result["replacementCount"] == 0

    def test_write_creates_new_file(self, file):
        file.write("TestFileEdit/new_file.txt", "brand new content")
        result = file.read("TestFileEdit/new_file.txt")
        assert "brand new content" in result["content"]

    def test_write_overwrites_existing_file(self, file):
        file.write("TestFileEdit/hello.txt", "overwritten")
        result = file.read("TestFileEdit/hello.txt")
        assert "overwritten" in result["content"]
        assert "Hello, World!" not in result["content"]

    def test_write_empty_content(self, file):
        file.write("TestFileEdit/hello.txt", "")
        result = file.read("TestFileEdit/hello.txt")
        assert result["content"].strip() == ""

    def test_write_unicode_content(self, file):
        unicode_text = "Café 世界 🚀\n"
        file.write("TestFileEdit/hello.txt", unicode_text)
        result = file.read("TestFileEdit/hello.txt")
        assert "Café" in result["content"]
