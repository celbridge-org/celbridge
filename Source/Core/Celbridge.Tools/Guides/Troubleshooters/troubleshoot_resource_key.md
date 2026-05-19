# Troubleshoot: invalid resource key

The tool rejected the value as a resource key. Resource keys are forward-slash paths under a named root, never absolute paths or backslash-separated. See the `resource_keys` concept guide for the full rule set.

## Recovering

- **Backslashes.** Replace every `\` with `/`. Windows-style paths (e.g. `Scripts\hello.py`) do not parse; the canonical form is `Scripts/hello.py`.
- **Leading slash.** Strip any leading `/`. `/readme.md` is invalid; `readme.md` is the top-level file under the project root.
- **Drive letters and absolute paths.** Resource keys never include `C:\...` or any disk path. The key is the path inside a registered root only.
- **Invalid root prefix.** A `root:` prefix must be lowercase and at least two characters, matching `[a-z][a-z0-9_]+`. Uppercase roots (`Project:foo`), empty roots (`:foo`), and single-character roots (`a:foo`) are rejected.
- **Stray surrounding whitespace.** Trim the input; leading and trailing spaces are not stripped.

If you intended the project root, pass an empty string `""`, the bare path, or `project:` followed by the path; the explicit prefix is optional for `project:`.

## Verifying the corrected key exists

After fixing the syntax, the resource may still be missing on disk. Use `file_get_tree("")` to list the top level of the project tree, or pass a folder key to list its contents. The `resource_keys` concept guide carries the full rule set if you need a refresher.
