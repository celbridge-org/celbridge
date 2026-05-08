---
name: agent_instructions
description: Mandatory instructions for any agent connecting to Celbridge — read before any tool work, and read each domain's namespace guide before relying on its results.
priority: 1
---

# Agent instructions

These are instructions, not a tutorial. Every agent that connects to Celbridge must read this guide before invoking any tool other than the `guides_*` bootstrap tools. The broker's cold-start gate enforces this.

## Before any tool work

1. **Read this guide.** You are doing that now. On a fresh session, call `guides_read(["agent_instructions"])` on its own — do not parallelize it with other tool calls in the same turn, or those calls will be rejected by the cold-start gate.
2. **Call `app_get_state`.** Most workspace tools require a loaded project. The response reports the project load status, the `featureFlags` map (consult before invoking a feature-gated tool), and the `focusedPanel` plus `layoutMode` you can use to follow the user's attention. To see the project's Python dependencies, read the `.celbridge` project file (`[project].dependencies`).
3. **Read the namespace guide for each tool domain you engage with, before relying on results from that domain.** Namespace guides consolidate the must-knows for the namespace and are the single guide you read to be ready to work in that domain. Every registered MCP namespace has one. Skipping this step is the most common cause of silent-failure bugs.

## The conventions you will trip on

- **Resource keys are forward-slash paths relative to the project content root**, never backslashes or absolute paths. `Scripts/hello.py` is a file; `Data` is a folder; the empty string is the project root. See `resource_keys` for the full rules.
- **Edits write straight to disk.** `file_apply_edits`, `file_write`, `file_find_replace`, `file_delete_lines`, and `file_write_binary` save immediately. If the document is open, the editor reloads from disk and Monaco's undo history is wiped, so Ctrl+Z will not revert your edit. See `file_changes`.
- **Resolve ambiguous file references against the user's current view first**, not by searching the whole project. The active document via `document_get_state`, then other open documents, then the explorer selection via `explorer_get_state` — only fall back to project-wide grep when these don't resolve. See `workspace_panels`.

## Silent-failure rules to watch for

These are the rules that turn a successful tool call into wrong results. Read the linked namespace guide before relying on the tool's output.

- **Spreadsheet operations including reads require A1 notation and cell-typing context.** A `spreadsheet_read_sheet` call with the wrong `headers` flag or a misread of cell types returns subtly wrong values, not an error. Read the `spreadsheet` namespace guide before any spreadsheet work, then the `spreadsheet_a1_notation`, `spreadsheet_cell_typing`, and `spreadsheet_headers_mode` concept guides.
- **WebView tools depend on which editor opened the document.** Calling `webview_*` against a `.html` file that was opened in the code editor instead of the HTML viewer fails confusingly. Check `editorId` from `document_get_state` first. Read the `webview` namespace guide and `webview_devtools` before any webview work.
- **Programmatic edits cannot be undone with Ctrl+Z.** They wipe Monaco's undo history when reloading the buffer. The user's recovery path is source control or a copy. Read `file_changes` before any non-trivial edit.

## Domain prep — namespace guides

Read these before working in the corresponding domain:

- `app` — application state, logging, alerts, refresh.
- `document` — open / close / activate editor tabs and snapshot editor state.
- `explorer` — create / move / rename / delete files and folders, manipulate the resource tree.
- `file` — read, write, search, and edit file contents.
- `guides` — discover and read the built-in agent guide library.
- `package` — build, install, archive, publish Celbridge packages.
- `spreadsheet` — read and write `.xlsx` workbooks. Mandatory pre-reading before any spreadsheet call.
- `webview` — devtools-style automation of HTML and contribution editors.

Fetch any of them with `guides_read(["<namespace>"])`.

## Writing scripts

If the user asks you to write a Python script that calls Celbridge tools, read `python_proxy_conventions` for the `cel` proxy and the import line. For JavaScript inside a contribution editor, read `javascript_proxy_conventions` for the manifest declaration and the `cel` global. Per-tool guides show the call signature; the proxy conventions show how to bootstrap.

## Finding more

- `guides_list` — every guide in canonical order with one-line descriptions.
- `guides_read(["<name>"])` — read one or more guides by exact name. Tool guides carry runnable Python and JavaScript invocation strings.
- `guides_search` — regex search over names, descriptions, and bodies. Plain words work as patterns.

Tool errors that mention an unfamiliar tool will nudge you at `guides_read`. Follow the link.
