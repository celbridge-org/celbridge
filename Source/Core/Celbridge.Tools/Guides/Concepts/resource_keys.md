# Resource keys

All file and folder references in Celbridge tools use **resource keys**: forward-slash paths under a named root. The default root is the project folder. Other roots address host scratch space and diagnostic logs.

## Form

A resource key has the optional `root:path` form. When no root prefix is given, the key resolves under the implicit `project:` root.

| Key | What it refers to |
|---|---|
| `readme.md` | A file at the top of the project folder |
| `Scripts/hello.py` | A nested file in the project folder |
| `Data` | A subfolder in the project folder |
| `project:` (or `""`) | The project root itself |
| `temp:staging/pkg/v1/file.txt` | A file under the `temp:` scratch root |
| `logs:session.log` | A file under the `logs:` diagnostic root |
| `utils:scratchpad.scratchpad._scratchpad` | A file under the `utils:` utility-document root |

## Roots

- `project:` — the project folder, and the default root. The Explorer shows its contents. The prefix is optional in input but always present in output. Use `project:` for all user content.
- `temp:` — host scratch space. The Explorer does not show it. Used by host tools, scripts, and agents for transient artifacts and staging output. Contents are not version-controlled. **All contents are wiped when the project loads and when it closes.** Data that must persist belongs under `project:`. Conventional sub-folders include `temp:staging/...`, `temp:scratch/...`, `temp:cache/...`, and `temp:downloads/...`.
- `logs:` — host diagnostic logs. The Explorer does not show it. Used by the host engine, Python scripts, agents, and console session loggers.
- `utils:` — persistent state for utility documents. The Explorer, text search and New File do not show it. A workspace load keeps its contents, so `utils:` is the durable home for a utility's per-project state. Each utility has a fixed key there (`utils:{package}.{contribution}{resource-extension}`). See `utility_documents`.

The `temp:`, `logs:` and `utils:` roots are folders in the project's data folder, which Celbridge keeps outside the project folder. Their contents are local to the machine: nothing in them is committed, synced or copied with the project. A Python script reaches `temp:` and `logs:` through the `cel` proxy, or directly through the folders that every console's environment names in `CELBRIDGE_TEMP_FOLDER` and `CELBRIDGE_LOGS_FOLDER`, for example to hand a file there to another program. `utils:` has no variable, since its files belong to utility documents.

## Output canonical form

When a tool reports a resource key in its result or in an error message, it always carries the explicit root prefix:

- `project:` keys are reported as `project:Scripts/hello.py`, never bare `Scripts/hello.py`.
- Non-`project:` keys are reported with their full root prefix (e.g. `temp:staging/pkg/file.txt`).

This is the form a reference takes in file content, so a key copied from a tool response can be pasted straight into a reference without forgetting the prefix.

## Rules

- Forward slashes only. Backslashes are rejected.
- No leading slash. `/readme.md` is invalid.
- No absolute paths or drive letters. The key is always relative to its root's backing folder.
- Root prefixes are lowercase and match `[a-z][a-z0-9_]+`. Single-character roots and uppercase roots are rejected.
- An undeclared root (e.g. `unknown:foo`) is an error, not a missing-file failure.
- A resource key names an existing file or folder only by the name stored on disk, in every root and on every platform. Windows and macOS file systems ignore case. Celbridge still rejects a key in the wrong letter case there, and on macOS a key in another Unicode normalization too. The error message names the canonical form. A key for something that does not exist yet passes when its existing folders match their stored names exactly. Copy resource keys from tool responses, like file listings and search results, to keep each name exact.

To check which keys exist, call `file_get_tree("")` to list the top level of the project folder. Pass a folder key to list that folder's contents.

## References

A reference is the text `project:<key>` in a file, naming a resource by its key. Write it with the `project:` prefix, as a string in the file's own format:

```toml
target = "project:docs/intro.md"
```

```json
{"target": "project:docs/intro.md"}
```

Celbridge does not track references. A move, rename or delete changes only the resource and its paired `.cel` sidecar, so other files that name the old key keep naming it.

## Renaming and deleting a resource

Before you move, rename or delete a resource, find the references to it:

1. Search with `file_grep` for `project:<key>`, with `matchCase: true`, since keys are case-sensitive. For a folder, the same search finds the references to everything inside it, which begin `project:<folder>/`.
2. Review each match in context. A key quoted as an example, in documentation or a comment, is not a reference. Edit only the real references.
3. After a move or rename, update each reference to the new key. Before a delete, tell the user which files reference the resource.

A project search skips the folders that the project's `search-exclude` setting names (see `project_structure`). To search one of them, list its files with `file_get_tree` and pass their keys in `file_grep`'s `files` parameter.
