import base64
import json

import pytest

from celbridge.cel_proxy import CelError

from .helpers import delete_if_exists


# Minimal JPEG (SOI + JFIF header + EOI) used by file.read_image tests.
_MINIMAL_JPEG_BYTES = bytes([
    0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
    0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
    0xFF, 0xD9,
])


@pytest.fixture(autouse=True)
def workspace(explorer, file):
    delete_if_exists(explorer, "TestFile")
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
    yield
    delete_if_exists(explorer, "TestFile")


class TestFile:

    def test_get_tree(self, file):
        tree = file.get_tree("", depth=3)
        assert tree["type"] == "folder"
        assert "children" in tree

    def test_list_contents(self, file):
        items = file.list_contents("TestFile")
        names = [i["name"] for i in items]
        assert "hello.txt" in names

    def test_list_contents_glob(self, file):
        items = file.list_contents("TestFile", glob="*.txt")
        for item in items:
            assert item["name"].endswith(".txt")

    def test_get_info(self, file):
        info = file.get_info("TestFile/hello.txt")
        assert info["type"] == "file"
        assert "size" in info
        assert "modified" in info
        assert info["isText"]
        assert "lineCount" in info

    def test_get_info_folder(self, file):
        info = file.get_info("TestFile")
        assert info["type"] == "folder"
        assert "modified" in info

    def test_read(self, file):
        result = file.read("TestFile/hello.txt")
        assert "Hello" in result["content"]

    def test_read_with_offset_limit(self, file):
        result = file.read("TestFile/hello.txt", offset=2, limit=1)
        assert "Line 2" in result["content"]

    def test_read_with_line_numbers(self, file):
        result = file.read("TestFile/hello.txt", line_numbers=True)
        assert "1:" in result["content"]

    def test_read_binary(self, file):
        result = file.read_binary("TestFile/test.bin")
        assert "base64" in result
        assert result["size"] > 0
        decoded = base64.b64decode(result["base64"])
        assert b"BINARY_TEST_DATA_12345" in decoded

    def test_read_image_returns_metadata(self, file):
        # The proxy drops the typed image block; only metadata reaches Python.
        result = file.read_image("TestFile/sample.jpg")
        # Tool responses emit resource keys in canonical "root:path" form.
        assert result["resource"] == "project:TestFile/sample.jpg"
        assert result["mimeType"] == "image/jpeg"
        assert result["sizeBytes"] == len(_MINIMAL_JPEG_BYTES)

    def test_read_image_unsupported_extension_fails(self, file):
        with pytest.raises(CelError, match="(?i)does not support extension"):
            file.read_image("TestFile/hello.txt")

    def test_read_image_missing_file_fails(self, file):
        with pytest.raises(CelError, match="(?i)file not found"):
            file.read_image("TestFile/no_such_image.png")

    def test_read_image_invalid_resource_key_fails(self, file):
        with pytest.raises(CelError, match="(?i)invalid resource key"):
            file.read_image("\\invalid")

    def test_read_many(self, file):
        result = file.read_many(["TestFile/hello.txt", "TestFile/other.txt"])
        assert len(result["files"]) == 2
        for entry in result["files"]:
            assert "content" in entry
            assert "totalLineCount" in entry

    def test_search(self, file):
        results = file.search("**/*.txt")
        assert isinstance(results, list)
        assert any("hello.txt" in r for r in results)

    def test_grep(self, file):
        result = file.grep("Hello")
        assert result["totalMatches"] >= 1
        assert result["totalFiles"] >= 1

    def test_grep_with_context(self, file):
        result = file.grep("Hello", context_lines=1)
        if result["totalMatches"] > 0:
            first_match = result["files"][0]["matches"][0]
            assert "contextAfter" in first_match

    def test_read_nonexistent_file(self, file):
        with pytest.raises(CelError):
            file.read("TestFile/does_not_exist.txt")

    def test_read_invalid_resource_key(self, file):
        with pytest.raises(CelError):
            file.read("\\invalid\\path")

    def test_read_binary_nonexistent_file(self, file):
        with pytest.raises(CelError):
            file.read_binary("TestFile/does_not_exist.bin")

    def test_get_info_nonexistent(self, file):
        with pytest.raises(CelError):
            file.get_info("TestFile/does_not_exist.txt")

    def test_list_contents_nonexistent_folder(self, file):
        with pytest.raises(CelError):
            file.list_contents("NonExistentFolder")

    def test_list_contents_on_file(self, file):
        with pytest.raises(CelError):
            file.list_contents("TestFile/hello.txt")

    def test_get_tree_on_file(self, file):
        with pytest.raises(CelError):
            file.get_tree("TestFile/hello.txt")

    def test_get_tree_nonexistent(self, file):
        with pytest.raises(CelError):
            file.get_tree("NonExistentFolder")

    def test_read_many_invalid_json(self, file):
        with pytest.raises(CelError):
            file.read_many("not valid json")

    def test_read_many_empty_array(self, file):
        with pytest.raises(CelError):
            file.read_many([])

    def test_read_many_mixed_valid_and_invalid(self, file):
        result = file.read_many(["TestFile/hello.txt", "TestFile/does_not_exist.txt"])
        entries = result["files"]
        assert len(entries) == 2
        # Tool responses emit resource keys in canonical "root:path" form.
        valid_entry = next(e for e in entries if e["resource"] == "project:TestFile/hello.txt")
        assert "content" in valid_entry
        invalid_entry = next(e for e in entries if e["resource"] == "project:TestFile/does_not_exist.txt")
        assert "error" in invalid_entry

    def test_grep_no_matches(self, file):
        result = file.grep("NONEXISTENT_STRING_XYZ_123", resource="TestFile")
        assert result["totalMatches"] == 0
        assert result["totalFiles"] == 0

    def test_grep_regex(self, file):
        result = file.grep(r"Line \d+", use_regex=True)
        assert result["totalMatches"] >= 1

    def test_grep_invalid_regex(self, file):
        with pytest.raises(CelError):
            file.grep("[invalid regex", use_regex=True)

    def test_grep_case_sensitive(self, file):
        result = file.grep("hello", match_case=True, resource="TestFile")
        assert result["totalMatches"] == 0

    def test_grep_case_insensitive(self, file):
        result = file.grep("hello", match_case=False)
        assert result["totalMatches"] >= 1

    def test_grep_targeted_files(self, file):
        result = file.grep("Hello", files=["TestFile/hello.txt"])
        assert result["totalMatches"] >= 1

    def test_grep_whole_word(self, file):
        result = file.grep("Hello", whole_word=True)
        assert result["totalMatches"] >= 1

    def test_grep_matches_cel_sidecar_content(self, file, data):
        # file.grep includes .cel sidecar contents so agents can locate
        # metadata text. The user-facing Search panel excludes them.
        data.set_field(
            "TestFile/hello.txt",
            "summary",
            json.dumps("UNIQUE_CEL_TOKEN_xyz"),
        )
        result = file.grep("UNIQUE_CEL_TOKEN_xyz")
        sidecar_hit = next(
            (f for f in result["files"] if f["resource"].endswith(".cel")),
            None,
        )
        assert sidecar_hit is not None

    def test_read_offset_beyond_file(self, file):
        result = file.read("TestFile/hello.txt", offset=9999)
        assert result["content"] == ""

    def test_get_tree_depth_zero(self, file):
        tree = file.get_tree("", depth=0)
        assert tree["type"] == "folder"

    def test_search_no_matches(self, file):
        results = file.search("**/*.nonexistent_extension_xyz")
        assert isinstance(results, list)
        assert len(results) == 0

    def test_list_contents_glob_no_matches(self, file):
        items = file.list_contents("TestFile", glob="*.nonexistent_xyz")
        assert len(items) == 0
