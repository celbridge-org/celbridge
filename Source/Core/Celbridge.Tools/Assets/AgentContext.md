# Celbridge Agent Context

## Getting Started

Call `app_get_status` before using workspace tools — most require a loaded project.

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
`document_delete_lines`, `document_write_binary`) flush to disk before returning,
so a follow-up `file_read` sees the result.

Each takes `open_document` (default `true`). When true, the edit routes through
Monaco and joins the document's undo stack (one undo reverses it). When false, it
goes straight to disk: no tab, no undo entry, faster.

Prefer `true` for small targeted edits on existing files (<=3 files, user likely
reviewing). Prefer `false` for new file creation (no prior content to undo), bulk
operations, and edits the user did not ask to review. Within an opened scope,
favour several `document_apply_edits` calls over one `document_write` so each
change is a separate undo step.

## Workspace Panels

- **Explorer** — the project file tree. Use `explorer_*` tools to create, move, and delete resources. `explorer_undo` / `explorer_redo` only affect file system operations (create, delete, move, rename, copy) — they cannot undo document text edits.
- **Documents** — the editor area. Files open as tabs across up to 3 sections (sectionIndex 0, 1, 2 from left to right). Use `document_*` tools to open, edit, and manage documents. To undo a text edit, apply a reverse edit with `document_apply_edits` or `document_delete_lines`.
- **Inspector** — shows contextual properties for the selected resource.
- **Search** — full-text search across project files. Use `file_grep` from the agent.
- **Console** — the built-in Python REPL for running and testing scripts interactively.

## Special File Formats

### `.webapp` — embedded web view

A `.webapp` file is a JSON file with a single `sourceUrl` property that specifies
the web page or local HTML file to display in an embedded browser panel.

```json
{ "sourceUrl": "https://example.com" }
```

The `sourceUrl` value can be:
- A full URL: `https://example.com`
- A relative path to a local HTML file: `my_app.html` (resolved relative to the `.webapp` file)

Use `document_write` to create a `.webapp` file in one step.

## Packages

Packages extend Celbridge with custom document editors and other contributions.
Each package lives in its own kebab-case subfolder within the `packages/` folder
at the project root (e.g. `packages/my-widget`). Packages run inside a WebView2
control and communicate with the host via JSON-RPC. They can contain any type of
content; web content (HTML, JavaScript, CSS) is typical.

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
