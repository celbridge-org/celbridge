"""
Celbridge MCP Integration Test Script
Tests all available tools across all celbridge modules:
  - celbridge.app
  - celbridge.file
  - celbridge.query
  - celbridge.explorer
  - celbridge.document
  - celbridge.package
  - celbridge.webview

Includes both happy-path tests and adversarial error-handling tests.

Usage (IPython REPL):
    cel.test()
"""

import json
import base64
import os
import time
import unittest

from celbridge.cel_proxy import CelError

# These are populated by main() before tests run.
# Declared at module level so test classes can reference them.
app = None
file = None
query = None
explorer = None
document = None
package = None
webview = None


# ---------------------------------------------------------------------------
# Custom test runner that reports progress
# ---------------------------------------------------------------------------

class ProgressTestResult(unittest.TestResult):
    """Test result that prints progress to stdout as each test completes."""

    def __init__(self, total_tests):
        super().__init__()
        self._total = total_tests
        self._current = 0

    def _progress_prefix(self):
        self._current += 1
        return f"[{self._current}/{self._total}]"

    def startTest(self, test):
        super().startTest(test)

    def addSuccess(self, test):
        super().addSuccess(test)
        prefix = self._progress_prefix()
        print(f"  {prefix} \033[92mPASS\033[0m: {test}")
        app.log(f"  {prefix} PASS: {test}")

    def addFailure(self, test, err):
        super().addFailure(test, err)
        prefix = self._progress_prefix()
        print(f"  {prefix} \033[91mFAIL\033[0m: {test}")
        app.log_error(f"  {prefix} FAIL: {test}")

    def addError(self, test, err):
        super().addError(test, err)
        prefix = self._progress_prefix()
        print(f"  {prefix} \033[91mERROR\033[0m: {test}")
        app.log_error(f"  {prefix} ERROR: {test}")

    def addSkip(self, test, reason):
        super().addSkip(test, reason)
        prefix = self._progress_prefix()
        print(f"  {prefix} \033[93mSKIP\033[0m: {test} -- {reason}")
        app.log_warning(f"  {prefix} SKIP: {test} -- {reason}")


class ProgressTestRunner:
    """Minimal test runner that uses ProgressTestResult."""

    def run(self, suite):
        total = suite.countTestCases()
        print(f"\nRunning {total} tests...\n")
        result = ProgressTestResult(total)
        suite(result)
        return result


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _delete_if_exists(resource):
    """Delete a resource, ignoring errors if it doesn't exist."""
    try:
        explorer.delete(resource)
    except Exception:
        pass


def _close_if_open(resource):
    """Close a document if it is open."""
    try:
        ctx = document.get_context()
        if any(d["resource"] == resource for d in ctx.get("openDocuments", [])):
            document.close(resource, force_close=True)
    except Exception:
        pass


def _write_with_line_endings(resource, text_with_lf, line_ending):
    """Write a file with explicit line endings, bypassing document.write's
    platform-default conversion. Used by the line-ending preservation tests
    to set up a file with known endings regardless of host OS."""
    text = text_with_lf.replace("\n", line_ending)
    encoded = base64.b64encode(text.encode("utf-8")).decode("ascii")
    document.write_binary(resource, encoded)


# ---------------------------------------------------------------------------
# app module
# ---------------------------------------------------------------------------

class TestApp(unittest.TestCase):

    def test_get_status(self):
        result = app.get_status()
        self.assertTrue(result["isLoaded"])
        self.assertTrue(len(result["projectName"]) > 0)

    def test_get_version(self):
        version = app.get_version()
        parts = version.split(".")
        self.assertEqual(len(parts), 3, f"Expected 3-part version, got: {version}")

    def test_log(self):
        app.log("Integration test: log message")

    def test_log_warning(self):
        app.log_warning("Integration test: warning message")

    def test_log_error(self):
        app.log_error("Integration test: error message")

    def test_refresh_files(self):
        app.refresh_files()

    def test_show_alert(self):
        app.show_alert("Integration test alert", title="Test")

    def test_log_empty_message(self):
        app.log("")

    def test_log_unicode(self):
        app.log("Unicode test: \u00e9\u00e8\u00ea \u4e16\u754c \ud83d\ude00")


# ---------------------------------------------------------------------------
# query module
# ---------------------------------------------------------------------------

class TestQuery(unittest.TestCase):

    def test_get_context(self):
        ctx = query.get_context()
        self.assertIn("Resource Keys", ctx)

    def test_get_python_api(self):
        api = query.get_python_api()
        self.assertIn("Celbridge Python API Reference", api)
        self.assertIn("## document", api)
        self.assertIn("## file", api)

    def test_get_python_api_contains_return_types(self):
        api = query.get_python_api()
        self.assertIn("-> ", api)

    def test_get_python_api_contains_parameter_formats(self):
        api = query.get_python_api()
        self.assertIn("edits_json", api)

    def test_get_context_is_idempotent(self):
        first = query.get_context()
        second = query.get_context()
        self.assertEqual(first, second)


# ---------------------------------------------------------------------------
# explorer module
# ---------------------------------------------------------------------------

