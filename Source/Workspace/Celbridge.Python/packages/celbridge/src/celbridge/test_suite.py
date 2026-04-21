"""
Celbridge MCP Integration Test Script
Tests all available tools across all celbridge modules:
  - celbridge.app
  - celbridge.file
  - celbridge.query
  - celbridge.explorer
  - celbridge.document
  - celbridge.package

Includes both happy-path tests and adversarial error-handling tests.

Usage (IPython REPL):
    cel.test()
"""

import json
import base64
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
            open_document=False,
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
        document.apply_edits(
            "TestDocument/hello.txt", json.dumps(edits), open_document=False
        )
        result = file.read("TestDocument/hello.txt")
        self.assertIn("Celbridge", result["content"])

    def test_apply_edits_open_document_persists_and_reports_post_edit_state(self):
        """Regression: when open_document=True the edit routes through the
        editor. The response must describe the document AFTER the edit and
        the file on disk must reflect it immediately (without waiting for
        the save timer). Previously the response showed pre-edit line count
        and the disk file was stale, forcing agents to retry or re-read."""
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
            "TestDocument/hello.txt", json.dumps(edits), open_document=True
        )

        # Disk reflects the edit immediately — ForceSave flushed it.
        disk = file.read("TestDocument/hello.txt")
        self.assertIn("Regression line 1", disk["content"])
        self.assertIn("Regression line 2", disk["content"])

        # Response totalLineCount matches the actual post-edit line count.
        disk_line_count = len(disk["content"].splitlines())
        self.assertEqual(result["totalLineCount"], disk_line_count)

        # contextLines describe the post-edit content around the affected region.
        affected = result["affectedLines"][0]
        context_text = "\n".join(affected["contextLines"])
        self.assertIn("Regression line 1", context_text)

    def test_find_replace(self):
        result = document.find_replace(
            "TestDocument/hello.txt",
            search_text="Line 2",
            replace_text="Second Line",
            open_document=False,
        )
        self.assertGreaterEqual(result["replacementCount"], 1)
        result = file.read("TestDocument/hello.txt")
        self.assertIn("Second Line", result["content"])

    def test_delete_lines(self):
        result = document.delete_lines(
            "TestDocument/hello.txt", start_line=2, end_line=3, open_document=False
        )
        self.assertIn("deletedFrom", result)
        self.assertIn("totalLineCount", result)
        result = file.read("TestDocument/hello.txt")
        self.assertNotIn("Line 2", result["content"])
        self.assertNotIn("Line 3", result["content"])

    def test_write_binary(self):
        content = base64.b64encode(b"BINARY_TEST_DATA_12345").decode("ascii")
        document.write_binary("TestDocument/test.bin", content, open_document=False)
        result = file.read_binary("TestDocument/test.bin")
        decoded = base64.b64decode(result["base64"])
        self.assertIn(b"BINARY_TEST_DATA_12345", decoded)

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
        document.apply_edits("TestDocument/hello.txt", "[]", open_document=False)

    def test_apply_edits_auto_serialized_list(self):
        edits = [{"line": 1, "endLine": 1, "newText": "Replaced first line"}]
        document.apply_edits("TestDocument/hello.txt", edits, open_document=False)
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
            open_document=False,
        )
        self.assertEqual(result["replacementCount"], 0)

    def test_find_replace_regex(self):
        result = document.find_replace(
            "TestDocument/hello.txt",
            search_text=r"Line \d+",
            replace_text="Replaced",
            use_regex=True,
            open_document=False,
        )
        self.assertGreaterEqual(result["replacementCount"], 1)

    def test_find_replace_case_sensitive(self):
        result = document.find_replace(
            "TestDocument/hello.txt",
            search_text="hello",
            replace_text="Goodbye",
            match_case=True,
            open_document=False,
        )
        self.assertEqual(result["replacementCount"], 0)

    def test_close_multiple_documents(self):
        document.write("TestDocument/new_file.txt", "temp", open_document=False)
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
        document.write(
            "TestDocument/new_file.txt", "brand new content", open_document=False
        )
        result = file.read("TestDocument/new_file.txt")
        self.assertIn("brand new content", result["content"])

    def test_write_overwrites_existing_file(self):
        document.write(
            "TestDocument/hello.txt", "overwritten", open_document=False
        )
        result = file.read("TestDocument/hello.txt")
        self.assertIn("overwritten", result["content"])
        self.assertNotIn("Hello, World!", result["content"])

    def test_write_empty_content(self):
        document.write("TestDocument/hello.txt", "", open_document=False)
        result = file.read("TestDocument/hello.txt")
        self.assertEqual(result["content"].strip(), "")

    def test_write_unicode_content(self):
        unicode_text = "Caf\u00e9 \u4e16\u754c \ud83d\ude80\n"
        document.write(
            "TestDocument/hello.txt", unicode_text, open_document=False
        )
        result = file.read("TestDocument/hello.txt")
        self.assertIn("Caf\u00e9", result["content"])


# ---------------------------------------------------------------------------
# file module
# ---------------------------------------------------------------------------

class TestFile(unittest.TestCase):

    def setUp(self):
        _delete_if_exists("TestFile")
        explorer.create_folder("TestFile")
        document.write(
            "TestFile/hello.txt",
            "Hello, World!\nLine 2\nLine 3\n",
            open_document=False,
        )
        document.write(
            "TestFile/other.txt",
            "Other file content\n",
            open_document=False,
        )
        content = base64.b64encode(b"BINARY_TEST_DATA_12345").decode("ascii")
        document.write_binary("TestFile/test.bin", content, open_document=False)

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
        document.write(
            "TestPackage/file.txt", "archive content\n", open_document=False
        )

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
# Main
# ---------------------------------------------------------------------------

def main():
    global app, file, query, explorer, document, package

    import celbridge
    app = celbridge.app
    file = celbridge.file
    query = celbridge.query
    explorer = celbridge.explorer
    document = celbridge.document
    package = celbridge.package

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
        TestFile,
        TestPackage,
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

