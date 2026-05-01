# Celbridge Agent Context

## Getting Started

Call `app_get_status` before using workspace tools — most require a loaded project. The
response also includes a `featureFlags` map you can consult before calling a feature-gated
tool (e.g. check `webview-dev-tools-eval` before `webview_eval`).

## Resource Keys

All file and folder references use **resource keys**: forward-slash paths relative to
the project content root.

- `readme.md` — file at the top level
- `Scripts/hello.py` — nested file
- `Data` — subfolder
- (empty string) — the top level itself

Never use backslashes, absolute paths, or leading slashes. When in doubt, call
`file_get_tree` with an empty resource key to see the project's resource keys.

## Context Prioritization

When the user refers to a file without specifying which one, resolve ambiguity using
the current workspace context before searching the whole project:

1. **Active document** — the document the user is looking at right now (`document_get_context`, check `activeDocument`).
2. **Other open documents** — files already open in the editor tabs (`document_get_context`, check `openDocuments`).
3. **Explorer context** — the selected resource and expanded folders in the explorer panel (`explorer_get_context`).

Only fall back to a broad project search (`file_grep`, `file_get_tree`) when these
sources do not resolve the reference.

## Document Changes and Saving

Celbridge saves automatically — there is no save tool. Editing tools
(`document_apply_edits`, `document_write`, `document_find_replace`,
`document_delete_lines`, `document_write_binary`) write straight to disk, so a
follow-up `file_read` sees the result. If a document is open in the editor, its
buffer reloads from disk automatically and the editor's undo history is wiped —
Ctrl+Z cannot revert your edit. Users who care about recovering the prior
content rely on source control.

## Workspace Panels

- **Explorer** — the project file tree. Use `explorer_*` tools to create, move, and delete resources. `explorer_undo` / `explorer_redo` only affect file system operations (create, delete, move, rename, copy) — they cannot undo document text edits.
- **Documents** — the editor area. Files open as tabs across up to 3 sections (sectionIndex 0, 1, 2 from left to right). Use `document_*` tools to open, edit, and manage documents. To undo a text edit, apply a reverse edit with `document_apply_edits` or `document_delete_lines`.
- **Inspector** — shows contextual properties for the selected resource.
- **Search** — full-text search across project files. Use `file_grep` from the agent.
- **Console** — the built-in Python REPL for running and testing scripts interactively.

## Special File Formats

### `.webview` — embedded external URL

A `.webview` file is a JSON file with a single `sourceUrl` property that specifies
an external web page to display in an embedded browser panel.

```json
{ "sourceUrl": "https://example.com" }
```

The `sourceUrl` must be an external `http://` or `https://` URL. Local paths
and resource keys are not supported. Use `document_write` to create a
`.webview` file in one step.

## Packages

Packages extend Celbridge with custom document editors and other contributions.
Each package lives in its own kebab-case subfolder within the `packages/` folder
at the project root (e.g. `packages/my-widget`). Packages run inside a WebView2
control and communicate with the host via JSON-RPC. They can contain any type of
content. Web content (HTML, JavaScript, CSS) is typical.

### Creating a Package

Use `package.create("my-widget")` to scaffold a new package. This creates
`packages/my-widget/` with a stub `package.toml` manifest.

### Package Manifest (package.toml)

Every package folder must contain a `package.toml` file at its root with at
minimum a `[package]` section containing `id` and `name`:

```toml
[package]
id = "my-widget"
name = "My Widget"
version = "1.0.0"

[contributes]
document_editors = ["my-editor.document.toml"]
```

Required fields: `id`, `name`. Optional fields: `version`, `feature_flag`.
The `[contributes]` section lists document editor manifests provided by the package.

### Publishing and Installing

Packages are published to and installed from a remote package registry:

- `package.create("name")` — create a new package with a stub manifest
- `package.publish("packages/name", "name")` — validate and upload to the registry
- `package.install("name")` — download and extract from the registry
- `package.list()` — list all packages available in the registry