class TestExplorer(unittest.TestCase):

    def setUp(self):
        _delete_if_exists("TestExplorer")
        explorer.create_folder("TestExplorer")

    def tearDown(self):
        _close_if_open("TestExplorer/hello.txt")
        _close_if_open("TestExplorer/original.txt")
        _close_if_open("TestExplorer/moved.txt")
        _delete_if_exists("TestExplorer")

    def test_create_file(self):
        explorer.create_file("TestExplorer/hello.txt")
        items = file.list_contents("TestExplorer")
        names = [i["name"] for i in items]
        self.assertIn("hello.txt", names)

    def test_create_folder(self):
        explorer.create_folder("TestExplorer/subfolder")
        items = file.list_contents("TestExplorer")
        names = [i["name"] for i in items]
        self.assertIn("subfolder", names)

    def test_select(self):
        explorer.create_file("TestExplorer/hello.txt")
        explorer.select("TestExplorer/hello.txt")

    def test_get_context(self):
        explorer.create_file("TestExplorer/hello.txt")
        explorer.select("TestExplorer/hello.txt")
        ctx = explorer.get_context()
        self.assertIn("selectedResource", ctx)
        self.assertIn("expandedFolders", ctx)

    def test_expand_and_collapse_folder(self):
        explorer.expand_folder("TestExplorer", expanded=True)
        explorer.expand_folder("TestExplorer", expanded=False)

    def test_collapse_all(self):
        explorer.expand_folder("TestExplorer", expanded=True)
        explorer.collapse_all()

    def test_copy(self):
        explorer.create_file("TestExplorer/hello.txt")
        explorer.copy("TestExplorer/hello.txt", "TestExplorer/hello_copy.txt")
        items = file.list_contents("TestExplorer")
        names = [i["name"] for i in items]
        self.assertIn("hello_copy.txt", names)

    def test_move(self):
        explorer.create_file("TestExplorer/original.txt")
        explorer.move("TestExplorer/original.txt", "TestExplorer/moved.txt")
        items = file.list_contents("TestExplorer")
        names = [i["name"] for i in items]
        self.assertIn("moved.txt", names)
        self.assertNotIn("original.txt", names)

    def test_undo_redo(self):
        explorer.create_file("TestExplorer/undo_test.txt")
        explorer.undo()
        explorer.redo()

    def test_delete(self):
        explorer.create_file("TestExplorer/to_delete.txt")
        explorer.delete("TestExplorer/to_delete.txt")
        items = file.list_contents("TestExplorer")
        names = [i["name"] for i in items]
        self.assertNotIn("to_delete.txt", names)

    # NOTE: explorer.rename and explorer.duplicate are interactive (show dialogs)
    # and cannot be tested in an automated script.

    # -- Error cases --

    def test_create_file_invalid_resource_key(self):
        with self.assertRaises(CelError):
            explorer.create_file("\\backslash\\not\\allowed")

    def test_create_folder_invalid_resource_key(self):
        with self.assertRaises(CelError):
            explorer.create_folder("/leading/slash")

    def test_copy_invalid_source(self):
        with self.assertRaises(CelError):
            explorer.copy("\\invalid", "TestExplorer/dest.txt")

    def test_move_invalid_destination(self):
        explorer.create_file("TestExplorer/hello.txt")
        with self.assertRaises(CelError):
            explorer.move("TestExplorer/hello.txt", "\\invalid")

    def test_select_invalid_resource_key(self):
        with self.assertRaises(CelError):
            explorer.select("\\invalid")

    def test_expand_folder_invalid_resource_key(self):
        with self.assertRaises(CelError):
            explorer.expand_folder("\\invalid")

    def test_nested_folder_operations(self):
        explorer.create_folder("TestExplorer/a/b/c")
        explorer.create_file("TestExplorer/a/b/c/deep.txt")
        items = file.list_contents("TestExplorer/a/b/c")
        names = [i["name"] for i in items]
        self.assertIn("deep.txt", names)


# ---------------------------------------------------------------------------
# document module
# ---------------------------------------------------------------------------

