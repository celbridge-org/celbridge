"""Verifies that file edit tools preserve the existing file's line endings
and trailing-newline state, and that file.write picks the platform default
when creating a new file.
"""
import json
import re

import pytest

from .helpers import close_if_open, delete_if_exists, write_with_line_endings


@pytest.fixture(autouse=True)
def workspace(explorer, document):
    delete_if_exists(explorer, "TestLineEndings")
    explorer.create_folder("TestLineEndings")
    yield
    close_if_open(document, "TestLineEndings/crlf.txt")
    close_if_open(document, "TestLineEndings/lf.txt")
    close_if_open(document, "TestLineEndings/no_trailing.txt")
    close_if_open(document, "TestLineEndings/with_trailing.txt")
    close_if_open(document, "TestLineEndings/new.txt")
    delete_if_exists(explorer, "TestLineEndings")


class TestFileLineEndings:

    def test_edit_preserves_crlf(self, file):
        write_with_line_endings(
            file,
            "TestLineEndings/crlf.txt",
            "Line 1\nLine 2\nLine 3\n",
            "\r\n",
        )
        file.edit(
            "TestLineEndings/crlf.txt",
            old_string="Line 2",
            new_string="Replaced",
        )

        content = file.read("TestLineEndings/crlf.txt")["content"]
        assert "\r\n" in content
        # Regression for the historical \r\r\n bug. No doubled CR allowed.
        assert "\r\r" not in content
        # No lone \n alongside CRLF.
        assert not re.search(r"(?<!\r)\n", content)

    def test_edit_preserves_lf(self, file):
        write_with_line_endings(
            file,
            "TestLineEndings/lf.txt",
            "Line 1\nLine 2\nLine 3\n",
            "\n",
        )
        file.edit(
            "TestLineEndings/lf.txt",
            old_string="Line 2",
            new_string="Replaced",
        )

        content = file.read("TestLineEndings/lf.txt")["content"]
        assert "\r" not in content

    def test_replace_preserves_crlf(self, file):
        write_with_line_endings(
            file,
            "TestLineEndings/crlf.txt",
            "alpha\nbeta\ngamma\n",
            "\r\n",
        )
        file.replace(
            "TestLineEndings/crlf.txt",
            search_text="beta",
            replace_text="BETA",
        )

        content = file.read("TestLineEndings/crlf.txt")["content"]
        assert "BETA" in content
        assert "\r\n" in content
        assert "\r\r" not in content

    def test_multi_edit_preserves_crlf(self, file):
        write_with_line_endings(
            file,
            "TestLineEndings/crlf.txt",
            "one\ntwo\nthree\nfour\n",
            "\r\n",
        )
        edits = [
            {"oldString": "two", "newString": "TWO"},
            {"oldString": "four", "newString": "FOUR"},
        ]
        file.multi_edit(
            "TestLineEndings/crlf.txt", json.dumps(edits)
        )

        content = file.read("TestLineEndings/crlf.txt")["content"]
        assert "TWO" in content
        assert "FOUR" in content
        assert "\r\n" in content
        assert "\r\r" not in content

    def test_write_new_file_uses_lf(self, file):
        # file.write defaults to LF for new files regardless of host platform.
        # Matches cross-platform toolchain expectations and what most agents
        # produce by default.
        file.write(
            "TestLineEndings/new.txt", "first\nsecond\nthird\n"
        )

        content = file.read("TestLineEndings/new.txt")["content"]
        assert "\n" in content
        assert "\r" not in content

    def test_edit_preserves_no_trailing_newline(self, file):
        write_with_line_endings(
            file,
            "TestLineEndings/no_trailing.txt",
            "alpha\nbeta\ngamma",  # no trailing \n
            "\r\n",
        )
        file.edit(
            "TestLineEndings/no_trailing.txt",
            old_string="beta",
            new_string="BETA",
        )

        content = file.read("TestLineEndings/no_trailing.txt")["content"]
        assert not content.endswith("\n")
        assert not content.endswith("\r")
        assert "BETA" in content

    def test_edit_preserves_trailing_newline(self, file):
        write_with_line_endings(
            file,
            "TestLineEndings/with_trailing.txt",
            "alpha\nbeta\ngamma\n",  # trailing \n
            "\r\n",
        )
        file.edit(
            "TestLineEndings/with_trailing.txt",
            old_string="beta",
            new_string="BETA",
        )

        content = file.read("TestLineEndings/with_trailing.txt")["content"]
        assert content.endswith("\r\n")
        assert "\r\r" not in content
