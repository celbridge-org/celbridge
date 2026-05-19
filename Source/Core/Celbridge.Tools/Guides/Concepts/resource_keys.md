# Resource keys

All file and folder references in Celbridge tools use **resource keys**: forward-slash paths under a named root. The default root is the project tree; other roots address host scratch space and diagnostic logs.

## Form

A resource key has the optional `root:path` form. When no root prefix is given, the key resolves under the implicit `project:` root.

| Key | What it refers to |
|---|---|
| `readme.md` | A file at the top of the project tree |
| `Scripts/hello.py` | A nested file in the project tree |
| `Data` | A subfolder in the project tree |
| `project:` (or `""`) | The project root itself |
| `temp:staging/pkg/v1/file.txt` | A file under the `temp:` scratch root |
| `logs:session.log` | A file under the `logs:` diagnostic root |

## Roots

- `project:` — the visible project tree. The default root; the prefix is optional in input and omitted in output. Use for all user content.
- `temp:` — host scratch space (`.celbridge/temp/`). Hidden from the resource tree. Used by host tools, scripts, and agents for transient artifacts and staging output. Contents are not version-controlled. Conventional sub-folders include `temp:staging/...`, `temp:scratch/...`, and `temp:cache/...`.
- `logs:` — host diagnostic logs (`.celbridge/logs/`). Hidden from the resource tree. Used by the host engine, Python scripts, agents, and Console panel session loggers.

## Output canonical form

When a tool reports a resource key in its result or in an error message, it uses the canonical form:

- `project:` keys are reported as bare paths (e.g. `Scripts/hello.py`), never with the explicit `project:` prefix.
- Non-`project:` keys are reported with their full root prefix (e.g. `temp:staging/pkg/file.txt`).

So `file_read` against a missing `temp:foo/bar` reports `temp:foo/bar` in the error, never bare `foo/bar`.

## Rules

- Forward slashes only. Backslashes are rejected.
- No leading slash. `/readme.md` is invalid.
- No absolute paths or drive letters. The key is always relative to its root's backing folder.
- Root prefixes are lowercase and match `[a-z][a-z0-9_]+`. Single-character roots and uppercase roots are rejected.
- An undeclared root (e.g. `unknown:foo`) is an error, not a missing-file failure.
- Case sensitivity follows the underlying filesystem; on Windows the system is case-preserving but case-insensitive.

When in doubt about which keys exist, call `file_get_tree("")` to list the top level of the project tree, or pass a folder key to list its contents.
