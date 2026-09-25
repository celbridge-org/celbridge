"""Rename coverage for the webview_* tool bridge and the editors it reaches.

A rename reuses the open document view, so the bridge registration has to move onto
the new resource key, and the editor page is told the document's new name and path.
Three editors are covered: the HTML editor (.html), whose calls land on the page it
previews, the markdown editor (.md), and the code editor (.js), whose calls land on
the editor page itself.

Renames run through explorer.move because explorer.rename is interactive.
"""
import time

import pytest

from celbridge.cel_proxy import CelError

from .helpers import close_if_open, delete_if_exists

FOLDER = "TestWebViewRename"

HTML_RESOURCE = f"{FOLDER}/page.html"
HTML_RENAMED_RESOURCE = f"{FOLDER}/page_renamed.html"

MARKDOWN_RESOURCE = f"{FOLDER}/notes.md"
MARKDOWN_RENAMED_RESOURCE = f"{FOLDER}/notes_renamed.md"
MARKDOWN_MOVED_RESOURCE = f"{FOLDER}/archive/notes.md"

SCRIPT_RESOURCE = f"{FOLDER}/tool.js"
SCRIPT_RENAMED_RESOURCE = f"{FOLDER}/tool.py"


_PAGE_HTML = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>WebView Rename Test</title>
</head>
<body>
  <h1>WebView Rename Test</h1>
