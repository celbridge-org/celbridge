# Resource keys

All file and folder references in Celbridge tools use **resource keys**: forward-slash paths relative to the project content root.

| Key | What it refers to |
|---|---|
| `readme.md` | A file at the top level |
| `Scripts/hello.py` | A nested file |
| `Data` | A subfolder |
| `` (empty string) | The top level itself |

## Rules

- Forward slashes only. Backslashes are rejected.
- No leading slash. `/readme.md` is invalid.
- No absolute paths. The key is always relative to the project content root.
- Case sensitivity follows the underlying filesystem; on Windows the system is case-preserving but case-insensitive.

When in doubt about which keys exist in the current project, call `file_get_tree("")` to list the top level, or pass a folder key to list its contents.
