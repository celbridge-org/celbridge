# file_search

Searches for resources by name using a glob pattern matched against the full resource path.

## Pattern semantics

- **Patterns without a path separator** match at any depth: `*.py` finds all `.py` files in the project.
- **Patterns containing a slash** are anchored to their declared position: `src/*.cs` matches only files directly under `src/`.
- **`**`** matches across path separators within an anchored pattern: `**/Commands/*.cs`, `Services/**/I*.cs`.
