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
  - celbridge.spreadsheet

Includes both happy-path tests and adversarial error-handling tests.

Usage (IPython REPL):
    cel.test()                  # run every test class
    cel.test("TestSpreadsheet") # run only the named class(es)
    cel.test("Spreadsheet")     # substring match against class names
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
spreadsheet = None


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
    """Write a file with explicit line endings, bypassing file.write's
    platform-default conversion. Used by the line-ending preservation tests
    to set up a file with known endings regardless of host OS."""
    text = text_with_lf.replace("\n", line_ending)
    encoded = base64.b64encode(text.encode("utf-8")).decode("ascii")
    file.write_binary(resource, encoded)


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
# document module (editor-tab tools only)
# ---------------------------------------------------------------------------

class TestDocument(unittest.TestCase):

    def setUp(self):
        _delete_if_exists("TestDocument")
        explorer.create_folder("TestDocument")
        file.write(
            "TestDocument/hello.txt",
            "Hello, World!\nLine 2\nLine 3\nLine 4\nLine 5\n",
        )

    def tearDown(self):
        _close_if_open("TestDocument/hello.txt")
        _close_if_open("TestDocument/new_file.txt")
        _delete_if_exists("TestDocument")

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

    def test_close(self):
        document.open("TestDocument/hello.txt")
        document.close("TestDocument/hello.txt", force_close=True)
        ctx = document.get_context()
        resources = [d["resource"] for d in ctx["openDocuments"]]
        self.assertNotIn("TestDocument/hello.txt", resources)

    def test_close_multiple_documents(self):
        file.write("TestDocument/new_file.txt", "temp")
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


# ---------------------------------------------------------------------------
# file edit tools (write, write_binary, apply_edits, find_replace, delete_lines)
# ---------------------------------------------------------------------------

