"""End-to-end tests for the webview_* tools.

Writes a self-contained HTML page, opens it, and exercises every tool.
Eval-dependent cases are skipped automatically when the
webview-dev-tools-eval feature flag is off.
"""
import time

import pytest

from celbridge.cel_proxy import CelError

from .helpers import close_if_open, delete_if_exists


TEST_RESOURCE = "TestWebView/page.html"
UNOPENED_RESOURCE = "TestWebView/unopened.html"


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


@pytest.fixture(scope="class")
def eval_enabled(app):
    flags = app.get_state().get("featureFlags", {})
    return flags.get("webview-dev-tools-eval", False)


@pytest.fixture(autouse=True)
def workspace(explorer, file, document):
    delete_if_exists(explorer, "TestWebView")
    explorer.create_folder("TestWebView")
    file.write(TEST_RESOURCE, _WEBVIEW_TEST_HTML)
    file.write(
        UNOPENED_RESOURCE,
        "<!doctype html><html><body>unopened</body></html>",
    )
    document.open(TEST_RESOURCE, activate=True)
    # The bridge's content-ready gate covers most of the navigation wait,
    # but a small grace period lets the inline <script> run so console
    # messages are present when the first get_console call fires.
    time.sleep(0.5)
    yield
    close_if_open(document, TEST_RESOURCE)
    delete_if_exists(explorer, "TestWebView")