</body>
</html>
"""


class TestWebViewRenameHtmlEditor:

    @pytest.fixture(autouse=True)
    def workspace(self, explorer, file, document):
        delete_if_exists(explorer, FOLDER)
        explorer.create_folder(FOLDER)
        file.write(HTML_RESOURCE, _PAGE_HTML)
        # A write the file watcher reports after the document opens reaches an editor that is not
        # listening yet, and the host then holds back later reloads for five seconds. One test here
        # changes the file after a rename, so the write settles before the document opens.
        time.sleep(0.5)
        document.open(HTML_RESOURCE, activate=True)
        # Each call waits for the editor and its preview to load, but let the page
        # settle before the first tool call.
        time.sleep(0.5)
        yield
        # Both keys are closed because a failure can leave the tab under either one.
        close_if_open(document, HTML_RESOURCE)
        close_if_open(document, HTML_RENAMED_RESOURCE)
        delete_if_exists(explorer, FOLDER)

    def test_rename_moves_the_bridge_registration(self, webview, explorer):
        explorer.move(HTML_RESOURCE, HTML_RENAMED_RESOURCE)
        time.sleep(0.5)

        result = webview.get_html(HTML_RENAMED_RESOURCE)
        assert "<h1>" in result["html"]

    def test_rename_leaves_no_registration_on_the_old_resource(self, webview, explorer):
        # A registration left behind on the old key keeps resolving against the live
        # WebView, under a resource that no longer exists on disk.
        explorer.move(HTML_RESOURCE, HTML_RENAMED_RESOURCE)
        time.sleep(0.5)

        with pytest.raises(CelError, match="(?i)not open in the editor"):
            webview.get_html(HTML_RESOURCE)

    def test_rename_preserves_console_history(self, webview, explorer, eval_enabled):
        # The marker is drained into the host accumulator before the rename, since
        # the accumulator is what the registration carries across.
        if not eval_enabled:
            pytest.skip("webview-dev-tools-eval flag is off")

        webview.eval(HTML_RESOURCE, "console.log('cel-test-pre-rename-marker')")
        webview.get_console(HTML_RESOURCE, tail=500)

        explorer.move(HTML_RESOURCE, HTML_RENAMED_RESOURCE)
        time.sleep(0.5)

        result = webview.get_console(HTML_RENAMED_RESOURCE, tail=500)
        joined_args = " ".join(" ".join(e["args"]) for e in result["entries"])
        assert "cel-test-pre-rename-marker" in joined_args, (
            f"pre-rename marker missing after rename. entries: {result['entries']}"
        )

    def test_rename_moves_the_preview_to_the_new_address(self, webview, explorer, eval_enabled):
        if not eval_enabled:
            pytest.skip("webview-dev-tools-eval flag is off")

        explorer.move(HTML_RESOURCE, HTML_RENAMED_RESOURCE)
        time.sleep(0.5)

        assert webview.eval(HTML_RENAMED_RESOURCE, "location.pathname") == f"/project/{HTML_RENAMED_RESOURCE}"

    def test_rename_then_a_change_on_disk_reaches_the_preview(self, webview, explorer, file):
        # The address the file has left now answers 404, so a preview still on it would show an
        # error page the tools cannot reach, and the call would fall back to the editor page.
        explorer.move(HTML_RESOURCE, HTML_RENAMED_RESOURCE)
        time.sleep(0.5)

        changed = _PAGE_HTML.replace("<h1>WebView Rename Test</h1>", "<h1>Changed after rename</h1>")
        file.write(HTML_RENAMED_RESOURCE, changed)
        time.sleep(1.5)

        result = webview.get_html(HTML_RENAMED_RESOURCE, selector="h1")
        assert result["frame"] == "#preview-iframe"
        assert "Changed after rename" in result["html"]


class TestWebViewRenameCustomEditor:

    @pytest.fixture(autouse=True)
    def workspace(self, explorer, file, document):
        delete_if_exists(explorer, FOLDER)
        explorer.create_folder(FOLDER)
        file.write(MARKDOWN_RESOURCE, "# Notes\n\nBody text.\n\n![logo](logo.png)\n")
        document.open(MARKDOWN_RESOURCE, activate=True)
        # The markdown editor boots its JS client before it signals content-ready.
        time.sleep(1.0)
        yield
        close_if_open(document, MARKDOWN_RESOURCE)
        close_if_open(document, MARKDOWN_RENAMED_RESOURCE)
        close_if_open(document, MARKDOWN_MOVED_RESOURCE)
        delete_if_exists(explorer, FOLDER)

    def test_rename_moves_the_bridge_registration(self, webview, explorer):
        # A custom editor signals content-ready once, from its JS client, and a rename
        # re-initializes neither. The registration has to carry its already-open gate
        # across, or every later tool call waits out the gate timeout instead.
        explorer.move(MARKDOWN_RESOURCE, MARKDOWN_RENAMED_RESOURCE)
        time.sleep(0.5)

        result = webview.get_html(MARKDOWN_RENAMED_RESOURCE)
        assert result["html"]

    def test_rename_leaves_no_registration_on_the_old_resource(self, webview, explorer):
        explorer.move(MARKDOWN_RESOURCE, MARKDOWN_RENAMED_RESOURCE)
        time.sleep(0.5)

        with pytest.raises(CelError, match="(?i)not open in the editor"):
            webview.get_html(MARKDOWN_RESOURCE)

    def test_move_resolves_relative_images_against_the_new_folder(self, webview, explorer):
        explorer.create_folder(f"{FOLDER}/archive")
        explorer.move(MARKDOWN_RESOURCE, MARKDOWN_MOVED_RESOURCE)
        time.sleep(0.5)

        result = webview.query(MARKDOWN_MOVED_RESOURCE, selector="img", frame="#preview-iframe")
        assert result["totalMatches"] == 1
        assert result["elements"][0]["attributes"]["src"] == f"/project/{FOLDER}/archive/logo.png"


class TestWebViewRenameCodeEditor:

    @pytest.fixture(autouse=True)
    def workspace(self, explorer, file, document):
        delete_if_exists(explorer, FOLDER)
        explorer.create_folder(FOLDER)
        file.write(SCRIPT_RESOURCE, "console.log('hello');\n")
        document.open(SCRIPT_RESOURCE, activate=True)
        time.sleep(1.0)
        yield
        close_if_open(document, SCRIPT_RESOURCE)
        close_if_open(document, SCRIPT_RENAMED_RESOURCE)
        delete_if_exists(explorer, FOLDER)

    def test_rename_switches_the_highlighting_to_the_new_extension(self, webview, explorer, eval_enabled):
        if not eval_enabled:
            pytest.skip("webview-dev-tools-eval flag is off")

        language = "monaco.editor.getModels()[0].getLanguageId()"
        assert webview.eval(SCRIPT_RESOURCE, language) == "javascript"

        explorer.move(SCRIPT_RESOURCE, SCRIPT_RENAMED_RESOURCE)
        time.sleep(0.5)

        assert webview.eval(SCRIPT_RENAMED_RESOURCE, language) == "python"
