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