class TestFileEdit(unittest.TestCase):
    """File-content edit tools. These write straight to disk; if a document
    is open, its buffer reloads from disk as a side effect."""

    def setUp(self):
        _delete_if_exists("TestFileEdit")
        explorer.create_folder("TestFileEdit")
        file.write(
            "TestFileEdit/hello.txt",
            "Hello, World!\nLine 2\nLine 3\nLine 4\nLine 5\n",
        )

    def tearDown(self):
        _close_if_open("TestFileEdit/hello.txt")
        _close_if_open("TestFileEdit/test.bin")
        _close_if_open("TestFileEdit/new_file.txt")
        _delete_if_exists("TestFileEdit")

    def test_write_and_read(self):
        result = file.read("TestFileEdit/hello.txt")
        self.assertIn("Hello, World!", result["content"])

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
        file.apply_edits("TestFileEdit/hello.txt", json.dumps(edits))
        result = file.read("TestFileEdit/hello.txt")
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
        file.apply_edits("TestFileEdit/hello.txt", json.dumps(edits))
        disk = file.read("TestFileEdit/hello.txt")
        self.assertIn("Celbridge", disk["content"])

    def test_apply_edits_open_document_persists_via_disk(self):
        """When the document is open, edits land on disk and the open buffer
        reloads from disk. The response describes the post-edit document and
        the file on disk reflects it immediately."""
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
        self.assertIn("Regression line 1", disk["content"])
        self.assertIn("Regression line 2", disk["content"])

        disk_line_count = len(disk["content"].splitlines())
        self.assertEqual(result["totalLineCount"], disk_line_count)

        affected = result["affectedLines"][0]
        context_text = "\n".join(affected["contextLines"])
        self.assertIn("Regression line 1", context_text)

    def test_find_replace(self):
        result = file.find_replace(
            "TestFileEdit/hello.txt",
            search_text="Line 2",
            replace_text="Second Line",
        )
        self.assertGreaterEqual(result["replacementCount"], 1)
        result = file.read("TestFileEdit/hello.txt")
        self.assertIn("Second Line", result["content"])

    def test_find_replace_open_document_followup_read_sees_replacement(self):
        """When the document is open, find_replace writes to disk and a
        follow-up file_read must see the replacement, not the pre-replace
        editor buffer."""
        document.open("TestFileEdit/hello.txt")
        result = file.find_replace(
            "TestFileEdit/hello.txt",
            search_text="Line 2",
            replace_text="Second Line",
        )
        self.assertGreaterEqual(result["replacementCount"], 1)
        disk = file.read("TestFileEdit/hello.txt")
        self.assertIn("Second Line", disk["content"])

    def test_delete_lines(self):
        result = file.delete_lines(
            "TestFileEdit/hello.txt", start_line=2, end_line=3
        )
        self.assertIn("deletedFrom", result)
        self.assertIn("totalLineCount", result)
        result = file.read("TestFileEdit/hello.txt")
        self.assertNotIn("Line 2", result["content"])
        self.assertNotIn("Line 3", result["content"])

    def test_write_binary(self):
        content = base64.b64encode(b"BINARY_TEST_DATA_12345").decode("ascii")
        file.write_binary("TestFileEdit/test.bin", content)
        result = file.read_binary("TestFileEdit/test.bin")
        decoded = base64.b64decode(result["base64"])
        self.assertIn(b"BINARY_TEST_DATA_12345", decoded)

    def test_write_replaces_open_document_content(self):
        """When the document is open, write replaces the disk content and
        the open buffer reloads from disk."""
        document.open("TestFileEdit/hello.txt")
        file.write("TestFileEdit/hello.txt", "completely new content")
        disk = file.read("TestFileEdit/hello.txt")
        self.assertEqual(disk["content"].strip(), "completely new content")

    # -- Error cases --

    def test_apply_edits_invalid_resource_key(self):
        with self.assertRaises(CelError):
            file.apply_edits("\\invalid", "[]")

    def test_apply_edits_invalid_json(self):
        with self.assertRaises(CelError):
            file.apply_edits("TestFileEdit/hello.txt", "not json")

    def test_apply_edits_empty_array(self):
        # Empty edits should succeed without error
        file.apply_edits("TestFileEdit/hello.txt", "[]")

    def test_apply_edits_auto_serialized_list(self):
        edits = [{"line": 1, "endLine": 1, "newText": "Replaced first line"}]
        file.apply_edits("TestFileEdit/hello.txt", edits)
        result = file.read("TestFileEdit/hello.txt")
        self.assertIn("Replaced first line", result["content"])

    def test_delete_lines_invalid_resource_key(self):
        with self.assertRaises(CelError):
            file.delete_lines("\\invalid", start_line=1, end_line=1)

    def test_delete_lines_start_less_than_one(self):
        with self.assertRaises(CelError):
            file.delete_lines(
                "TestFileEdit/hello.txt", start_line=0, end_line=1
            )

    def test_delete_lines_end_before_start(self):
        with self.assertRaises(CelError):
            file.delete_lines(
                "TestFileEdit/hello.txt", start_line=3, end_line=1
            )

    def test_find_replace_no_matches(self):
        result = file.find_replace(
            "TestFileEdit/hello.txt",
            search_text="NONEXISTENT_STRING_XYZ",
            replace_text="replacement",
        )
        self.assertEqual(result["replacementCount"], 0)

    def test_find_replace_regex(self):
        result = file.find_replace(
            "TestFileEdit/hello.txt",
            search_text=r"Line \d+",
            replace_text="Replaced",
            use_regex=True,
        )
        self.assertGreaterEqual(result["replacementCount"], 1)

    def test_find_replace_case_sensitive(self):
        result = file.find_replace(
            "TestFileEdit/hello.txt",
            search_text="hello",
            replace_text="Goodbye",
            match_case=True,
        )
        self.assertEqual(result["replacementCount"], 0)

    def test_write_creates_new_file(self):
        file.write("TestFileEdit/new_file.txt", "brand new content")
        result = file.read("TestFileEdit/new_file.txt")
        self.assertIn("brand new content", result["content"])

    def test_write_overwrites_existing_file(self):
        file.write("TestFileEdit/hello.txt", "overwritten")
        result = file.read("TestFileEdit/hello.txt")
        self.assertIn("overwritten", result["content"])
        self.assertNotIn("Hello, World!", result["content"])

    def test_write_empty_content(self):
        file.write("TestFileEdit/hello.txt", "")
        result = file.read("TestFileEdit/hello.txt")
        self.assertEqual(result["content"].strip(), "")

    def test_write_unicode_content(self):
        unicode_text = "Caf\u00e9 \u4e16\u754c \ud83d\ude80\n"
        file.write("TestFileEdit/hello.txt", unicode_text)
        result = file.read("TestFileEdit/hello.txt")
        self.assertIn("Caf\u00e9", result["content"])


# ---------------------------------------------------------------------------
# Line-ending preservation
# ---------------------------------------------------------------------------

