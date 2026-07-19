# Agent instructions

You are reading this because the auto-attach response filter prepended it to the result of your first non-proxy tool call this session. You will not see it again unless your context auto-compacts and you re-fetch it explicitly with `guides_read(["agent_instructions"])`.

## How guides and state arrive

The first non-proxy tool call this session also delivered two state snapshots ahead of these instructions:

- **App state** — the running app `version`, whether a project is loaded, the `featureFlags` map (consult before invoking a feature-gated tool such as `webview_eval`), the `focusedPanel`, whether `usesNativeMenuBar` is enabled, and the `layoutMode` visibility flags.
- **Open documents** — the active document and the full list of open editor tabs with their `resource`, `sectionIndex`, `tabOrder`, `isActive`, and `editorId`.

These are **snapshots taken at session start**. The user can switch tabs, open new files, or change focus at any time, and your snapshot will not update with them. Whenever current state matters — resolving an ambiguous file reference, deciding whether a feature flag is enabled, checking which document is active before a `webview_*` call — call `app_get_state` or `document_get_state` to refresh.

Per-tool, namespace, and concept guides ride along with your tool calls automatically:

- The first call into a namespace attaches that namespace's guide.
- The first call to a tool attaches its per-tool guide and the related concept and troubleshooter guides the tool author listed.
- Errors that map to a category helper (`InvalidResourceKey`, `FeatureFlagDisabled`, `ResourceNotFound`) attach a focused troubleshooter guide on first occurrence.

Each auto-attached block opens with a `#` heading naming what it is (`# file_grep`, `# Resource keys`, `# App state`), and the broker attaches each at most once per MCP session — there is no separate fetch step you need to make. If you resume a conversation, the new MCP session has no memory of what you saw earlier, so guides and state snapshots you have already absorbed will re-attach on first use of each tool; when you spot a heading you already have in context, skim past the body. If your context has been compacted and you need a guide body back, call `guides_read(["<name>"])` explicitly.

## The conventions you will trip on

- **Resource keys are forward-slash paths relative to the project content root**, never backslashes or absolute paths. `Scripts/hello.py` is a file; `Data` is a folder; the empty string is the project root. The `resource_keys` concept guide auto-attaches with most tools and carries the full rules.
- **Edits write straight to disk.** `file_edit`, `file_multi_edit`, `file_replace`, `file_write`, and `file_write_binary` save immediately. If the document is open, the editor reloads from disk and Monaco's undo history is wiped, so Ctrl+Z will not revert your edit. The `file_changes` and `editing_documents` guides are auto-attached on first use of those tools.
- **Resolve ambiguous file references against the user's current view first**, not by searching the whole project. Active document via `document_get_state`, then other open documents, then the explorer selection via `explorer_get_state`. Only fall back to project-wide grep when these don't resolve. The `workspace_panels` concept guide carries the details.

## Silent-failure rules to watch for

These are the rules that turn a successful tool call into wrong results:

- **Spreadsheet operations including reads require A1 notation and cell-typing context.** A `spreadsheet_read_sheet` call with the wrong `headers` flag or a misread of cell types returns subtly wrong values, not an error. The `spreadsheet` namespace guide and the `spreadsheet_a1_notation`, `spreadsheet_cell_typing`, `spreadsheet_headers_mode` concepts auto-attach on first spreadsheet use.
- **WebView tools depend on which editor opened the document.** Calling `webview_*` against a `.html` file that was opened in the code editor instead of the HTML viewer fails confusingly. Check `editorId` from `document_get_state` first.
- **Programmatic edits cannot be undone with Ctrl+Z.** They wipe Monaco's undo history when reloading the buffer. The user's recovery path is source control or a copy.

## Tool naming across surfaces

A single tool method is exposed under three names — the MCP form, the Python form, and the JavaScript form. They differ only in punctuation, but mixing them up is one of the most common parameter errors:

