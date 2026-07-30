"""Rename coverage for the webview_* tool bridge.

A rename reuses the open document view, so the bridge registration has to move onto
the new resource key. Both WebView hosts are covered: the HTML viewer (.html) and a
contribution custom editor (.md, the markdown editor).

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


class TestWebViewRenameHtmlViewer:

    @pytest.fixture(autouse=True)
    def workspace(self, explorer, file, document):
        delete_if_exists(explorer, FOLDER)
        explorer.create_folder(FOLDER)
        file.write(HTML_RESOURCE, _PAGE_HTML)
        document.open(HTML_RESOURCE, activate=True)
        # Navigation drives the content-ready gate, but let the page settle before
        # the first tool call.
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
        # The marker is drained into the host accumulator before the rename: the
        # in-page buffer does not survive the post-rename navigation, and the
        # accumulator is what the registration carries across.
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


class TestWebViewRenameCustomEditor:

    @pytest.fixture(autouse=True)
    def workspace(self, explorer, file, document):
        delete_if_exists(explorer, FOLDER)
        explorer.create_folder(FOLDER)
        file.write(MARKDOWN_RESOURCE, "# Notes\n\nBody text.\n")
        document.open(MARKDOWN_RESOURCE, activate=True)
        # The markdown editor boots its JS client before it signals content-ready.
        time.sleep(1.0)
        yield
        close_if_open(document, MARKDOWN_RESOURCE)
        close_if_open(document, MARKDOWN_RENAMED_RESOURCE)
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