To publish, the package must be in the `packages/` folder, the folder name must
match the package name, and the manifest must be valid.

**Important:** `package.publish` and `package.install` are destructive actions.
Both tools accept a `confirmWithUser` parameter (default `true`) that displays a
confirmation dialog in the application before proceeding. Always pass `true`
(or omit the parameter) unless the user has explicitly asked for unattended
operation (e.g. in an install script).

## WebView DevTools

The `webview_*` tools give you a feedback loop into a running contribution editor
or project HTML viewer WebView. Use them to iterate on package code without
needing the user to reload and paste back errors.

**Edit-reload-inspect workflow:**

1. Edit the package's HTML/CSS/JS files with `document_*` or `file_*` tools.
2. Call `webview_reload(resource)` to reload the WebView for the open document.
   The package code reinitialises from scratch and Monaco's undo history (if
   any) is wiped — the reload is destructive by design. By default the WebView
   HTTP cache is cleared first so newly-edited JS/CSS/image sub-resources are
   refetched. Pass `clearCache: false` to skip the cache clear when you know
   no sub-resources have changed and want a slightly faster reload.
3. Call `webview_get_console(resource)` immediately after a reload to read any
   parse errors, runtime exceptions, or warnings the editor logged. The console
   buffer is preserved across reloads so errors logged before the reload remain
   readable after.
4. Inspect the rendered DOM with `webview_get_html(resource, selector?)`,
   `webview_query(resource, ...)`, and `webview_inspect(resource, selector)`
   to confirm the markup, find specific elements, and read computed styles.
5. Exercise the editor with `webview_click(resource, selector)`,
   `webview_fill(resource, selector, value)`, or `webview_eval(resource, expression)`.
6. Capture a visual snapshot with `webview_screenshot(resource, ...)` when the
   rendered output matters. Observe network activity with
   `webview_get_network(resource)`.

**Confirm the right editor opened the document.** `document_get_context`
returns an `editorId` for each open document (e.g. `celbridge.html-viewer`
or `celbridge.code-editor`). If you opened a `.html` file expecting the
HTML viewer but `editorId` is the code editor, the WebView devtools will
not work against it — check the resource's file association before
calling any `webview_*` tool.

**Programmatic clicks have `isTrusted: false`.** `webview_click` dispatches a
real `MouseEvent` sequence (`mousedown`, `mouseup`, `click`) that bubbles, but
because the events are synthetic, handlers that gate on `event.isTrusted` will
not fire. If a click appears to do nothing, use `webview_eval` to verify the
listener is registered (e.g. `getEventListeners(document.querySelector('#btn'))`
in DevTools-style probes) before assuming the click failed.

**`webview_fill` works for most framework input bindings.** It assigns the
value through the native `HTMLInputElement` / `HTMLTextAreaElement` /
`HTMLSelectElement` setter and then dispatches bubbling `input` and `change`
events, so React's synthetic event system, Lit, Vue, and Svelte all observe
the change. Only `<input>`, `<textarea>`, `<select>`, and `contenteditable`
elements are accepted. Any other selector causes the call to fail fast.

**`webview_get_network` defaults to a header- and body-free summary.** Each
entry includes URL, method, status, timing, and sizes. Set `includeHeaders`
or `includeBodies` to widen the payload — bodies dominate context, so opt in
only when you need them. Response bodies are captured up to ~16KB with
truncation markers. Binary responses appear as a placeholder. The buffer
survives reloads, like the console buffer.

**`webview_screenshot` requires the document to be the active tab.**
WebView2 pauses rendering for inactive tabs, so an inactive tab cannot
produce a frame and the tool fails fast rather than hanging. Before
calling, ensure the document is open (`document_open`) and that it is the
active document (check `document_get_context` → `activeDocument`). If the
user switches tabs during the capture, the tool times out within ~5
seconds with an explanatory error — re-activate the tab and retry.

