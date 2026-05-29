# Troubleshoot: resource not found

The resource key parsed correctly as a path, but no file or folder lives at that key under the named root. This is distinct from an invalid resource key — the syntax was fine; the target just is not there.

## Recovering

- **Confirm the project is loaded.** Call `app_get_state` and check `isLoaded`. If no project is loaded, no resource can resolve; ask the user which project to open.
- **List the parent.** Pass the parent folder's key to `file_list_contents` (immediate children only) or `file_get_tree` (recursive). If the user is referring to a file by an inexact name, check sibling entries for typos and case differences.
- **Ask the workspace first for ambiguous references.** If the user said "this file" without a name, prefer `document_get_state` (the active document, then other open documents) and `explorer_get_state` (the explorer's selection) before searching the whole project. See `workspace_panels`.
- **Check case.** On Windows the filesystem is case-preserving but case-insensitive; resource keys round-trip whatever case the file actually has. If the registry reports the file at a different casing, use the casing it returns.
- **Check the root.** A resource key is always relative to its root's backing folder. `Scripts/foo.py` (project root) resolves under the project content folder, not the workspace folder or the agent's working directory. `temp:foo` resolves under `.celbridge/temp/`, not the project tree. Files outside any registered root cannot be addressed via resource key.

If the user explicitly intended to create the resource, switch to `file_write`, `file_write_binary`, `explorer_create_file`, or `explorer_create_folder`. The file-writing tools create the target if missing; the explorer tools fail if the resource already exists.