class TestDocument(unittest.TestCase):

    def setUp(self):
        _delete_if_exists("TestDocument")
        explorer.create_folder("TestDocument")
        document.write(
            "TestDocument/hello.txt",
            "Hello, World!\nLine 2\nLine 3\nLine 4\nLine 5\n",
        )

    def tearDown(self):
        _close_if_open("TestDocument/hello.txt")
        _close_if_open("TestDocument/test.bin")
        _close_if_open("TestDocument/new_file.txt")
        _delete_if_exists("TestDocument")

    def test_write_and_read(self):
        result = file.read("TestDocument/hello.txt")
        self.assertIn("Hello, World!", result["content"])

    def test_open_and_activate(self):
        document.open("TestDocument/hello.txt")
        document.activate("TestDocument/hello.txt")

    def test_get_context(self):
        document.open("TestDocument/hello.txt")
        ctx = document.get_context()
        self.assertIn("activeDocument", ctx)
        self.assertIn("openDocuments", ctx)
        self.assertIn("sectionCount", ctx)
        resources = [d["resource"] for d in ctx["openDocuments"]]
        self.assertIn("TestDocument/hello.txt", resources)

    def test_apply_edits(self):
        edits = [
            {
                "line": 1,
                "column": 8,
                "endLine": 1,
                "endColumn": 13,
                "newText": "Celbridge",
            }
        ]
        document.apply_edits("TestDocument/hello.txt", json.dumps(edits))
        result = file.read("TestDocument/hello.txt")
        self.assertIn("Celbridge", result["content"])

    def test_apply_edits_on_closed_document_writes_to_disk(self):
        """Edits to a closed document write straight to disk and the disk
        immediately reflects the edit."""
        edits = [
            {
                "line": 1,
                "column": 8,
                "endLine": 1,
                "endColumn": 13,
                "newText": "Celbridge",
            }
        ]
        document.apply_edits("TestDocument/hello.txt", json.dumps(edits))
        disk = file.read("TestDocument/hello.txt")
        self.assertIn("Celbridge", disk["content"])

    def test_apply_edits_open_document_persists_via_disk(self):
        """When the document is open, edits land on disk and the open buffer
        reloads from disk. The response describes the post-edit document and
        the file on disk reflects it immediately."""
        document.open("TestDocument/hello.txt")
        edits = [
            {
                "line": 1,
                "column": 1,
                "endLine": 1,
                "endColumn": -1,
                "newText": "Regression line 1\nRegression line 2",
            }
        ]
        result = document.apply_edits(
            "TestDocument/hello.txt", json.dumps(edits)
        )

        disk = file.read("TestDocument/hello.txt")
        self.assertIn("Regression line 1", disk["content"])
        self.assertIn("Regression line 2", disk["content"])

        disk_line_count = len(disk["content"].splitlines())
        self.assertEqual(result["totalLineCount"], disk_line_count)

        affected = result["affectedLines"][0]
        context_text = "\n".join(affected["contextLines"])
        self.assertIn("Regression line 1", context_text)

    def test_find_replace(self):
        result = document.find_replace(
            "TestDocument/hello.txt",
            search_text="Line 2",
            replace_text="Second Line",
        )
        self.assertGreaterEqual(result["replacementCount"], 1)
        result = file.read("TestDocument/hello.txt")
        self.assertIn("Second Line", result["content"])

    def test_find_replace_open_document_followup_read_sees_replacement(self):
        """When the document is open, find_replace writes to disk and a
        follow-up file_read must see the replacement, not the pre-replace
        editor buffer."""
        document.open("TestDocument/hello.txt")
        result = document.find_replace(
            "TestDocument/hello.txt",
            search_text="Line 2",
            replace_text="Second Line",
        )
        self.assertGreaterEqual(result["replacementCount"], 1)
        disk = file.read("TestDocument/hello.txt")
        self.assertIn("Second Line", disk["content"])

    def test_delete_lines(self):
        result = document.delete_lines(
            "TestDocument/hello.txt", start_line=2, end_line=3
        )
        self.assertIn("deletedFrom", result)
        self.assertIn("totalLineCount", result)
        result = file.read("TestDocument/hello.txt")
        self.assertNotIn("Line 2", result["content"])
        self.assertNotIn("Line 3", result["content"])

    def test_write_binary(self):
        content = base64.b64encode(b"BINARY_TEST_DATA_12345").decode("ascii")
        document.write_binary("TestDocument/test.bin", content)
        result = file.read_binary("TestDocument/test.bin")
        decoded = base64.b64decode(result["base64"])
        self.assertIn(b"BINARY_TEST_DATA_12345", decoded)

    def test_write_replaces_open_document_content(self):
        """When the document is open, write replaces the disk content and
        the open buffer reloads from disk."""
        document.open("TestDocument/hello.txt")
        document.write("TestDocument/hello.txt", "completely new content")
        disk = file.read("TestDocument/hello.txt")
        self.assertEqual(disk["content"].strip(), "completely new content")

    def test_close(self):
        document.open("TestDocument/hello.txt")
        document.close("TestDocument/hello.txt", force_close=True)
        ctx = document.get_context()
        resources = [d["resource"] for d in ctx["openDocuments"]]
        self.assertNotIn("TestDocument/hello.txt", resources)

    # -- Error cases --

    def test_open_invalid_resource_key(self):
        with self.assertRaises(CelError):
            document.open("\\invalid")

    def test_open_invalid_section_index(self):
        with self.assertRaises(CelError):
            document.open("TestDocument/hello.txt", section_index=5)

    def test_activate_invalid_resource_key(self):
        with self.assertRaises(CelError):
            document.activate("\\invalid")

    def test_apply_edits_invalid_resource_key(self):
        with self.assertRaises(CelError):
            document.apply_edits("\\invalid", "[]")

    def test_apply_edits_invalid_json(self):
        with self.assertRaises(CelError):
            document.apply_edits("TestDocument/hello.txt", "not json")

    def test_apply_edits_empty_array(self):
        # Empty edits should succeed without error
        document.apply_edits("TestDocument/hello.txt", "[]")

    def test_apply_edits_auto_serialized_list(self):
        edits = [{"line": 1, "endLine": 1, "newText": "Replaced first line"}]
        document.apply_edits("TestDocument/hello.txt", edits)
        result = file.read("TestDocument/hello.txt")
        self.assertIn("Replaced first line", result["content"])

    def test_delete_lines_invalid_resource_key(self):
        with self.assertRaises(CelError):
            document.delete_lines("\\invalid", start_line=1, end_line=1)

    def test_delete_lines_start_less_than_one(self):
        with self.assertRaises(CelError):
            document.delete_lines(
                "TestDocument/hello.txt", start_line=0, end_line=1
            )

    def test_delete_lines_end_before_start(self):
        with self.assertRaises(CelError):
            document.delete_lines(
                "TestDocument/hello.txt", start_line=3, end_line=1
            )

    def test_find_replace_no_matches(self):
        result = document.find_replace(
            "TestDocument/hello.txt",
            search_text="NONEXISTENT_STRING_XYZ",
            replace_text="replacement",
        )
        self.assertEqual(result["replacementCount"], 0)

    def test_find_replace_regex(self):
        result = document.find_replace(
            "TestDocument/hello.txt",
            search_text=r"Line \d+",
            replace_text="Replaced",
            use_regex=True,
        )
        self.assertGreaterEqual(result["replacementCount"], 1)

    def test_find_replace_case_sensitive(self):
        result = document.find_replace(
            "TestDocument/hello.txt",
            search_text="hello",
            replace_text="Goodbye",
            match_case=True,
        )
        self.assertEqual(result["replacementCount"], 0)

    def test_close_multiple_documents(self):
        document.write("TestDocument/new_file.txt", "temp")
        document.open("TestDocument/hello.txt")
        document.open("TestDocument/new_file.txt")
        document.close(
            ["TestDocument/hello.txt", "TestDocument/new_file.txt"],
            force_close=True,
        )
        ctx = document.get_context()
        resources = [d["resource"] for d in ctx["openDocuments"]]
        self.assertNotIn("TestDocument/hello.txt", resources)
        self.assertNotIn("TestDocument/new_file.txt", resources)

    def test_write_creates_new_file(self):
        document.write("TestDocument/new_file.txt", "brand new content")
        result = file.read("TestDocument/new_file.txt")
        self.assertIn("brand new content", result["content"])

    def test_write_overwrites_existing_file(self):
        document.write("TestDocument/hello.txt", "overwritten")
        result = file.read("TestDocument/hello.txt")
        self.assertIn("overwritten", result["content"])
        self.assertNotIn("Hello, World!", result["content"])

    def test_write_empty_content(self):
        document.write("TestDocument/hello.txt", "")
        result = file.read("TestDocument/hello.txt")
        self.assertEqual(result["content"].strip(), "")

    def test_write_unicode_content(self):
        unicode_text = "Caf\u00e9 \u4e16\u754c \ud83d\ude80\n"
        document.write("TestDocument/hello.txt", unicode_text)
        result = file.read("TestDocument/hello.txt")
        self.assertIn("Caf\u00e9", result["content"])


# ---------------------------------------------------------------------------
# Line-ending preservation
# ---------------------------------------------------------------------------