**`webview_screenshot` returns the image inline by default and does not
write to disk.** The captured image arrives as an MCP image content block
that the multimodal model sees directly, alongside a JSON metadata text
block. No project file is created unless you explicitly ask for one. Use
`saveTo` to additionally archive the image into the project tree:

- `saveTo: ""` (default) — ephemeral capture, nothing written to disk.
- `saveTo: "screenshots/"` — write into that folder with an auto-generated
  filename (trailing slash or no extension is treated as a folder
  reference).
- `saveTo: "docs/output.png"` — write to that exact resource key. The
  file extension must match the format (`.jpg`/`.jpeg` for `jpeg`,
  `.png` for `png`).

When you do save into the project, the destination is ordinary project
space — list it with `file_get_tree`, open files with `document_open`,
and delete with `explorer_delete` when they are no longer needed.
Nothing is evicted automatically.

For save-only flows where the image bytes are not needed in the model's
context (e.g. publishing a snapshot for the user to view), pass
`returnImage: false` along with `saveTo` to skip the inline-token cost.
`returnImage: false` with no `saveTo` is a hard error (the capture would
have no output route).

**After a layout-changing operation, pass `settleMs`.** The screenshot
tool waits for the editor's content-ready signal before capturing, but
that signal is package-defined and can fire before panel animations
finish, fonts load, or async resources arrive — leaving the agent with
a half-rendered frame and no obvious tell. After `document_open`, after
a click that triggers route navigation, or any time you suspect the
editor's package may still be composing its initial layout, pass
`settleMs: 500` (or higher — 1000 is fine for slow editors). A small
fixed paint backstop is always applied, so omitting `settleMs` still
gives the page one paint cycle of headroom for a static-on-arrival
target.

Defaults are JPEG quality 70 with the longer edge capped at 768 pixels —
roughly 590 image tokens per capture for a 4:3 viewport. That's the
right tradeoff for typical UI inspection. Pass `maxEdge: 1024` (or
higher) when you need to read fine on-screen text such as code in an
editor. Pass `maxEdge: 0` to disable downscaling entirely (useful with
`selector` when capturing a small element at native resolution). Image
token cost scales with pixel area: Claude tokenizes images at roughly
`width × height / 750`, so doubling `maxEdge` quadruples the token
cost. Pass `format: "png"` for lossless output. Pass `selector` to
clip to a specific element. The tool returns "not supported" on
platforms without a native snapshot API. The metadata payload is
`{format, width, height, sizeBytes, resource, imageReturned}` —
`resource` is the saved file's resource key when `saveTo` was
provided, otherwise null.

**Reading saved images: `file_read_image`.** To inspect an image that
already exists in the project tree (a previously-saved screenshot, a
user-supplied PNG, etc.), call `file_read_image(resource)`. It returns
the pixels as an MCP image content block in the same way
`webview_screenshot` does. Supported formats: JPEG, PNG, GIF, WebP. For
non-image binaries, use `file_read_binary` instead — `file_read_image`
intentionally rejects other types because the inline image transport
only makes sense for visual content.

**Readiness contract.** Every inspection and eval tool waits up to 5 seconds
for the editor's content-ready signal before dispatching. For contribution
editors that means `celbridge.notifyContentLoaded()`. For the HTML viewer it
means the WebView's NavigationCompleted. If a tool times out with a
`content-ready` message, the editor never signalled readiness — check the
console for an unhandled exception during init.

**`webview_query` modes.** Pass exactly one of `role` (with optional `name`),
`text`, or `selector`. Role queries combine the explicit `role` attribute and
the implicit role for the element's tag (button → button, h2 → heading,
nav → navigation). Returned `selector` strings are stable enough to pass
straight to `webview_inspect`.

**Eligible targets.** Works on any open document editor — text, markdown,
HTML viewers, and custom contribution editors. Excluded: external-URL
`.webview` documents and editors whose package opts out of devtools. If you
are unsure whether a given document qualifies, just call the tool — the error
message will tell you. The resource key must match an open document tab.