| Surface | Form | Example |
|---|---|---|
| MCP tool name (in `tools/list`) | `<namespace>_<snake_method>` | `file_replace` |
| Python REPL proxy (`cel.*`) | `cel.<namespace>.<snake_method>(...)` | `cel.file.replace(...)` |
| JavaScript call site (in a package) | `cel.<namespace>.<camelMethod>(...)` | `cel.file.replace(...)` |
| `[permissions] tools` manifest entry | `<namespace>.<snake_method>` | `"file.replace"` |

The dot-form alias used in manifests matches the MCP tool name after swapping the first underscore for a dot. The JavaScript proxy converts the method portion to camelCase at the call site automatically; the manifest does **not**.

## Command semantics

All tools that modify application state execute sequentially and wait for completion before returning. State is always fully applied when the tool call returns. You do not need to poll, you do not need to wait, and concurrent tool calls produce a defined order. Tools that drive user-facing dialogs (e.g. `package_publish` with `confirmWithUser: true`) wait for the user's response before returning — see `silent_vs_interactive` for which ones do.

## Python proxy conventions

The `cel` proxy is the canonical way to call Celbridge tools from Python. It is available at the REPL prompt and inside scripts run from the REPL via `%run`:

```python
cel.document.open("readme.md")
cel.app.log("Processing complete")
```

- **Parameters use snake_case.** `cel.file.multi_edit(...)`, not `multiEdit`.
- **JSON results are returned as dicts.** Tools that return structured payloads (e.g. `app.get_state`, `document.get_state`) deserialise into native Python dicts.
- **Errors raise `CelError`** with a message string. The REPL is configured to display these without a traceback so the message is the focus.
- **Methods marked `-> ok`** return the string `'ok'` on success or raise `CelError`.
- **Structured-parameter formats** like `edits_json`, `resources`, `files`, `file_resource` accept native Python lists and dicts; the proxy auto-serialises to JSON. You do not need to call `json.dumps`.

Type `help(cel)` to list the namespaces, or `help(cel.file)` to see the methods on one. To make the dependency on the proxy explicit at the top of a script, import it: `from celbridge import cel`.

## JavaScript proxy conventions

Package extensions run inside a WebView hosted by a document editor contribution (declared in `package.toml` under `[contributes].document_editors`). Before writing any JS that calls `cel.*`, declare the tools your package needs in `package.toml` under `[permissions].tools`:

```toml
[permissions]
tools = ["document.*", "file.*", "app.get_state"]
```

The manifest uses the **alias form** — `namespace.snake_case_method`. The JS proxy converts the method portion to camelCase at the call site; the manifest does **not**.

```javascript
import celbridge from '/assets/celbridge-client/celbridge.js';
await celbridge.initialize();
const tree = await cel.file.getTree("");
```

- **Arguments are positional and camelCase.** Extra arguments throw `CEL_TOOL_INVALID_ARGS`.
- **Errors throw `CelToolError`** with `{ code, tool, message }`.
- **Calling a namespace not covered by `[permissions] tools`** throws `TypeError: Cannot read properties of undefined`. Fix the manifest, not the call site.

## Domain prep — namespace guides

These auto-attach the first time you call a tool in their namespace, but you can also fetch them explicitly when planning ahead of a domain you have not entered yet:

- `app` — application state, logging, alerts, refresh.
- `document` — open / close / activate editor tabs and snapshot editor state.
- `explorer` — create / move / rename / delete files and folders, manipulate the resource tree.
- `file` — read, write, search, and edit file contents.
- `guides` — re-fetch guides after context auto-compaction.
- `package` — build, install, archive, publish Celbridge packages.
- `spreadsheet` — read and write `.xlsx` workbooks. Read this before any spreadsheet call.
- `webview` — devtools-style automation of HTML and contribution editors.

Fetch any of them with `guides_read(["<namespace>"])`.

## If your context has compacted

The host's auto-compaction can scroll guide bodies out of context. If you find yourself unsure of a rule you previously read, fetch the relevant guide(s) explicitly:

```
guides_read(["agent_instructions", "spreadsheet"])
```

`guides_read` returns the same body that auto-attached originally, including the per-tool Python and JavaScript invocation strings.