class TestDocumentLineEndings(unittest.TestCase):
    """Verifies that document edit tools preserve the existing file's line
    endings and trailing-newline state, and that document.write picks the
    platform default when creating a new file."""

    def setUp(self):
        _delete_if_exists("TestLineEndings")
        explorer.create_folder("TestLineEndings")

    def tearDown(self):
        _close_if_open("TestLineEndings/crlf.txt")
        _close_if_open("TestLineEndings/lf.txt")
        _close_if_open("TestLineEndings/no_trailing.txt")
        _close_if_open("TestLineEndings/with_trailing.txt")
        _close_if_open("TestLineEndings/new.txt")
        _delete_if_exists("TestLineEndings")

    def test_apply_edits_preserves_crlf(self):
        _write_with_line_endings(
            "TestLineEndings/crlf.txt",
            "Line 1\nLine 2\nLine 3\n",
            "\r\n",
        )
        edits = [{"line": 2, "endLine": 2, "newText": "Replaced"}]
        document.apply_edits("TestLineEndings/crlf.txt", json.dumps(edits))

        content = file.read("TestLineEndings/crlf.txt")["content"]
        self.assertIn("\r\n", content)
        # Regression for the historical \r\r\n bug \u2014 no doubled CR allowed.
        self.assertNotIn("\r\r", content)
        self.assertNotRegex(content, r"(?<!\r)\n")  # no lone \n alongside CRLF

    def test_apply_edits_preserves_lf(self):
        _write_with_line_endings(
            "TestLineEndings/lf.txt",
            "Line 1\nLine 2\nLine 3\n",
            "\n",
        )
        edits = [{"line": 2, "endLine": 2, "newText": "Replaced"}]
        document.apply_edits("TestLineEndings/lf.txt", json.dumps(edits))

        content = file.read("TestLineEndings/lf.txt")["content"]
        self.assertNotIn("\r", content)

    def test_find_replace_preserves_crlf(self):
        _write_with_line_endings(
            "TestLineEndings/crlf.txt",
            "alpha\nbeta\ngamma\n",
            "\r\n",
        )
        document.find_replace(
            "TestLineEndings/crlf.txt",
            search_text="beta",
            replace_text="BETA",
        )

        content = file.read("TestLineEndings/crlf.txt")["content"]
        self.assertIn("BETA", content)
        self.assertIn("\r\n", content)
        self.assertNotIn("\r\r", content)

    def test_delete_lines_preserves_crlf(self):
        _write_with_line_endings(
            "TestLineEndings/crlf.txt",
            "one\ntwo\nthree\nfour\n",
            "\r\n",
        )
        document.delete_lines(
            "TestLineEndings/crlf.txt", start_line=2, end_line=3
        )

        content = file.read("TestLineEndings/crlf.txt")["content"]
        self.assertNotIn("two", content)
        self.assertNotIn("three", content)
        self.assertIn("\r\n", content)
        self.assertNotIn("\r\r", content)

    def test_write_new_file_uses_platform_default(self):
        # document.write with input that uses \n separators should write the
        # host platform's line endings to a brand-new file.
        document.write(
            "TestLineEndings/new.txt", "first\nsecond\nthird\n"
        )

        content = file.read("TestLineEndings/new.txt")["content"]
        self.assertIn(os.linesep, content)
        if os.linesep == "\r\n":
            self.assertNotRegex(content, r"(?<!\r)\n")
        else:
            self.assertNotIn("\r", content)

    def test_apply_edits_preserves_no_trailing_newline(self):
        _write_with_line_endings(
            "TestLineEndings/no_trailing.txt",
            "alpha\nbeta\ngamma",  # no trailing \n
            "\r\n",
        )
        edits = [{"line": 2, "endLine": 2, "newText": "BETA"}]
        document.apply_edits(
            "TestLineEndings/no_trailing.txt", json.dumps(edits)
        )

        content = file.read("TestLineEndings/no_trailing.txt")["content"]
        self.assertFalse(content.endswith("\n"))
        self.assertFalse(content.endswith("\r"))
        self.assertIn("BETA", content)

    def test_apply_edits_preserves_trailing_newline(self):
        _write_with_line_endings(
            "TestLineEndings/with_trailing.txt",
            "alpha\nbeta\ngamma\n",  # trailing \n
            "\r\n",
        )
        edits = [{"line": 2, "endLine": 2, "newText": "BETA"}]
        document.apply_edits(
            "TestLineEndings/with_trailing.txt", json.dumps(edits)
        )

        content = file.read("TestLineEndings/with_trailing.txt")["content"]
        self.assertTrue(content.endswith("\r\n"))
        self.assertNotIn("\r\r", content)


# ---------------------------------------------------------------------------
# file module
# ---------------------------------------------------------------------------

# Minimal JPEG (SOI + JFIF header + EOI) used by file.read_image tests.
_MINIMAL_JPEG_BYTES = bytes([
    0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
    0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
    0xFF, 0xD9,
])


class TestFile(unittest.TestCase):

    def setUp(self):
        _delete_if_exists("TestFile")
        explorer.create_folder("TestFile")
        document.write(
            "TestFile/hello.txt",
            "Hello, World!\nLine 2\nLine 3\n",
        )
        document.write(
            "TestFile/other.txt",
            "Other file content\n",
        )
        content = base64.b64encode(b"BINARY_TEST_DATA_12345").decode("ascii")
        document.write_binary("TestFile/test.bin", content)
        # A minimal JPEG used by the file.read_image tests below.
        jpeg_b64 = base64.b64encode(_MINIMAL_JPEG_BYTES).decode("ascii")
        document.write_binary("TestFile/sample.jpg", jpeg_b64)

    def tearDown(self):
        _delete_if_exists("TestFile")

    def test_get_tree(self):
        tree = file.get_tree("", depth=3)
        self.assertEqual(tree["type"], "folder")
        self.assertIn("children", tree)

    def test_list_contents(self):
        items = file.list_contents("TestFile")
        names = [i["name"] for i in items]
        self.assertIn("hello.txt", names)

    def test_list_contents_glob(self):
        items = file.list_contents("TestFile", glob="*.txt")
        for item in items:
            self.assertTrue(item["name"].endswith(".txt"))

    def test_get_info(self):
        info = file.get_info("TestFile/hello.txt")
        self.assertEqual(info["type"], "file")
        self.assertIn("size", info)
        self.assertIn("modified", info)
        self.assertTrue(info["isText"])
        self.assertIn("lineCount", info)

    def test_get_info_folder(self):
        info = file.get_info("TestFile")
        self.assertEqual(info["type"], "folder")
        self.assertIn("modified", info)

    def test_read(self):
        result = file.read("TestFile/hello.txt")
        self.assertIn("Hello", result["content"])

    def test_read_with_offset_limit(self):
        result = file.read("TestFile/hello.txt", offset=2, limit=1)
        self.assertIn("Line 2", result["content"])

    def test_read_with_line_numbers(self):
        result = file.read("TestFile/hello.txt", line_numbers=True)
        self.assertIn("1:", result["content"])

    def test_read_binary(self):
        result = file.read_binary("TestFile/test.bin")
        self.assertIn("base64", result)
        self.assertGreater(result["size"], 0)
        decoded = base64.b64decode(result["base64"])
        self.assertIn(b"BINARY_TEST_DATA_12345", decoded)

    def test_read_image_returns_metadata(self):
        # The proxy drops the typed image block; only metadata reaches Python.
        result = file.read_image("TestFile/sample.jpg")
        self.assertEqual(result["resource"], "TestFile/sample.jpg")
        self.assertEqual(result["mimeType"], "image/jpeg")
        self.assertEqual(result["sizeBytes"], len(_MINIMAL_JPEG_BYTES))

    def test_read_image_unsupported_extension_fails(self):
        with self.assertRaisesRegex(CelError, "(?i)does not support extension"):
            file.read_image("TestFile/hello.txt")

    def test_read_image_missing_file_fails(self):
        with self.assertRaisesRegex(CelError, "(?i)file not found"):
            file.read_image("TestFile/no_such_image.png")

    def test_read_image_invalid_resource_key_fails(self):
        with self.assertRaisesRegex(CelError, "(?i)invalid resource key"):
            file.read_image("\\invalid")

    def test_read_many(self):
        result = file.read_many(["TestFile/hello.txt", "TestFile/other.txt"])
        self.assertEqual(len(result["files"]), 2)
        for entry in result["files"]:
            self.assertIn("content", entry)
            self.assertIn("totalLineCount", entry)

    def test_search(self):
        results = file.search("**/*.txt")
        self.assertIsInstance(results, list)
        self.assertTrue(any("hello.txt" in r for r in results))

    def test_grep(self):
        result = file.grep("Hello")
        self.assertGreaterEqual(result["totalMatches"], 1)
        self.assertGreaterEqual(result["totalFiles"], 1)

    def test_grep_with_context(self):
        result = file.grep("Hello", context_lines=1)
        if result["totalMatches"] > 0:
            first_match = result["files"][0]["matches"][0]
            self.assertIn("contextAfter", first_match)

    # -- Error cases --

    def test_read_nonexistent_file(self):
        with self.assertRaises(CelError):
            file.read("TestFile/does_not_exist.txt")

    def test_read_invalid_resource_key(self):
        with self.assertRaises(CelError):
            file.read("\\invalid\\path")

    def test_read_binary_nonexistent_file(self):
        with self.assertRaises(CelError):
            file.read_binary("TestFile/does_not_exist.bin")

    def test_get_info_nonexistent(self):
        with self.assertRaises(CelError):
            file.get_info("TestFile/does_not_exist.txt")

    def test_list_contents_nonexistent_folder(self):
        with self.assertRaises(CelError):
            file.list_contents("NonExistentFolder")

    def test_list_contents_on_file(self):
        with self.assertRaises(CelError):
            file.list_contents("TestFile/hello.txt")

    def test_get_tree_on_file(self):
        with self.assertRaises(CelError):
            file.get_tree("TestFile/hello.txt")

    def test_get_tree_nonexistent(self):
        with self.assertRaises(CelError):
            file.get_tree("NonExistentFolder")

    def test_read_many_invalid_json(self):
        with self.assertRaises(CelError):
            file.read_many("not valid json")

    def test_read_many_empty_array(self):
        with self.assertRaises(CelError):
            file.read_many([])

    def test_read_many_mixed_valid_and_invalid(self):
        result = file.read_many(["TestFile/hello.txt", "TestFile/does_not_exist.txt"])
        entries = result["files"]
        self.assertEqual(len(entries), 2)
        valid_entry = next(e for e in entries if e["resource"] == "TestFile/hello.txt")
        self.assertIn("content", valid_entry)
        invalid_entry = next(e for e in entries if e["resource"] == "TestFile/does_not_exist.txt")
        self.assertIn("error", invalid_entry)

    def test_grep_no_matches(self):
        result = file.grep("NONEXISTENT_STRING_XYZ_123", resource="TestFile")
        self.assertEqual(result["totalMatches"], 0)
        self.assertEqual(result["totalFiles"], 0)

    def test_grep_regex(self):
        result = file.grep(r"Line \d+", use_regex=True)
        self.assertGreaterEqual(result["totalMatches"], 1)

    def test_grep_invalid_regex(self):
        with self.assertRaises(CelError):
            file.grep("[invalid regex", use_regex=True)

    def test_grep_case_sensitive(self):
        result = file.grep("hello", match_case=True, resource="TestFile")
        self.assertEqual(result["totalMatches"], 0)

    def test_grep_case_insensitive(self):
        result = file.grep("hello", match_case=False)
        self.assertGreaterEqual(result["totalMatches"], 1)

    def test_grep_targeted_files(self):
        result = file.grep("Hello", files=["TestFile/hello.txt"])
        self.assertGreaterEqual(result["totalMatches"], 1)

    def test_grep_whole_word(self):
        result = file.grep("Hello", whole_word=True)
        self.assertGreaterEqual(result["totalMatches"], 1)

    def test_read_offset_beyond_file(self):
        result = file.read("TestFile/hello.txt", offset=9999)
        self.assertEqual(result["content"], "")

    def test_get_tree_depth_zero(self):
        tree = file.get_tree("", depth=0)
        self.assertEqual(tree["type"], "folder")

    def test_search_no_matches(self):
        results = file.search("**/*.nonexistent_extension_xyz")
        self.assertIsInstance(results, list)
        self.assertEqual(len(results), 0)

    def test_list_contents_glob_no_matches(self):
        items = file.list_contents("TestFile", glob="*.nonexistent_xyz")
        self.assertEqual(len(items), 0)