**`webview_eval` is gated by an extra feature flag.** It is an arbitrary code
execution primitive, so it requires both `webview-dev-tools` and
`webview-dev-tools-eval`. If it is unavailable, the other webview_* tools may
still be usable. Tell the user the flag is off and why it is gated separately.

**These tools are available from the Python `cel.*` proxy and the MCP path,
but not from the JavaScript `cel.*` proxy inside contribution editor
packages.** The Python proxy runs on behalf of the user (the trust root) so
`cel.webview.eval(...)` works there. The JavaScript proxy runs inside
third-party package code, so the host denies any `webview.*` call arriving
from a contribution editor — regardless of what the package's
`requires_tools` declares. This blocks the cross-document attack vector
where one editor's JavaScript could eval against another open document. Do
not declare `webview.*` in a package's `requires_tools`.

## Regular Expressions

Tools that accept regex patterns (e.g. `file_grep` with `useRegex: true`) use
**.NET `System.Text.RegularExpressions` syntax**. Key differences from other flavours:
named groups use `(?<name>...)`, variable-length lookbehinds are supported, `\w`/`\d`
are Unicode-aware by default, and `\K` (PCRE keep) is not available.

## Commands

All tools that modify application state execute sequentially and wait for completion
before returning. State is always fully applied when a tool call returns — the agent
never needs to poll or wait for an operation to finish.

## Python Scripting

Import modules from the `celbridge` package. Module names match tool namespaces.
Call `query_get_python_api` for the full API reference with method signatures,
parameter types, and installed Python packages.

```python
from celbridge import app, document

document.open("readme.md")
app.log("Processing complete")
```

## Writing Package Extensions (JavaScript)

Package extensions run inside a WebView hosted by a document editor
contribution (declared in `package.toml` under `[contributes].document_editors`).
There is currently no non-editor WebView surface, so JS tool calls happen
from within an editor.

**Before writing any JS that calls `cel.*`, declare the tools your package
needs in `package.toml` under `[mod].requires_tools`.** The `cel.*` proxy
is built from this allowlist at `initialize()` time, so namespaces you
did not declare simply do not exist on the proxy. Glob patterns are
supported (`document.*`, `file.*`).

```toml
[mod]
requires_tools = ["document.*", "file.*", "app.get_status"]
```

**Use the alias form — `namespace.snake_case_method` — in the manifest.** This
is the same form listed in the Python API reference, and it matches the MCP
tool name after swapping the first underscore for a dot. The JS proxy converts
to camelCase at the call site, but the manifest does NOT. Examples:

- `"file.list_contents"` → call site `cel.file.listContents(...)`
- `"file.read_binary"` → call site `cel.file.readBinary(...)`
- `"document.apply_edits"` → call site `cel.document.applyEdits(...)`

Do not use camelCase (`"file.listContents"`) or the MCP underscore form
(`"file_list_contents"`) — neither matches.

Then import the client from the `shared.celbridge` virtual host and call
host tools through the `cel` global (populated after `initialize()`
resolves):

```javascript
import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
await celbridge.initialize();
const tree = await cel.file.getTree("");
```

`initialize()` performs the `document/initialize` RPC handshake with the
host and loads the tool descriptors.

Failure modes:
- Accessing `cel.*` before `initialize()` resolves throws `CelToolError`.
- Calling a namespace that is not covered by `requires_tools` throws
  `TypeError: Cannot read properties of undefined` — the namespace was
  never added to the proxy. Fix the manifest, not the call site.
- Calling a tool whose name is not covered by `requires_tools` but whose
  namespace partially matches another entry throws `CEL_TOOL_DENIED` at
  call time (the host re-checks the allowlist on every call).

The celbridge-client source is served from the Celbridge install directory
and is not readable via `file.read`. Call `query_get_javascript_api` for
the full signature reference.
