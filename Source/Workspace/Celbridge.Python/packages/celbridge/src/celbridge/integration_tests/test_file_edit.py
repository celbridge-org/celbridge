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

    def test_replace(self, file):
        result = file.replace(
            "TestFileEdit/hello.txt",
            search_text="Line 2",
            replace_text="Second Line",
        )
        assert result["replacementCount"] >= 1
        result = file.read("TestFileEdit/hello.txt")
        assert "Second Line" in result["content"]

    def test_replace_open_document_followup_read_sees_replacement(self, file, document):
        # When the document is open, replace writes to disk and a follow-up
        # file_read must see the replacement, not the pre-replace editor buffer.
        document.open("TestFileEdit/hello.txt")
        result = file.replace(
            "TestFileEdit/hello.txt",
            search_text="Line 2",
            replace_text="Second Line",
        )
        assert result["replacementCount"] >= 1
        disk = file.read("TestFileEdit/hello.txt")
        assert "Second Line" in disk["content"]

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

    def test_replace_no_matches(self, file):
        result = file.replace(
            "TestFileEdit/hello.txt",
            search_text="NONEXISTENT_STRING_XYZ",
            replace_text="replacement",
        )
        assert result["replacementCount"] == 0

    def test_replace_regex(self, file):
        result = file.replace(
            "TestFileEdit/hello.txt",
            search_text=r"Line \d+",
            replace_text="Replaced",
            use_regex=True,
        )
        assert result["replacementCount"] >= 1

    def test_replace_case_sensitive(self, file):
        result = file.replace(
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

    def test_file_edit_replaces_unique_match(self, file):
        result = file.edit(
            "TestFileEdit/hello.txt",
            old_string="Line 2",
            new_string="Second Line",
        )
        assert result["matchCount"] == 1
        assert len(result["affectedLines"]) == 1
        assert result["affectedLines"][0]["from"] == 2
        assert result["affectedLines"][0]["to"] == 2
        disk = file.read("TestFileEdit/hello.txt")
        assert "Second Line" in disk["content"]
        assert "Line 2" not in disk["content"]

    def test_file_edit_multi_match_fails_unless_replace_all(self, file):
        file.write(
            "TestFileEdit/hello.txt",
            "x\ny\nx\ny\nx\n",
        )

        # Without replace_all the call fails with a disambiguation hint.
        with pytest.raises(CelError) as exc_info:
            file.edit(
                "TestFileEdit/hello.txt",
                old_string="x",
                new_string="X",
            )
        assert "3 occurrences" in str(exc_info.value)

        # With replace_all every occurrence is replaced.
        result = file.edit(
            "TestFileEdit/hello.txt",
            old_string="x",
            new_string="X",
            replace_all=True,
        )
        assert result["matchCount"] == 3
        disk = file.read("TestFileEdit/hello.txt")
        assert disk["content"] == "X\ny\nX\ny\nX\n"

    def test_file_edit_append_via_last_line_anchor(self, file):
        # Canonical append workflow: anchor against the existing last line and
        # concatenate the new content in new_string. No coordinates needed.
        file.write(
            "TestFileEdit/hello.txt",
            "first\nlast line\n",
        )
        result = file.edit(
            "TestFileEdit/hello.txt",
            old_string="last line\n",
            new_string="last line\nappended one\nappended two\n",
        )
        assert result["matchCount"] == 1
        disk = file.read("TestFileEdit/hello.txt")
        assert disk["content"] == "first\nlast line\nappended one\nappended two\n"

    def test_file_multi_edit_atomic_batch(self, file):
        # All edits land or none do. The failing batch leaves the file unchanged.
        original = "alpha\nbeta\ngamma\n"
        file.write("TestFileEdit/hello.txt", original)

        edits = [
            {"oldString": "alpha", "newString": "ALPHA"},
            {"oldString": "does-not-exist", "newString": "X"},
        ]
        with pytest.raises(CelError) as exc_info:
            file.multi_edit("TestFileEdit/hello.txt", json.dumps(edits))
        assert "Edit 1" in str(exc_info.value)

        disk = file.read("TestFileEdit/hello.txt")
        assert disk["content"] == original

        # A clean batch applies both edits in order.
        edits = [
            {"oldString": "alpha", "newString": "ALPHA"},
            {"oldString": "gamma", "newString": "GAMMA"},
        ]
        result = file.multi_edit("TestFileEdit/hello.txt", json.dumps(edits))
        assert result["appliedCount"] == 2
        assert len(result["affectedLines"]) == 2
        disk = file.read("TestFileEdit/hello.txt")
        assert disk["content"] == "ALPHA\nbeta\nGAMMA\n"

    def test_file_multi_edit_sequential_application(self, file):
        # Edit 1 anchors against text produced by edit 0.
        file.write(
            "TestFileEdit/hello.txt",
            "foo()\nresult = foo()\n",
        )
        edits = [
            {"oldString": "foo()", "newString": "bar()", "replaceAll": True},
            {"oldString": "result = bar()", "newString": "result = bar() + 1"},
        ]
        result = file.multi_edit(
            "TestFileEdit/hello.txt", json.dumps(edits)
        )
        assert result["appliedCount"] == 2
        disk = file.read("TestFileEdit/hello.txt")
        assert disk["content"] == "bar()\nresult = bar() + 1\n"

    def test_file_edit_same_line_replace_all_merges_with_match_count(self, file):
        # Three hits of "foo" on a single line collapse into one affectedLines
        # entry whose matchCount reports the per-line total. Top-level matchCount
        # is still 3; the sum of per-entry matchCounts equals it.
        file.write(
            "TestFileEdit/hello.txt",
            "foo bar foo baz foo\nbeta\n",
        )
        result = file.edit(
            "TestFileEdit/hello.txt",
            old_string="foo",
            new_string="FOO",
            replace_all=True,
        )
        assert result["matchCount"] == 3
        assert len(result["affectedLines"]) == 1
        assert result["affectedLines"][0]["from"] == 1
        assert result["affectedLines"][0]["to"] == 1
        assert result["affectedLines"][0]["matchCount"] == 3
        disk = file.read("TestFileEdit/hello.txt")
        assert disk["content"] == "FOO bar FOO baz FOO\nbeta\n"

    def test_file_replace_match_word(self, file):
        # matchWord constrains literal matches to word boundaries. "log" hits the
        # two standalone occurrences but leaves "logger" and "mylog" alone.
        file.write(
            "TestFileEdit/hello.txt",
            "log here\nlogger\nmylog\nlog end\n",
        )
        result = file.replace(
            "TestFileEdit/hello.txt",
            search_text="log",
            replace_text="LOG",
            match_word=True,
        )
        assert result["replacementCount"] == 2
        disk = file.read("TestFileEdit/hello.txt")
        assert disk["content"] == "LOG here\nlogger\nmylog\nLOG end\n"

    def test_file_replace_default_is_case_sensitive(self, file):
        # The default for file.replace is matchCase: true — the right default
        # for code editing. Without overriding, "hello" matches only the
        # lowercase occurrence and leaves "Hello" / "HELLO" untouched.
        file.write(
            "TestFileEdit/hello.txt",
            "hello\nHello\nHELLO\n",
        )
        result = file.replace(
            "TestFileEdit/hello.txt",
            search_text="hello",
            replace_text="HI",
        )
        assert result["replacementCount"] == 1
        disk = file.read("TestFileEdit/hello.txt")
        assert disk["content"] == "HI\nHello\nHELLO\n"

    def test_file_multi_edit_edit_index_tags_disjoint_batch(self, file):
        # Input order targets lines 5, 1, 9 (out of file order). affectedLines
        # comes back sorted ascending by from, but each entry carries an
        # editIndex pointing back to its position in the input batch, so the
        # caller can attribute ranges without reverse-engineering the order.
        file.write(
            "TestFileEdit/hello.txt",
            "a\nb\nc\nd\ne\nf\ng\nh\ni\n",
        )
        edits = [
            {"oldString": "e", "newString": "EEE"},
            {"oldString": "a", "newString": "AAA"},
            {"oldString": "i", "newString": "III"},
        ]
        result = file.multi_edit("TestFileEdit/hello.txt", json.dumps(edits))
        assert result["appliedCount"] == 3
        affected = result["affectedLines"]
        assert len(affected) == 3
        assert affected[0]["from"] == 1
        assert affected[0]["editIndex"] == 1
        assert affected[1]["from"] == 5
        assert affected[1]["editIndex"] == 0
        assert affected[2]["from"] == 9
        assert affected[2]["editIndex"] == 2

    def test_file_edit_affected_lines_include_context_lines(self, file):
        # contextLines carries the post-edit content of the affected range plus
        # one surrounding line on each side, so the caller can verify the edit
        # without a follow-up file.read.
        result = file.edit(
            "TestFileEdit/hello.txt",
            old_string="Line 3",
            new_string="THIRD",
        )
        assert result["matchCount"] == 1
        affected = result["affectedLines"][0]
        assert affected["from"] == 3
        assert affected["to"] == 3
        assert affected["contextLines"] == ["Line 2", "THIRD", "Line 4"]


class TestCelDenial:
    """Byte-write tools refuse .cel targets and point the caller at data_*."""

    def test_write_denied(self, file):
        with pytest.raises(CelError, match="(?i)data_"):
            file.write("TestFileEdit/hello.txt.cel", "content")

    def test_write_binary_denied(self, file):
        content = base64.b64encode(b"bytes").decode("ascii")
        with pytest.raises(CelError, match="(?i)data_"):
            file.write_binary("TestFileEdit/hello.txt.cel", content)

    def test_edit_denied(self, file):
        with pytest.raises(CelError, match="(?i)data_"):
            file.edit(
                "TestFileEdit/hello.txt.cel",
                old_string="old",
                new_string="new",
            )

    def test_multi_edit_denied(self, file):
        edits = [{"oldString": "a", "newString": "b"}]
        with pytest.raises(CelError, match="(?i)data_"):
            file.multi_edit("TestFileEdit/hello.txt.cel", json.dumps(edits))

    def test_replace_denied(self, file):
        with pytest.raises(CelError, match="(?i)data_"):
            file.replace(
                "TestFileEdit/hello.txt.cel",
                search_text="old",
                replace_text="new",
            )

    def test_write_denied_when_creating_new_cel(self, file):
        # Denial fires on path shape, regardless of whether the target exists.
        with pytest.raises(CelError, match="(?i)data_"):
            file.write("TestFileEdit/new_orphan.cel", "content")