# ---------------------------------------------------------------------------
# package module
# ---------------------------------------------------------------------------

class TestPackage(unittest.TestCase):

    def setUp(self):
        _delete_if_exists("TestPackage")
        _delete_if_exists("TestPackageExtract")
        _delete_if_exists("test_archive.zip")
        _delete_if_exists("test_archive_filtered.zip")
        explorer.create_folder("TestPackage")
        document.write("TestPackage/file.txt", "archive content\n")

    def tearDown(self):
        _delete_if_exists("TestPackage")
        _delete_if_exists("TestPackageExtract")
        _delete_if_exists("test_archive.zip")
        _delete_if_exists("test_archive_filtered.zip")
        try:
            package.uninstall("test-integration-pkg")
        except Exception:
            pass

    def test_archive(self):
        result = package.archive("TestPackage", "test_archive.zip", overwrite=True)
        self.assertGreater(result["entries"], 0)
        self.assertGreater(result["size"], 0)

    def test_archive_filtered(self):
        result = package.archive(
            "TestPackage",
            "test_archive_filtered.zip",
            include="*.txt",
            overwrite=True,
        )
        self.assertGreaterEqual(result["entries"], 1)

    def test_unarchive(self):
        package.archive("TestPackage", "test_archive.zip", overwrite=True)
        explorer.create_folder("TestPackageExtract")
        result = package.unarchive(
            "test_archive.zip", "TestPackageExtract", overwrite=True
        )
        self.assertGreater(result["entries"], 0)

    def test_list(self):
        result = package.list()
        self.assertIsInstance(result, list)

    @unittest.skip("package.uninstall tool not yet implemented; test also needs packages/test-integration-pkg fixture")
    def test_publish_install_uninstall(self):
        pub_result = package.publish("TestPackage", "test-integration-pkg")
        self.assertEqual(pub_result["packageName"], "test-integration-pkg")
        self.assertGreater(pub_result["entries"], 0)

        inst_result = package.install("test-integration-pkg")
        self.assertEqual(inst_result["packageName"], "test-integration-pkg")

        uninst_result = package.uninstall("test-integration-pkg")
        self.assertEqual(uninst_result["packageName"], "test-integration-pkg")

    # -- Error cases --

    def test_archive_invalid_source(self):
        with self.assertRaises(CelError):
            package.archive("\\invalid", "test_archive.zip")

    def test_archive_invalid_destination(self):
        with self.assertRaises(CelError):
            package.archive("TestPackage", "\\invalid")

    def test_unarchive_invalid_archive(self):
        with self.assertRaises(CelError):
            package.unarchive("\\invalid", "TestPackageExtract")

    def test_install_nonexistent_package(self):
        with self.assertRaises(CelError):
            package.install("nonexistent-package-xyz-999")

    def test_install_invalid_package_name(self):
        with self.assertRaises(CelError):
            package.install("INVALID PACKAGE NAME!")

    @unittest.skip("package.uninstall tool not yet implemented")
    def test_uninstall_not_installed(self):
        with self.assertRaises(CelError):
            package.uninstall("not-installed-package-xyz")

    @unittest.skip("package.uninstall tool not yet implemented")
    def test_uninstall_invalid_package_name(self):
        with self.assertRaises(CelError):
            package.uninstall("INVALID!")

    def test_publish_invalid_package_name(self):
        with self.assertRaises(CelError):
            package.publish("TestPackage", "INVALID NAME!")

    def test_publish_nonexistent_source(self):
        with self.assertRaises(CelError):
            package.publish("NonExistentFolder", "test-pkg")