class TestWebView:

    # webview.reload

    def test_reload_returns_ok(self, webview):
        result = webview.reload(TEST_RESOURCE)
        assert result == "ok"
        # Reload resets the readiness gate. Wait for the next NavigationCompleted
        # so the next test in the class does not race the reload.
        time.sleep(0.5)

    def test_reload_unopened_resource_fails(self, webview):
        with pytest.raises(CelError, match="(?i)not open in the editor"):
            webview.reload("does/not/exist.html")

    def test_reload_path_traversal_rejected(self, webview):
        with pytest.raises(CelError, match="(?i)invalid resource key"):
            webview.reload("../escape.html")

    def test_reload_real_unopened_file_fails(self, webview):
        with pytest.raises(CelError, match="(?i)not open in the editor"):
            webview.reload(UNOPENED_RESOURCE)

    def test_reload_empty_resource_key_fails_with_unsupported_error(self, webview):
        # Empty string passes IsValidKey (it represents the project root) but
        # the project root has no WebView registered, so the call falls through
        # to the same unsupported error as any other unopened key.
        with pytest.raises(CelError, match="(?i)not open in the editor"):
            webview.reload("")

    # webview.eval (gated by webview-dev-tools-eval)

    def test_eval_arithmetic(self, webview, eval_enabled):
        if not eval_enabled:
            pytest.skip("webview-dev-tools-eval flag is off")
        assert webview.eval(TEST_RESOURCE, "1 + 1") == 2

    def test_eval_reads_document_title(self, webview, eval_enabled):
        if not eval_enabled:
            pytest.skip("webview-dev-tools-eval flag is off")
        title = webview.eval(TEST_RESOURCE, "document.title")
        assert title == "WebView Tools Test"

    def test_eval_unparseable_returns_none(self, webview, eval_enabled):
        # ExecuteScriptAsync returns null silently when the script throws or
        # fails to parse. The host does not surface JS errors. Lock that
        # contract in so a future change is caught.
        if not eval_enabled:
            pytest.skip("webview-dev-tools-eval flag is off")
        assert webview.eval(TEST_RESOURCE, "this is not valid javascript") is None

    def test_eval_empty_expression_rejected(self, webview, eval_enabled):
        if not eval_enabled:
            pytest.skip("webview-dev-tools-eval flag is off")
        with pytest.raises(CelError, match="must not be empty"):
            webview.eval(TEST_RESOURCE, "")

    # webview.get_html

    def test_get_html_returns_outer_html(self, webview):
        result = webview.get_html(TEST_RESOURCE)
        assert "<h1>" in result["html"]

    def test_get_html_scopes_to_selector(self, webview):
        result = webview.get_html(TEST_RESOURCE, selector="#controls")
        assert "run-btn" in result["html"]
        assert "<h1>" not in result["html"]

    def test_get_html_redacts_script_bodies(self, webview):
        result = webview.get_html(TEST_RESOURCE)
        assert "console.log" not in result["html"]
        assert "omitted" in result["html"]

    def test_get_html_redacts_style_bodies(self, webview):
        result = webview.get_html(TEST_RESOURCE)
        assert "font-family" not in result["html"]

    def test_get_html_missing_selector_fails(self, webview):
        with pytest.raises(CelError, match="no element matches"):
            webview.get_html(TEST_RESOURCE, selector="#no-such-thing")

    # webview.query

    def test_query_by_role_returns_all_matches(self, webview):
        result = webview.query(TEST_RESOURCE, role="button")
        assert result["mode"] == "role"
        assert result["totalMatches"] == 2

    def test_query_role_plus_name_filters_to_one(self, webview):
        result = webview.query(TEST_RESOURCE, role="button", name="Run")
        assert result["totalMatches"] == 1
        assert "run" in result["elements"][0]["accessibleName"].lower()

    def test_query_by_visible_text(self, webview):
        result = webview.query(TEST_RESOURCE, text="hello")
        assert result["mode"] == "text"
        assert result["totalMatches"] == 1

    def test_query_by_selector(self, webview):
        result = webview.query(TEST_RESOURCE, selector="p")
        assert result["totalMatches"] >= 3

    def test_query_role_heading_finds_h1(self, webview):
        result = webview.query(TEST_RESOURCE, role="heading")
        assert result["totalMatches"] == 1
        assert result["elements"][0]["tag"] == "h1"

    def test_query_no_mode_rejected(self, webview):
        with pytest.raises(CelError, match="exactly one"):
            webview.query(TEST_RESOURCE)

    def test_query_ambiguous_mode_rejected(self, webview):
        with pytest.raises(CelError, match="exactly one"):
            webview.query(TEST_RESOURCE, role="button", selector="button")

    def test_query_bad_selector_syntax_rejected(self, webview):
        with pytest.raises(CelError, match="invalid selector"):
            webview.query(TEST_RESOURCE, selector="<<<")

    # webview.inspect

    def test_inspect_returns_metadata(self, webview):
        result = webview.inspect(TEST_RESOURCE, "#run-btn")
        assert result["tag"] == "button"
        assert result["role"] == "button"
        assert result["accessibleName"] == "Run task"
        assert "computedStyles" in result
        assert "children" in result

    def test_inspect_returns_unique_selector(self, webview):
        result = webview.inspect(TEST_RESOURCE, "#size-select")
        assert result["selector"] == "#size-select"

    def test_inspect_missing_selector_fails(self, webview):
        with pytest.raises(CelError, match="no element matches"):
            webview.inspect(TEST_RESOURCE, "#nope")

    def test_inspect_bad_selector_syntax_rejected(self, webview):
        with pytest.raises(CelError, match="invalid selector"):
            webview.inspect(TEST_RESOURCE, "<<<")

    # webview.get_console

    def test_get_console_captures_boot_messages(self, webview):
        result = webview.get_console(TEST_RESOURCE, tail=200)
        assert any("boot:" in " ".join(e["args"]) for e in result["entries"])

    def test_get_console_suppresses_debug_by_default(self, webview):
        result = webview.get_console(TEST_RESOURCE, tail=200)
        assert not any(e["level"] == "debug" for e in result["entries"])

    def test_get_console_includes_debug_when_requested(self, webview):
        result = webview.get_console(
            TEST_RESOURCE, tail=200, include_debug=True
        )
        assert any(e["level"] == "debug" for e in result["entries"])

    def test_get_console_surfaces_caught_errors(self, webview):
        result = webview.get_console(TEST_RESOURCE, tail=200)
        assert any(
            e["level"] == "error"
            and "parse" in " ".join(e["args"]).lower()
            for e in result["entries"]
        )

    def test_get_console_since_filters_older_entries(self, webview, eval_enabled):
        if not eval_enabled:
            pytest.skip("webview-dev-tools-eval flag is off")
        baseline = webview.get_console(TEST_RESOURCE, tail=200)
        if not baseline["entries"]:
            pytest.skip("no console entries to take a checkpoint from")
        checkpoint = max(e["timestampMs"] for e in baseline["entries"])
        webview.eval(
            TEST_RESOURCE, "console.log('after-checkpoint marker')"
        )
        result = webview.get_console(
            TEST_RESOURCE, tail=200, since_timestamp_ms=checkpoint
        )
        assert all(e["timestampMs"] > checkpoint for e in result["entries"])
        assert any(
            "after-checkpoint" in " ".join(e["args"])
            for e in result["entries"]
        )

    def test_get_console_buffer_survives_reload(self, webview, eval_enabled):
        if not eval_enabled:
            pytest.skip("webview-dev-tools-eval flag is off")

        webview.eval(
            TEST_RESOURCE,
            "console.log('cel-test-pre-reload-marker')",
        )

        webview.reload(TEST_RESOURCE)
        time.sleep(0.5)  # wait for the navigation to complete

        webview.eval(
            TEST_RESOURCE,
            "console.log('cel-test-post-reload-marker')",
        )

        result = webview.get_console(TEST_RESOURCE, tail=500)
        joined_args = " ".join(
            " ".join(e["args"]) for e in result["entries"]
        )
        assert "cel-test-pre-reload-marker" in joined_args, (
            f"pre-reload marker missing after reload. entries: {result['entries']}"
        )
        assert "cel-test-post-reload-marker" in joined_args, (
            f"post-reload marker missing after reload. entries: {result['entries']}"
        )

    def test_get_network_buffer_survives_reload(self, webview, eval_enabled):
        # The shim records network entries on fetch resolution, not on call,
        # so the test sleeps briefly after each fetch.
        if not eval_enabled:
            pytest.skip("webview-dev-tools-eval flag is off")

        webview.eval(
            TEST_RESOURCE,
            "fetch('cel-test-pre-reload-fetch.json').catch(function(){})",
        )
        time.sleep(0.3)

        webview.reload(TEST_RESOURCE)
        time.sleep(0.5)

        webview.eval(
            TEST_RESOURCE,
            "fetch('cel-test-post-reload-fetch.json').catch(function(){})",
        )
        time.sleep(0.3)

        result = webview.get_network(TEST_RESOURCE, tail=200)
        urls = [entry["url"] for entry in result["entries"]]
        assert any("cel-test-pre-reload-fetch" in url for url in urls), (
            f"pre-reload fetch missing from network buffer after reload. URLs: {urls}"
        )
        assert any("cel-test-post-reload-fetch" in url for url in urls), (
            f"post-reload fetch missing from network buffer after reload. URLs: {urls}"
        )

    # webview.click

    def test_click_runs_handler_and_returns_metadata(self, webview):
        result = webview.click(TEST_RESOURCE, "#run-btn")
        assert result["selector"] == "#run-btn"
        assert result["tag"] == "button"
        # Programmatic events are always isTrusted = false.
        assert not result["isTrusted"]

        # The page's click handler flips data-clicked to "true".
        post = webview.inspect(TEST_RESOURCE, "#run-btn")
        assert post["attributes"].get("data-clicked") == "true"

    def test_click_missing_selector_fails(self, webview):
        with pytest.raises(CelError, match="(?i)no element matches"):
            webview.click(TEST_RESOURCE, "#no-such-element")

    def test_click_empty_selector_rejected(self, webview):
        with pytest.raises(CelError, match="(?i)non-empty selector"):
            webview.click(TEST_RESOURCE, "")

    def test_click_bad_selector_syntax_rejected(self, webview):
        with pytest.raises(CelError, match="(?i)invalid selector"):
            webview.click(TEST_RESOURCE, "<<<")

    # webview.fill

    def test_fill_sets_input_value(self, webview):
        # `value` attribute does not reflect property writes, so check the
        # response's read-back value rather than inspecting the attribute.
        result = webview.fill(TEST_RESOURCE, "#name-input", "Alice")
        assert result["tag"] == "input"
        assert result["value"] == "Alice"

    def test_fill_sets_textarea_value(self, webview):
        result = webview.fill(
            TEST_RESOURCE, "#notes-textarea", "line one\nline two"
        )
        assert result["tag"] == "textarea"
        assert result["value"] == "line one\nline two"

    def test_fill_sets_select_value(self, webview):
        result = webview.fill(TEST_RESOURCE, "#size-select", "l")
        assert result["tag"] == "select"
        assert result["value"] == "l"

    def test_fill_missing_selector_fails(self, webview):
        with pytest.raises(CelError, match="(?i)no element matches"):
            webview.fill(TEST_RESOURCE, "#no-such-element", "value")

    def test_fill_empty_selector_rejected(self, webview):
        with pytest.raises(CelError, match="(?i)non-empty selector"):
            webview.fill(TEST_RESOURCE, "", "value")

    # webview.get_network

    def test_get_network_captures_test_page_fetch(self, webview):
        # The inline <script> issues a fetch on load; capture records it
        # regardless of response status.
        result = webview.get_network(TEST_RESOURCE, tail=50)
        assert "entries" in result
        assert "returned" in result
        assert "totalAccumulated" in result

        urls = [entry["url"] for entry in result["entries"]]
        assert any("webview-test-network-fixture.json" in url for url in urls), (
            f"expected fetch URL not captured. URLs seen: {urls}"
        )

    def test_get_network_entry_has_required_fields(self, webview):
        result = webview.get_network(TEST_RESOURCE, tail=50)
        if not result["entries"]:
            pytest.skip("no captured entries to inspect")
        entry = result["entries"][0]
        for field in ("id", "type", "method", "url", "startTimeMs"):
            assert field in entry
        # Headers/bodies keys are emitted with null values when not opted in.
        assert entry.get("requestHeaders") is None
        assert entry.get("responseBody") is None

    def test_get_network_include_headers_opts_in(self, webview):
        opted_out = webview.get_network(TEST_RESOURCE, tail=50)
        opted_out_populated = any(
            entry.get("requestHeaders") is not None
            or entry.get("responseHeaders") is not None
            for entry in opted_out["entries"]
        )
        assert not opted_out_populated, (
            "headers should be null when include_headers=False"
        )

        opted_in = webview.get_network(
            TEST_RESOURCE, tail=50, include_headers=True
        )
        if not opted_in["entries"]:
            pytest.skip("no captured entries to inspect")
        # Early-failure rows may legitimately omit headers, so accept any.
        assert any(
            entry.get("requestHeaders") is not None
            or entry.get("responseHeaders") is not None
            for entry in opted_in["entries"]
        )

    # webview.screenshot

    def test_screenshot_default_returns_metadata_only(self, webview):
        # The proxy strips the typed image block; only metadata reaches Python.
        result = webview.screenshot(TEST_RESOURCE)
        assert result["format"] == "jpeg"
        assert result["sizeBytes"] > 0
        # `resource` is omitted from the JSON when null (WhenWritingNull).
        assert result.get("resource") is None
        assert result["imageReturned"]

    def test_screenshot_save_to_resource_writes_file(self, webview, file):
        save_resource = "TestWebView/captured.png"
        result = webview.screenshot(
            TEST_RESOURCE,
            save_to=save_resource,
            return_image=False,
            format="png",
        )
        assert result["format"] == "png"
        # Tool responses emit resource keys in canonical "root:path" form.
        assert result["resource"] == f"project:{save_resource}"
        assert not result["imageReturned"]

        info = file.get_info(save_resource)
        assert info["type"] == "file"
        assert info["size"] > 0

    def test_screenshot_no_output_combination_rejected(self, webview):
        with pytest.raises(CelError, match="(?i)discard the captured image"):
            webview.screenshot(TEST_RESOURCE, return_image=False)

    def test_screenshot_format_extension_mismatch_rejected(self, webview):
        with pytest.raises(CelError, match="(?i)does not match format"):
            webview.screenshot(
                TEST_RESOURCE,
                save_to="TestWebView/mismatch.jpg",
                return_image=False,
                format="png",
            )
