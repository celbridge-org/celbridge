# file

The `file` namespace operates on the contents of files in the project tree: reading, writing, searching, and applying targeted edits. The `explorer` namespace manages a file's existence, location, and name; `file` manages what is inside. Most edits write straight to disk and are visible immediately to the user — read `file_changes` before you edit.

## Must-knows

- **Edits write straight to disk.** `file_write`, `file_edit`, `file_multi_edit`, `file_replace`, and `file_write_binary` save immediately. If the document is open, the editor reloads from disk and Monaco's undo history is wiped. The agent's edit cannot be reverted with Ctrl+Z. See `file_changes`.
- **Resource keys are forward-slash relative paths.** No backslashes, no absolute paths. See `resource_keys`.
- **`file_grep` and `file_replace` use .NET regex syntax.** See `regex_syntax`.
- **Reading binary content takes a separate tool.** `file_read` returns text; `file_read_binary` returns base64. `file_read_image` returns an inline image content block for vision-capable models.

## Picking an edit tool

1. Need regex (capture groups, character classes, alternation) or a line-range scope? Use `file_replace`.
2. Multiple distinct surgical edits that should land atomically? Use `file_multi_edit`.
3. Single surgical edit? Use `file_edit`.
4. Whole-file rewrite or new file? Use `file_write`.

`file_edit` and `file_multi_edit` are text-match tools: quote the snippet you want to change in `oldString`, supply the replacement in `newString`, the tool finds the unique occurrence and substitutes it. They fail closed when the snippet is absent or non-unique, so a stale read surfaces as an error rather than a wrong-region edit.

## Tools

**Reading.**

- `file_read` — read text content. Optional line range and encoding.
- `file_read_many` — read many files in one call (cheaper than N calls when scanning).
- `file_read_binary` — read raw bytes as base64.
- `file_read_image` — read an image inline as a vision content block.
- `file_get_info` — file metadata (size, mtime; optional SHA-256 content hash via `computeHash: true`) without otherwise reading the file.
- `file_get_tree` — directory tree under a resource.
- `file_list_contents` — direct children of a folder.

**Searching.**

- `file_grep` — regex search across file contents.
- `file_search` — fuzzy / glob search by name.

**Writing.**

- `file_edit` — replace an exact text snippet. Default for single surgical edits.
- `file_multi_edit` — atomic batch of text-match edits.
- `file_replace` — regex or literal pattern replace, optional line-range scope.
- `file_write` — replace a file's contents or create a new file.
- `file_write_binary` — write raw bytes from base64.

**Attributes.**

- `file_set_writeable` — toggle the filesystem read-only attribute. Used to unlock files imported from source control, render managers, archives, or other locked-by-default workflows.