# ---------------------------------------------------------------------------
# webview module
# ---------------------------------------------------------------------------

# HTML content used by every TestWebView case. Self-contained so the test
# does not depend on any project-shipped page; the inline <script> emits
# entries at every console level so get_console can be exercised.
_WEBVIEW_TEST_HTML = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>WebView Tools Test</title>
<style>
  body { font-family: sans-serif; margin: 2rem; }
  .warn { color: #b15500; }
  #status { padding: 0.5rem; border: 1px solid #999; }
</style>
</head>
<body>
  <h1>WebView Tools Test</h1>
  <p>Self-contained page used by the cel.test() webview suite.</p>
  <section id="controls">
    <button id="run-btn" aria-label="Run task">Run</button>
    <button class="warn" aria-label="Cancel task">Cancel</button>
    <input id="name-input" type="text" placeholder="Your name" />
    <textarea id="notes-textarea"></textarea>
    <select id="size-select" aria-label="Choose size">
      <option value="s">Small</option>
      <option value="m" selected>Medium</option>
      <option value="l">Large</option>
    </select>
  </section>
  <section id="messages">
    <p>hello world</p>
    <p>goodbye world</p>
    <p class="warn">a warning paragraph</p>
  </section>
  <div id="status">idle</div>
  <script>
    console.log('boot: webview test page loaded');
    console.info('info-level message');
    console.warn('warn-level message');
    console.debug('debug-level message');
    try {
      JSON.parse('{not valid json');
    } catch (e) {
      console.error('caught parse error:', e.message);
    }
    // Click handler used by webview_click test (asserts data-clicked flips).
    document.getElementById('run-btn').addEventListener('click', function () {
      this.setAttribute('data-clicked', 'true');
      document.getElementById('status').textContent = 'ran';
    });
    // Same-origin fetch so webview_get_network has an entry to capture.
    fetch('webview-test-network-fixture.json').catch(function () { /* ignored */ });
  </script>
</body>
</html>
"""


class TestWebView(unittest.TestCase):
    """End-to-end tests for the webview_* tools.

    Writes a self-contained HTML page, opens it, and exercises every tool.
    Eval-dependent cases are skipped automatically when the
    webview-dev-tools-eval feature flag is off.
    """

    test_resource = "TestWebView/page.html"
    unopened_resource = "TestWebView/unopened.html"
    eval_enabled = False

    @classmethod
    def setUpClass(cls):
        flags = app.get_status().get("featureFlags", {})
        cls.eval_enabled = flags.get("webview-dev-tools-eval", False)

    def setUp(self):
        _delete_if_exists("TestWebView")
        explorer.create_folder("TestWebView")
        document.write(self.test_resource, _WEBVIEW_TEST_HTML)
        document.write(
            self.unopened_resource,
            "<!doctype html><html><body>unopened</body></html>",
        )
        document.open(self.test_resource, activate=True)
        # The bridge's content-ready gate covers most of the navigation wait,
        # but a small grace period lets the inline <script> run so console
        # messages are present when the first get_console call fires.
        time.sleep(0.5)

    def tearDown(self):
        _close_if_open(self.test_resource)
        _delete_if_exists("TestWebView")

    # -- webview.reload --

    def test_reload_returns_ok(self):
        result = webview.reload(self.test_resource)
        self.assertEqual(result, "ok")
        # Reload resets the readiness gate. Wait for the next NavigationCompleted
        # so the next test in the class does not race the reload.
        time.sleep(0.5)

    def test_reload_unopened_resource_fails(self):
        with self.assertRaisesRegex(CelError, "(?i)no tool-bridge-eligible"):
            webview.reload("does/not/exist.html")

    def test_reload_path_traversal_rejected(self):
        with self.assertRaisesRegex(CelError, "(?i)invalid resource key"):
            webview.reload("../escape.html")

    def test_reload_real_unopened_file_fails(self):
        with self.assertRaisesRegex(CelError, "(?i)no tool-bridge-eligible"):
            webview.reload(self.unopened_resource)

    def test_reload_empty_resource_key_fails_with_eligibility_error(self):
        # Empty string passes IsValidKey (it represents the project root) but
        # the project root has no WebView registered, so the call falls through
        # to the same eligibility error as any other unregistered key.
        with self.assertRaisesRegex(CelError, "(?i)no tool-bridge-eligible"):
            webview.reload("")

    # -- webview.eval (gated by webview-dev-tools-eval) --

    def test_eval_arithmetic(self):
        if not self.eval_enabled:
            self.skipTest("webview-dev-tools-eval flag is off")
        self.assertEqual(webview.eval(self.test_resource, "1 + 1"), 2)

    def test_eval_reads_document_title(self):
        if not self.eval_enabled:
            self.skipTest("webview-dev-tools-eval flag is off")
        title = webview.eval(self.test_resource, "document.title")
        self.assertEqual(title, "WebView Tools Test")

    def test_eval_unparseable_returns_none(self):
        # ExecuteScriptAsync returns null silently when the script throws or
        # fails to parse — the host does not surface JS errors. Lock that
        # contract in so a future change is caught.
        if not self.eval_enabled:
            self.skipTest("webview-dev-tools-eval flag is off")
        self.assertIsNone(
            webview.eval(self.test_resource, "this is not valid javascript")
        )

    def test_eval_empty_expression_rejected(self):
        if not self.eval_enabled:
            self.skipTest("webview-dev-tools-eval flag is off")
        with self.assertRaisesRegex(CelError, "must not be empty"):
            webview.eval(self.test_resource, "")

    # -- webview.get_html --

    def test_get_html_returns_outer_html(self):
        result = webview.get_html(self.test_resource)
        self.assertIn("<h1>", result["html"])

    def test_get_html_scopes_to_selector(self):
        result = webview.get_html(self.test_resource, selector="#controls")
        self.assertIn("run-btn", result["html"])
        self.assertNotIn("<h1>", result["html"])

    def test_get_html_redacts_script_bodies(self):
        result = webview.get_html(self.test_resource)
        self.assertNotIn("console.log", result["html"])
        self.assertIn("omitted", result["html"])

    def test_get_html_redacts_style_bodies(self):
        result = webview.get_html(self.test_resource)
        self.assertNotIn("font-family", result["html"])

    def test_get_html_missing_selector_fails(self):
        with self.assertRaisesRegex(CelError, "no element matches"):
            webview.get_html(self.test_resource, selector="#no-such-thing")

    # -- webview.query --

    def test_query_by_role_returns_all_matches(self):
        result = webview.query(self.test_resource, role="button")
        self.assertEqual(result["mode"], "role")
        self.assertEqual(result["totalMatches"], 2)

    def test_query_role_plus_name_filters_to_one(self):
        result = webview.query(self.test_resource, role="button", name="Run")
        self.assertEqual(result["totalMatches"], 1)
        self.assertIn("run", result["elements"][0]["accessibleName"].lower())

    def test_query_by_visible_text(self):
        result = webview.query(self.test_resource, text="hello")
        self.assertEqual(result["mode"], "text")
        self.assertEqual(result["totalMatches"], 1)

    def test_query_by_selector(self):
        result = webview.query(self.test_resource, selector="p")
        self.assertGreaterEqual(result["totalMatches"], 3)

    def test_query_role_heading_finds_h1(self):
        result = webview.query(self.test_resource, role="heading")
        self.assertEqual(result["totalMatches"], 1)
        self.assertEqual(result["elements"][0]["tag"], "h1")

    def test_query_no_mode_rejected(self):
        with self.assertRaisesRegex(CelError, "exactly one"):
            webview.query(self.test_resource)

    def test_query_ambiguous_mode_rejected(self):
        with self.assertRaisesRegex(CelError, "exactly one"):
            webview.query(self.test_resource, role="button", selector="button")

    def test_query_bad_selector_syntax_rejected(self):
        with self.assertRaisesRegex(CelError, "invalid selector"):
            webview.query(self.test_resource, selector="<<<")

    # -- webview.inspect --

    def test_inspect_returns_metadata(self):
        result = webview.inspect(self.test_resource, "#run-btn")
        self.assertEqual(result["tag"], "button")
        self.assertEqual(result["role"], "button")
        self.assertEqual(result["accessibleName"], "Run task")
        self.assertIn("computedStyles", result)
        self.assertIn("children", result)

    def test_inspect_returns_unique_selector(self):
        result = webview.inspect(self.test_resource, "#size-select")
        self.assertEqual(result["selector"], "#size-select")

    def test_inspect_missing_selector_fails(self):
        with self.assertRaisesRegex(CelError, "no element matches"):
            webview.inspect(self.test_resource, "#nope")

    def test_inspect_bad_selector_syntax_rejected(self):
        with self.assertRaisesRegex(CelError, "invalid selector"):
            webview.inspect(self.test_resource, "<<<")

    # -- webview.get_console --

    def test_get_console_captures_boot_messages(self):
        result = webview.get_console(self.test_resource, tail=200)
        self.assertTrue(
            any("boot:" in " ".join(e["args"]) for e in result["entries"])
        )

    def test_get_console_suppresses_debug_by_default(self):
        result = webview.get_console(self.test_resource, tail=200)
        self.assertFalse(
            any(e["level"] == "debug" for e in result["entries"])
        )

    def test_get_console_includes_debug_when_requested(self):
        result = webview.get_console(
            self.test_resource, tail=200, include_debug=True
        )
        self.assertTrue(
            any(e["level"] == "debug" for e in result["entries"])
        )

    def test_get_console_surfaces_caught_errors(self):
        result = webview.get_console(self.test_resource, tail=200)
        self.assertTrue(
            any(
                e["level"] == "error"
                and "parse" in " ".join(e["args"]).lower()
                for e in result["entries"]
            )
        )

    def test_get_console_since_filters_older_entries(self):
        if not self.eval_enabled:
            self.skipTest("webview-dev-tools-eval flag is off")
        baseline = webview.get_console(self.test_resource, tail=200)
        if not baseline["entries"]:
            self.skipTest("no console entries to take a checkpoint from")
        checkpoint = max(e["timestampMs"] for e in baseline["entries"])
        webview.eval(
            self.test_resource, "console.log('after-checkpoint marker')"
        )
        result = webview.get_console(
            self.test_resource, tail=200, since_timestamp_ms=checkpoint
        )
        self.assertTrue(
            all(e["timestampMs"] > checkpoint for e in result["entries"])
        )
        self.assertTrue(
            any(
                "after-checkpoint" in " ".join(e["args"])
                for e in result["entries"]
            )
        )

    def test_get_console_buffer_survives_reload(self):
        if not self.eval_enabled:
            self.skipTest("webview-dev-tools-eval flag is off")

        webview.eval(
            self.test_resource,
            "console.log('cel-test-pre-reload-marker')",
        )

        webview.reload(self.test_resource)
        time.sleep(0.5)  # wait for the navigation to complete

        webview.eval(
            self.test_resource,
            "console.log('cel-test-post-reload-marker')",
        )

        result = webview.get_console(self.test_resource, tail=500)
        joined_args = " ".join(
            " ".join(e["args"]) for e in result["entries"]
        )
        self.assertIn(
            "cel-test-pre-reload-marker",
            joined_args,
            f"pre-reload marker missing after reload. entries: {result['entries']}",
        )
        self.assertIn(
            "cel-test-post-reload-marker",
            joined_args,
            f"post-reload marker missing after reload. entries: {result['entries']}",
        )

    def test_get_network_buffer_survives_reload(self):
        # The shim records network entries on fetch resolution, not on call,
        # so the test sleeps briefly after each fetch.
        if not self.eval_enabled:
            self.skipTest("webview-dev-tools-eval flag is off")

        webview.eval(
            self.test_resource,
            "fetch('cel-test-pre-reload-fetch.json').catch(function(){})",
        )
        time.sleep(0.3)

        webview.reload(self.test_resource)
        time.sleep(0.5)

        webview.eval(
            self.test_resource,
            "fetch('cel-test-post-reload-fetch.json').catch(function(){})",
        )
        time.sleep(0.3)

        result = webview.get_network(self.test_resource, tail=200)
        urls = [entry["url"] for entry in result["entries"]]
        self.assertTrue(
            any("cel-test-pre-reload-fetch" in url for url in urls),
            f"pre-reload fetch missing from network buffer after reload. URLs: {urls}",
        )
        self.assertTrue(
            any("cel-test-post-reload-fetch" in url for url in urls),
            f"post-reload fetch missing from network buffer after reload. URLs: {urls}",
        )

    # -- webview.click --

    def test_click_runs_handler_and_returns_metadata(self):
        result = webview.click(self.test_resource, "#run-btn")
        self.assertEqual(result["selector"], "#run-btn")
        self.assertEqual(result["tag"], "button")
        # Programmatic events are always isTrusted = false.
        self.assertFalse(result["isTrusted"])

        # The page's click handler flips data-clicked to "true".
        post = webview.inspect(self.test_resource, "#run-btn")
        self.assertEqual(post["attributes"].get("data-clicked"), "true")

    def test_click_missing_selector_fails(self):
        with self.assertRaisesRegex(CelError, "(?i)no element matches"):
            webview.click(self.test_resource, "#no-such-element")

    def test_click_empty_selector_rejected(self):
        with self.assertRaisesRegex(CelError, "(?i)non-empty selector"):
            webview.click(self.test_resource, "")

    def test_click_bad_selector_syntax_rejected(self):
        with self.assertRaisesRegex(CelError, "(?i)invalid selector"):
            webview.click(self.test_resource, "<<<")

    # -- webview.fill --

    def test_fill_sets_input_value(self):
        # `value` attribute does not reflect property writes, so check the
        # response's read-back value rather than inspecting the attribute.
        result = webview.fill(self.test_resource, "#name-input", "Alice")
        self.assertEqual(result["tag"], "input")
        self.assertEqual(result["value"], "Alice")

    def test_fill_sets_textarea_value(self):
        result = webview.fill(
            self.test_resource, "#notes-textarea", "line one\nline two"
        )
        self.assertEqual(result["tag"], "textarea")
        self.assertEqual(result["value"], "line one\nline two")

    def test_fill_sets_select_value(self):
        result = webview.fill(self.test_resource, "#size-select", "l")
        self.assertEqual(result["tag"], "select")
        self.assertEqual(result["value"], "l")

    def test_fill_missing_selector_fails(self):
        with self.assertRaisesRegex(CelError, "(?i)no element matches"):
            webview.fill(self.test_resource, "#no-such-element", "value")

    def test_fill_empty_selector_rejected(self):
        with self.assertRaisesRegex(CelError, "(?i)non-empty selector"):
            webview.fill(self.test_resource, "", "value")

    # -- webview.get_network --

    def test_get_network_captures_test_page_fetch(self):
        # The inline <script> issues a fetch on load; capture records it
        # regardless of response status.
        result = webview.get_network(self.test_resource, tail=50)
        self.assertIn("entries", result)
        self.assertIn("returned", result)
        self.assertIn("totalAccumulated", result)

        urls = [entry["url"] for entry in result["entries"]]
        self.assertTrue(
            any("webview-test-network-fixture.json" in url for url in urls),
            f"expected fetch URL not captured. URLs seen: {urls}",
        )

    def test_get_network_entry_has_required_fields(self):
        result = webview.get_network(self.test_resource, tail=50)
        if not result["entries"]:
            self.skipTest("no captured entries to inspect")
        entry = result["entries"][0]
        for field in ("id", "type", "method", "url", "startTimeMs"):
            self.assertIn(field, entry)
        # Headers/bodies keys are emitted with null values when not opted in.
        self.assertIsNone(entry.get("requestHeaders"))
        self.assertIsNone(entry.get("responseBody"))

    def test_get_network_include_headers_opts_in(self):
        opted_out = webview.get_network(self.test_resource, tail=50)
        opted_out_populated = any(
            entry.get("requestHeaders") is not None
            or entry.get("responseHeaders") is not None
            for entry in opted_out["entries"]
        )
        self.assertFalse(
            opted_out_populated,
            "headers should be null when include_headers=False",
        )

        opted_in = webview.get_network(
            self.test_resource, tail=50, include_headers=True
        )
        if not opted_in["entries"]:
            self.skipTest("no captured entries to inspect")
        # Early-failure rows may legitimately omit headers, so accept any.
        self.assertTrue(
            any(
                entry.get("requestHeaders") is not None
                or entry.get("responseHeaders") is not None
                for entry in opted_in["entries"]
            )
        )

    # -- webview.screenshot --

    def test_screenshot_default_returns_metadata_only(self):
        # The proxy strips the typed image block; only metadata reaches Python.
        result = webview.screenshot(self.test_resource)
        self.assertEqual(result["format"], "jpeg")
        self.assertGreater(result["sizeBytes"], 0)
        # `resource` is omitted from the JSON when null (WhenWritingNull).
        self.assertIsNone(result.get("resource"))
        self.assertTrue(result["imageReturned"])

    def test_screenshot_save_to_resource_writes_file(self):
        save_resource = "TestWebView/captured.png"
        result = webview.screenshot(
            self.test_resource,
            save_to=save_resource,
            return_image=False,
            format="png",
        )
        self.assertEqual(result["format"], "png")
        self.assertEqual(result["resource"], save_resource)
        self.assertFalse(result["imageReturned"])

        info = file.get_info(save_resource)
        self.assertEqual(info["type"], "file")
        self.assertGreater(info["size"], 0)

    def test_screenshot_no_output_combination_rejected(self):
        with self.assertRaisesRegex(CelError, "(?i)discard the captured image"):
            webview.screenshot(self.test_resource, return_image=False)

    def test_screenshot_format_extension_mismatch_rejected(self):
        with self.assertRaisesRegex(CelError, "(?i)does not match format"):
            webview.screenshot(
                self.test_resource,
                save_to="TestWebView/mismatch.jpg",
                return_image=False,
                format="png",
            )


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main():
    global app, file, query, explorer, document, package, webview

    import celbridge
    app = celbridge.app
    file = celbridge.file
    query = celbridge.query
    explorer = celbridge.explorer
    document = celbridge.document
    package = celbridge.package
    webview = celbridge.webview

    print("\n" + "=" * 60)
    print("Celbridge MCP Integration Test")
    print("=" * 60)

    loader = unittest.TestLoader()
    suite = unittest.TestSuite()

    test_classes = [
        TestApp,
        TestQuery,
        TestExplorer,
        TestDocument,
        TestDocumentLineEndings,
        TestFile,
        TestPackage,
        TestWebView,
    ]
    for cls in test_classes:
        suite.addTests(loader.loadTestsFromTestCase(cls))

    runner = ProgressTestRunner()
    result = runner.run(suite)

    # Summary
    print("\n" + "=" * 60)
    passed = result.testsRun - len(result.failures) - len(result.errors)
    if result.failures or result.errors:
        print(
            f"Results: \033[92m{passed} passed\033[0m, "
            f"\033[91m{len(result.failures)} failed\033[0m, "
            f"\033[91m{len(result.errors)} errors\033[0m"
        )
    else:
        print(f"Results: \033[92m{passed} passed\033[0m, 0 failed, 0 errors")

    if result.failures:
        print("\n\033[91mFailures:\033[0m")
        for test, traceback_str in result.failures:
            print(f"  \033[91m{test}\033[0m")
            print(f"    {traceback_str.strip().splitlines()[-1]}")

    if result.errors:
        print("\n\033[91mErrors:\033[0m")
        for test, traceback_str in result.errors:
            print(f"  \033[91m{test}\033[0m")
            print(f"    {traceback_str.strip().splitlines()[-1]}")

    print("=" * 60)