class TestFileLineEndings(unittest.TestCase):
    """Verifies that file edit tools preserve the existing file's line endings
    and trailing-newline state, and that file.write picks the platform default
    when creating a new file."""

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
        file.apply_edits("TestLineEndings/crlf.txt", json.dumps(edits))

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
        file.apply_edits("TestLineEndings/lf.txt", json.dumps(edits))

        content = file.read("TestLineEndings/lf.txt")["content"]
        self.assertNotIn("\r", content)

    def test_find_replace_preserves_crlf(self):
        _write_with_line_endings(
            "TestLineEndings/crlf.txt",
            "alpha\nbeta\ngamma\n",
            "\r\n",
        )
        file.find_replace(
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
        file.delete_lines(
            "TestLineEndings/crlf.txt", start_line=2, end_line=3
        )

        content = file.read("TestLineEndings/crlf.txt")["content"]
        self.assertNotIn("two", content)
        self.assertNotIn("three", content)
        self.assertIn("\r\n", content)
        self.assertNotIn("\r\r", content)

    def test_write_new_file_uses_platform_default(self):
        # file.write with input that uses \n separators should write the
        # host platform's line endings to a brand-new file.
        file.write(
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
        file.apply_edits(
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
        file.apply_edits(
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
        file.write(
            "TestFile/hello.txt",
            "Hello, World!\nLine 2\nLine 3\n",
        )
        file.write(
            "TestFile/other.txt",
            "Other file content\n",
        )
        content = base64.b64encode(b"BINARY_TEST_DATA_12345").decode("ascii")
        file.write_binary("TestFile/test.bin", content)
        # A minimal JPEG used by the file.read_image tests below.
        jpeg_b64 = base64.b64encode(_MINIMAL_JPEG_BYTES).decode("ascii")
        file.write_binary("TestFile/sample.jpg", jpeg_b64)

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
        file.write("TestPackage/file.txt", "archive content\n")

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
        file.write(self.test_resource, _WEBVIEW_TEST_HTML)
        file.write(
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
        with self.assertRaisesRegex(CelError, "(?i)not open in the editor"):
            webview.reload("does/not/exist.html")

    def test_reload_path_traversal_rejected(self):
        with self.assertRaisesRegex(CelError, "(?i)invalid resource key"):
            webview.reload("../escape.html")

    def test_reload_real_unopened_file_fails(self):
        with self.assertRaisesRegex(CelError, "(?i)not open in the editor"):
            webview.reload(self.unopened_resource)

    def test_reload_empty_resource_key_fails_with_unsupported_error(self):
        # Empty string passes IsValidKey (it represents the project root) but
        # the project root has no WebView registered, so the call falls through
        # to the same unsupported error as any other unopened key.
        with self.assertRaisesRegex(CelError, "(?i)not open in the editor"):
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
# spreadsheet module
# ---------------------------------------------------------------------------

class TestSpreadsheet(unittest.TestCase):

    _WORKBOOK = "TestSpreadsheet/sheet.xlsx"

    def setUp(self):
        _delete_if_exists("TestSpreadsheet")
        explorer.create_folder("TestSpreadsheet")
        explorer.create_file(self._WORKBOOK)

    def tearDown(self):
        _close_if_open(self._WORKBOOK)
        _delete_if_exists("TestSpreadsheet")

    # -- spreadsheet_get_context --

    def test_get_context(self):
        result = spreadsheet.get_context()
        self.assertIn("A1 notation", result)

    # -- spreadsheet_get_info --

    def test_get_info_empty_workbook(self):
        info = spreadsheet.get_info(self._WORKBOOK)
        self.assertEqual(len(info["sheets"]), 1)
        sheet = info["sheets"][0]
        self.assertEqual(sheet["name"], "Sheet1")
        self.assertEqual(sheet["position"], 1)
        self.assertEqual(sheet["rowCount"], 0)
        self.assertIsNone(sheet.get("usedRange"))
        self.assertEqual(sheet["frozenRows"], 0)
        self.assertEqual(sheet["frozenColumns"], 0)

    def test_get_info_reports_frozen_panes(self):
        spreadsheet.freeze_panes(self._WORKBOOK, "Sheet1", rows=2, columns=1)
        info = spreadsheet.get_info(self._WORKBOOK)
        sheet = info["sheets"][0]
        self.assertEqual(sheet["frozenRows"], 2)
        self.assertEqual(sheet["frozenColumns"], 1)

    # -- spreadsheet_read_sheet --

    def test_read_sheet_empty(self):
        result = spreadsheet.read_sheet(self._WORKBOOK, "Sheet1")
        self.assertEqual(result["totalRowCount"], 0)
        self.assertEqual(result["rows"], [])

    def test_read_sheet_with_data(self):
        spreadsheet.import_csv(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "month,sales\nJan,100\nFeb,200\n"}],
        )
        result = spreadsheet.read_sheet(self._WORKBOOK, "Sheet1", headers=True)
        self.assertEqual(result["totalRowCount"], 2)
        first_row = result["rows"][0]
        self.assertEqual(first_row["month"], "Jan")
        self.assertEqual(first_row["sales"], "100")

    # -- spreadsheet_export_csv --

    def test_export_csv_inline_empty(self):
        result = spreadsheet.export_csv(self._WORKBOOK, "Sheet1")
        self.assertEqual(result, "")

    def test_export_csv_inline_with_data(self):
        spreadsheet.append_rows(self._WORKBOOK, "Sheet1", [["A", "B"], ["C", "D"]])
        result = spreadsheet.export_csv(self._WORKBOOK, "Sheet1")
        self.assertIsInstance(result, str)
        self.assertTrue(result.endswith("\r\n"))
        self.assertIn("A", result)

    def test_export_csv_destination(self):
        spreadsheet.append_rows(self._WORKBOOK, "Sheet1", [["x", "y"], ["1", "2"]])
        dest = "TestSpreadsheet/export.csv"
        result = spreadsheet.export_csv(self._WORKBOOK, "Sheet1", destination=dest)
        self.assertIsInstance(result, dict)
        self.assertEqual(result["rowCount"], 2)
        self.assertEqual(result["columnCount"], 2)
        self.assertGreater(result["byteCount"], 0)
        self.assertEqual(result["destination"], dest)
        info = file.get_info(dest)
        self.assertEqual(info["type"], "file")

    def test_export_csv_invalid_destination(self):
        with self.assertRaises(CelError):
            spreadsheet.export_csv(
                self._WORKBOOK, "Sheet1", destination="\\invalid\\path"
            )

    # -- spreadsheet_write_cells --

    def test_write_cells(self):
        result = spreadsheet.write_cells(
            self._WORKBOOK, "Sheet1", [{"cell": "B2", "value": 99}]
        )
        self.assertEqual(result["cellCount"], 1)
        read_result = spreadsheet.read_sheet(self._WORKBOOK, "Sheet1", range="B2")
        self.assertEqual(read_result["rows"][0][0], 99.0)

    # -- spreadsheet_append_rows --

    def test_append_rows(self):
        result = spreadsheet.append_rows(
            self._WORKBOOK, "Sheet1", [["Jan", 100], ["Feb", 200]]
        )
        self.assertEqual(result["appendedRowCount"], 2)
        self.assertEqual(result["firstRow"], 1)
        self.assertEqual(result["lastRow"], 2)

    # -- spreadsheet_import_csv --

    def test_import_csv_multi_sheet(self):
        spreadsheet.add_sheets(self._WORKBOOK, ["Q1", "Q2"])
        result = spreadsheet.import_csv(
            self._WORKBOOK,
            [
                {"sheet": "Q1", "csvText": "month,total\nJan,100\n"},
                {"sheet": "Q2", "csvText": "month,total\nApr,200\n"},
            ],
        )
        self.assertEqual(result["importsApplied"], 2)
        self.assertEqual(result["totalRowCount"], 4)  # header + 1 data row per sheet
        self.assertEqual(result["sheetsCreated"], 0)

    # -- spreadsheet_add_sheets --

    def test_add_sheets(self):
        result = spreadsheet.add_sheets(self._WORKBOOK, ["Data", "Summary"])
        self.assertIn("Data", result["sheets"])
        self.assertIn("Summary", result["sheets"])

    def test_add_sheets_duplicate_in_batch_fails(self):
        with self.assertRaises(CelError):
            spreadsheet.add_sheets(self._WORKBOOK, ["NewSheet", "NewSheet"])

    def test_add_sheets_collision_with_existing_fails(self):
        with self.assertRaises(CelError):
            spreadsheet.add_sheets(self._WORKBOOK, ["Sheet1"])

    # -- spreadsheet_remove_sheet --

    def test_remove_sheet(self):
        spreadsheet.add_sheets(self._WORKBOOK, ["Extra"])
        result = spreadsheet.remove_sheet(self._WORKBOOK, "Extra")
        self.assertEqual(result["sheet"], "Extra")
        info = spreadsheet.get_info(self._WORKBOOK)
        names = [s["name"] for s in info["sheets"]]
        self.assertNotIn("Extra", names)

    def test_remove_last_sheet_fails(self):
        with self.assertRaises(CelError):
            spreadsheet.remove_sheet(self._WORKBOOK, "Sheet1")

    # -- spreadsheet_rename_sheet --

    def test_rename_sheet(self):
        result = spreadsheet.rename_sheet(self._WORKBOOK, "Sheet1", "Sales")
        self.assertEqual(result["previousName"], "Sheet1")
        self.assertEqual(result["newName"], "Sales")
        info = spreadsheet.get_info(self._WORKBOOK)
        names = [s["name"] for s in info["sheets"]]
        self.assertIn("Sales", names)
        self.assertNotIn("Sheet1", names)

    # -- spreadsheet_move_sheet --

    def test_move_sheet(self):
        spreadsheet.add_sheets(self._WORKBOOK, ["A", "B", "C"])
        result = spreadsheet.move_sheet(self._WORKBOOK, "C", 1)
        self.assertEqual(result["position"], 1)
        info = spreadsheet.get_info(self._WORKBOOK)
        self.assertEqual(info["sheets"][0]["name"], "C")

    # -- formula recalculation --

    def test_formula_recalculates_on_save(self):
        spreadsheet.write_cells(
            self._WORKBOOK, "Sheet1",
            [{"cell": "A1", "value": 10}, {"cell": "A2", "value": 20}],
        )
        spreadsheet.write_cells(
            self._WORKBOOK, "Sheet1",
            [{"cell": "A3", "value": "=SUM(A1:A2)", "isFormula": True}],
        )
        result = spreadsheet.read_sheet(self._WORKBOOK, "Sheet1", range="A3")
        self.assertEqual(result["rows"][0][0], 30.0)

    # -- spreadsheet_format_ranges --

    def test_format_ranges_text_and_background(self):
        edits = [
            {
                "sheet": "Sheet1",
                "range": "A1",
                "format": {
                    "textFormat": {"bold": True},
                    "backgroundColor": "#FF0000",
                },
            }
        ]
        result = spreadsheet.format_ranges(self._WORKBOOK, edits)
        self.assertEqual(result["editsApplied"], 1)
        self.assertGreater(result["propertiesApplied"], 0)

    def test_format_ranges_borders(self):
        edits = [
            {
                "sheet": "Sheet1",
                "range": "A1",
                "format": {
                    "borders": {
                        "top": {"style": "SOLID", "color": "#000000"},
                        "bottom": {"style": "DASHED", "color": "#888888"},
                    }
                },
            }
        ]
        result = spreadsheet.format_ranges(self._WORKBOOK, edits)
        self.assertEqual(result["editsApplied"], 1)

    def test_format_ranges_column_width_and_autofit(self):
        edits = [
            {
                "sheet": "Sheet1",
                "range": "A",
                "format": {"columnWidth": 20, "autoFitColumns": True},
            }
        ]
        result = spreadsheet.format_ranges(self._WORKBOOK, edits)
        self.assertTrue(result["autoFitApplied"])

    def test_format_ranges_unknown_color_raises(self):
        with self.assertRaises(CelError):
            spreadsheet.format_ranges(
                self._WORKBOOK,
                [
                    {
                        "sheet": "Sheet1",
                        "range": "A1",
                        "format": {"backgroundColor": "not-a-color"},
                    }
                ],
            )

    # -- spreadsheet_read_format --

    def test_read_format_round_trips_through_format_ranges(self):
        spreadsheet.format_ranges(
            self._WORKBOOK,
            [
                {
                    "sheet": "Sheet1",
                    "range": "A1",
                    "format": {"textFormat": {"bold": True}, "backgroundColor": "#FFFF00"},
                }
            ],
        )
        format_grid = spreadsheet.read_format(self._WORKBOOK, "Sheet1", "A1")
        self.assertIn("rows", format_grid)
        cell_spec = format_grid["rows"][0][0]
        self.assertTrue(cell_spec.get("textFormat", {}).get("bold", False))
        result = spreadsheet.format_ranges(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "range": "B1", "format": cell_spec}],
        )
        self.assertEqual(result["editsApplied"], 1)

    # -- spreadsheet_freeze_panes --

    def test_freeze_panes_rows_columns_and_clear(self):
        result = spreadsheet.freeze_panes(self._WORKBOOK, "Sheet1", rows=1)
        self.assertEqual(result["rows"], 1)
        self.assertEqual(result["columns"], 0)

        result = spreadsheet.freeze_panes(self._WORKBOOK, "Sheet1", rows=1, columns=2)
        self.assertEqual(result["rows"], 1)
        self.assertEqual(result["columns"], 2)

        result = spreadsheet.freeze_panes(self._WORKBOOK, "Sheet1", rows=0, columns=0)
        self.assertEqual(result["rows"], 0)
        self.assertEqual(result["columns"], 0)

    # -- spreadsheet_set_active_view --

    def test_set_active_view_persists_sheet_and_selection(self):
        spreadsheet.add_sheets(self._WORKBOOK, ["Summary"])
        result = spreadsheet.set_active_view(self._WORKBOOK, "Summary", range="A1")
        self.assertEqual(result["sheet"], "Summary")
        self.assertEqual(result["range"], "A1")

    # -- spreadsheet_delete --

    def test_delete_rows_shifts_remaining_rows_up(self):
        spreadsheet.import_csv(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "a\nb\nc\nd\ne\n"}],
        )
        result = spreadsheet.delete(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "range": "2:3"}],
        )
        self.assertEqual(result["deletedRowCount"], 2)

        rows = spreadsheet.read_sheet(self._WORKBOOK, "Sheet1")["rows"]
        self.assertEqual([row[0] for row in rows], ["a", "d", "e"])

    def test_delete_uses_original_coordinates_across_operations(self):
        spreadsheet.import_csv(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "\n".join(f"row{i}" for i in range(1, 13)) + "\n"}],
        )
        result = spreadsheet.delete(
            self._WORKBOOK,
            [
                {"sheet": "Sheet1", "range": "3:5"},
                {"sheet": "Sheet1", "range": "10"},
            ],
        )
        self.assertEqual(result["deletedRowCount"], 4)

        rows = spreadsheet.read_sheet(self._WORKBOOK, "Sheet1")["rows"]
        self.assertEqual(
            [row[0] for row in rows],
            ["row1", "row2", "row6", "row7", "row8", "row9", "row11", "row12"],
        )

    # -- spreadsheet_clear --

    def test_clear_range_leaves_other_cells_alone(self):
        spreadsheet.import_csv(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "a,b,c\n1,2,3\n4,5,6\n"}],
        )
        result = spreadsheet.clear(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "range": "B2:C2"}],
        )
        self.assertEqual(result["cellCount"], 2)

        # import_csv stores fields as text, so the round-tripped values are strings.
        rows = spreadsheet.read_sheet(self._WORKBOOK, "Sheet1")["rows"]
        self.assertEqual(rows[0], ["a", "b", "c"])
        self.assertEqual(rows[1][0], "1")
        self.assertIsNone(rows[1][1])
        self.assertIsNone(rows[1][2])
        self.assertEqual(rows[2], ["4", "5", "6"])

    def test_clear_empty_range_clears_entire_sheet(self):
        spreadsheet.import_csv(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "a,b\n1,2\n"}],
        )
        spreadsheet.clear(self._WORKBOOK, [{"sheet": "Sheet1", "range": ""}])

        result = spreadsheet.read_sheet(self._WORKBOOK, "Sheet1")
        self.assertEqual(result["totalRowCount"], 0)

    # -- spreadsheet_get_active_view --

    def test_get_active_view_round_trips_through_set_active_view(self):
        spreadsheet.add_sheets(self._WORKBOOK, ["Summary"])
        spreadsheet.set_active_view(
            self._WORKBOOK,
            "Summary",
            range="B2:D4",
            active_cell="C3",
            top_left_cell="A1",
        )

        view = spreadsheet.get_active_view(self._WORKBOOK)
        self.assertEqual(view["sheet"], "Summary")
        self.assertEqual(view["range"], "B2:D4")
        self.assertEqual(view["activeCell"], "C3")
        self.assertEqual(view["topLeftCell"], "A1")

        # Round-trip the get response back through set_active_view; the workbook
        # state should still match.
        spreadsheet.set_active_view(
            self._WORKBOOK,
            view["sheet"],
            range=view["range"],
            active_cell=view["activeCell"],
            top_left_cell=view["topLeftCell"],
        )

        view_again = spreadsheet.get_active_view(self._WORKBOOK)
        self.assertEqual(view_again, view)

    def test_set_active_view_multi_range_round_trips(self):
        spreadsheet.add_sheets(self._WORKBOOK, ["Multi"])
        spreadsheet.set_active_view(
            self._WORKBOOK,
            "Multi",
            ranges_json=json.dumps(["A7:B8", "A12:B13"]),
            active_cell="A7",
        )

        view = spreadsheet.get_active_view(self._WORKBOOK)
        self.assertEqual(view["sheet"], "Multi")
        self.assertEqual(view["range"], "A7:B8")
        self.assertEqual(view["ranges"], ["A7:B8", "A12:B13"])
        self.assertEqual(view["activeCell"], "A7")

        # Round-trip the ranges back through set_active_view.
        spreadsheet.set_active_view(
            self._WORKBOOK,
            view["sheet"],
            ranges_json=json.dumps(view["ranges"]),
            active_cell=view["activeCell"],
        )

        view_again = spreadsheet.get_active_view(self._WORKBOOK)
        self.assertEqual(view_again["ranges"], ["A7:B8", "A12:B13"])
        self.assertEqual(view_again["activeCell"], "A7")

    def test_get_active_view_single_range_includes_ranges_array(self):
        spreadsheet.add_sheets(self._WORKBOOK, ["Solo"])
        spreadsheet.set_active_view(
            self._WORKBOOK,
            "Solo",
            range="C5:D7",
        )

        view = spreadsheet.get_active_view(self._WORKBOOK)
        self.assertEqual(view["range"], "C5:D7")
        self.assertEqual(view["ranges"], ["C5:D7"])

    # -- spreadsheet_insert --

    def test_insert_rows_shifts_existing_rows_down(self):
        spreadsheet.import_csv(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "row1\nrow2\nrow3\n"}],
        )
        result = spreadsheet.insert(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "range": "2:3"}],
        )
        self.assertEqual(result["insertedRowCount"], 2)

        rows = spreadsheet.read_sheet(self._WORKBOOK, "Sheet1")["rows"]
        self.assertEqual(rows[0], ["row1"])
        self.assertEqual(rows[3], ["row2"])
        self.assertEqual(rows[4], ["row3"])

    def test_insert_columns_shifts_existing_columns_right(self):
        spreadsheet.write_cells(
            self._WORKBOOK,
            "Sheet1",
            [
                {"cell": "A1", "value": "col1"},
                {"cell": "B1", "value": "col2"},
                {"cell": "C1", "value": "col3"},
            ],
        )
        result = spreadsheet.insert(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "range": "B"}],
        )
        self.assertEqual(result["insertedColumnCount"], 1)

        rows = spreadsheet.read_sheet(self._WORKBOOK, "Sheet1")["rows"]
        # B1 is now blank; the original col2 has shifted to C1.
        self.assertEqual(rows[0][0], "col1")
        self.assertIsNone(rows[0][1])
        self.assertEqual(rows[0][2], "col2")

    # -- spreadsheet_find --

    def test_find_returns_matches_across_sheets(self):
        spreadsheet.add_sheets(self._WORKBOOK, ["Other"])
        spreadsheet.write_cells(
            self._WORKBOOK,
            "Sheet1",
            [{"cell": "A1", "value": "Hello World"}],
        )
        spreadsheet.write_cells(
            self._WORKBOOK,
            "Other",
            [{"cell": "B5", "value": "Hello, friend"}],
        )

        result = spreadsheet.find(self._WORKBOOK, "Hello")
        self.assertEqual(result["matchCount"], 2)
        cells = sorted((m["sheet"], m["cell"]) for m in result["matches"])
        self.assertEqual(cells, [("Other", "B5"), ("Sheet1", "A1")])

    def test_find_match_entire_cell_contents_only(self):
        spreadsheet.write_cells(
            self._WORKBOOK,
            "Sheet1",
            [
                {"cell": "A1", "value": "foo"},
                {"cell": "A2", "value": "foobar"},
            ],
        )
        result = spreadsheet.find(
            self._WORKBOOK,
            "foo",
            sheet="Sheet1",
            match_entire_cell_contents=True,
        )
        self.assertEqual(result["matchCount"], 1)
        self.assertEqual(result["matches"][0]["cell"], "A1")

    # -- spreadsheet_sort --

    def test_sort_orders_rows_by_column(self):
        spreadsheet.import_csv(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "Charlie\nAlpha\nBravo\n"}],
        )
        result = spreadsheet.sort(
            self._WORKBOOK,
            "Sheet1",
            "A1:A3",
            [{"column": "A", "ascending": True}],
        )
        self.assertEqual(result["rowCount"], 3)

        rows = spreadsheet.read_sheet(self._WORKBOOK, "Sheet1")["rows"]
        self.assertEqual([row[0] for row in rows], ["Alpha", "Bravo", "Charlie"])

    def test_sort_with_header_row_keeps_header_in_place(self):
        spreadsheet.import_csv(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "Name\nCharlie\nAlpha\nBravo\n"}],
        )
        result = spreadsheet.sort(
            self._WORKBOOK,
            "Sheet1",
            "A1:A4",
            [{"column": "A", "ascending": True}],
            has_header_row=True,
        )
        self.assertEqual(result["rowCount"], 3)

        rows = spreadsheet.read_sheet(self._WORKBOOK, "Sheet1")["rows"]
        self.assertEqual([row[0] for row in rows], ["Name", "Alpha", "Bravo", "Charlie"])

    # -- spreadsheet_duplicate_sheet --

    def test_duplicate_sheet_copies_values_and_appends(self):
        spreadsheet.write_cells(
            self._WORKBOOK,
            "Sheet1",
            [{"cell": "A1", "value": "header"}, {"cell": "A2", "value": 42}],
        )
        result = spreadsheet.duplicate_sheet(
            self._WORKBOOK,
            "Sheet1",
            "Sheet1Copy",
        )
        self.assertEqual(result["newSheet"], "Sheet1Copy")
        self.assertEqual(result["position"], 2)

        info = spreadsheet.get_info(self._WORKBOOK)
        sheet_names = [s["name"] for s in info["sheets"]]
        self.assertEqual(sheet_names, ["Sheet1", "Sheet1Copy"])

        rows = spreadsheet.read_sheet(self._WORKBOOK, "Sheet1Copy")["rows"]
        self.assertEqual(rows[0][0], "header")
        self.assertEqual(rows[1][0], 42)

    def test_duplicate_sheet_collision_fails(self):
        spreadsheet.add_sheets(self._WORKBOOK, ["Existing"])
        with self.assertRaises(CelError):
            spreadsheet.duplicate_sheet(
                self._WORKBOOK,
                "Sheet1",
                "Existing",
            )

    # -- spreadsheet_set_auto_filter --

    def test_set_auto_filter_applies_to_used_range(self):
        spreadsheet.import_csv(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "name,total\nAlpha,1\nBravo,2\n"}],
        )
        result = spreadsheet.set_auto_filter(self._WORKBOOK, "Sheet1")
        self.assertTrue(result["enabled"])
        self.assertEqual(result["filterRange"], "A1:B3")

    def test_set_auto_filter_disabled_clears_existing(self):
        spreadsheet.import_csv(
            self._WORKBOOK,
            [{"sheet": "Sheet1", "csvText": "name,total\nAlpha,1\n"}],
        )
        spreadsheet.set_auto_filter(self._WORKBOOK, "Sheet1")

        result = spreadsheet.set_auto_filter(
            self._WORKBOOK,
            "Sheet1",
            enabled=False,
        )
        self.assertFalse(result["enabled"])
        self.assertEqual(result["filterRange"], "")

    # -- spreadsheet_set_conditional_formatting --

    def test_set_conditional_formatting_adds_greater_than_rule(self):
        spreadsheet.write_cells(
            self._WORKBOOK,
            "Sheet1",
            [{"cell": "A1", "value": 50}, {"cell": "A2", "value": 150}],
        )
        result = spreadsheet.set_conditional_formatting(
            self._WORKBOOK,
            "Sheet1",
            "A1:A2",
            [{"type": "greaterThan", "value": 100, "backgroundColor": "#FFCCCC"}],
        )
        self.assertEqual(result["rulesApplied"], 1)
        self.assertEqual(result["rulesRemoved"], 0)

    def test_set_conditional_formatting_clear_existing_replaces_rules(self):
        spreadsheet.write_cells(
            self._WORKBOOK,
            "Sheet1",
            [{"cell": "A1", "value": 50}],
        )
        spreadsheet.set_conditional_formatting(
            self._WORKBOOK,
            "Sheet1",
            "A1:A10",
            [{"type": "greaterThan", "value": 0, "backgroundColor": "#FFCCCC"}],
        )

        replace = spreadsheet.set_conditional_formatting(
            self._WORKBOOK,
            "Sheet1",
            "A1:A10",
            [{"type": "lessThan", "value": 10, "backgroundColor": "#CCFFCC"}],
            clear_existing=True,
        )
        self.assertEqual(replace["rulesApplied"], 1)
        self.assertEqual(replace["rulesRemoved"], 1)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main(class_filter=None):
    global app, file, query, explorer, document, package, webview, spreadsheet

    import celbridge
    app = celbridge.app
    file = celbridge.file
    query = celbridge.query
    explorer = celbridge.explorer
    document = celbridge.document
    package = celbridge.package
    webview = celbridge.webview
    spreadsheet = celbridge.spreadsheet

    all_test_classes = [
        TestApp,
        TestQuery,
        TestExplorer,
        TestDocument,
        TestFileEdit,
        TestFileLineEndings,
        TestFile,
        TestPackage,
        TestWebView,
        TestSpreadsheet,
    ]

    test_classes = _select_test_classes(all_test_classes, class_filter)
    if not test_classes:
        names = ", ".join(cls.__name__ for cls in all_test_classes)
        raise ValueError(
            f"No test classes match {class_filter!r}. Available classes: {names}"
        )

    print("\n" + "=" * 60)
    print("Celbridge MCP Integration Test")
    if class_filter is not None:
        running = ", ".join(cls.__name__ for cls in test_classes)
        print(f"Filter: {class_filter!r} -> {running}")
    print("=" * 60)

    loader = unittest.TestLoader()
    suite = unittest.TestSuite()
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
            _print_failure_detail(traceback_str)

    if result.errors:
        print("\n\033[91mErrors:\033[0m")
        for test, traceback_str in result.errors:
            print(f"  \033[91m{test}\033[0m")
            _print_failure_detail(traceback_str)

    print("=" * 60)


def _select_test_classes(all_test_classes, class_filter):
    if class_filter is None:
        return list(all_test_classes)

    if isinstance(class_filter, str):
        names = [class_filter]
    else:
        names = list(class_filter)

    selected = []
    seen = set()
    for name in names:
        for cls in all_test_classes:
            if cls.__name__ == name or name.lower() in cls.__name__.lower():
                if cls.__name__ not in seen:
                    selected.append(cls)
                    seen.add(cls.__name__)
    return selected


def _print_failure_detail(traceback_str):
    # The full traceback is verbose, but the AssertionError block (and the
    # surrounding diff that unittest writes for dict/list/string mismatches)
    # is what's actually informative. Print every line from the first
    # AssertionError to the end so we don't truncate diagnostic context.
    lines = traceback_str.rstrip().splitlines()
    start_index = 0
    for index, line in enumerate(lines):
        if "AssertionError" in line:
            start_index = index
            break
    for line in lines[start_index:]:
        print(f"    {line}")

