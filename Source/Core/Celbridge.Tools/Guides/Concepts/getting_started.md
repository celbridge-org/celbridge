---
name: getting_started
description: Orientation for any agent new to a Celbridge workspace. Covers the highest-value conventions and a decision tree for finding more detail.
priority: 1
---

# Getting Started

Celbridge is a desktop application for editing project files (text, spreadsheets, custom editors) with an MCP tool surface for agents. Every tool call operates on the project the user has loaded.

Call `app_get_state` first. Most workspace tools require a loaded project, and the response also reports the `featureFlags` map you should consult before invoking a feature-gated tool, the `focusedPanel` and `layoutMode` you can consult to follow the user's attention, and the installed Python package list.

## The conventions agents trip over

- **Resource keys are forward-slash paths relative to the project content root**, never backslashes or absolute paths. `Scripts/hello.py` is a file; `Data` is a folder; the empty string is the project root. See `resource_keys` for the full rules.
- **Edits write straight to disk.** `file_apply_edits`, `file_write`, `file_find_replace`, `file_delete_lines`, and `file_write_binary` save immediately. If the document is open, the editor reloads from disk and Monaco's undo history is wiped, so Ctrl+Z will not revert your edit. See `file_changes` for the full save model.
- **Resolve ambiguous file references against the user's current view first**, not by searching the whole project. The active document, then other open documents, then the explorer selection — only fall back to project-wide grep when these don't resolve. See `workspace_panels`.

## Decision tree

| You need to | Look at |
|---|---|
| Understand the project file tree | `project_structure`, `resource_keys` |
| Edit, search, or grep file contents | `file_changes`, `regex_syntax`, the `file_*` tools |
| Open or activate documents in the editor | `editing_documents`, the `document_*` tools |
| Inspect what the user is currently doing | `workspace_panels`, `app_get_state`, `document_get_context`, `explorer_get_context` |
| Work with `.xlsx` workbooks | `spreadsheet_a1_notation`, `spreadsheet_cell_typing`, `spreadsheet_workflows` |
| Embed an external page | `webview_documents` |
| Use the WebView devtools loop on a contribution editor | `webview_devtools` |
| Build or publish a package | `packages_overview` |
| Write Python scripts that call tools | `python_proxy_conventions` |
| Write JavaScript inside a contribution editor | `javascript_proxy_conventions` |
| Understand undo behaviour | `undo_semantics` |
| Understand which actions ask the user before running | `silent_vs_interactive` |
| Understand command names and conventions | `command_conventions`, `tool_naming` |

When you know what you want but not the exact guide name, call `guides_search` with a regex pattern. Plain words work as patterns.

## Asking for more

Every tool's full description is available via `guides_read([tool_name])`. The trimmed `tools/list` summary is enough for selection; the per-tool guide carries the rationale, examples, and gotchas. Parameter-validation errors will often nudge you at a specific guide; follow the link.
