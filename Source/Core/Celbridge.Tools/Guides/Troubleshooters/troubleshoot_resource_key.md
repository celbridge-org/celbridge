# Troubleshoot: invalid resource key

The tool rejected the value as a resource key. Resource keys are forward-slash paths relative to the project content root, never absolute paths or backslash-separated.

## Recovering

- **Backslashes.** Replace every `\` with `/`. Windows-style paths (e.g. `Scripts\hello.py`) do not parse; the canonical form is `Scripts/hello.py`.
- **Leading slash.** Strip any leading `/`. `/readme.md` is invalid; `readme.md` is the top-level file.
- **Drive letters and absolute paths.** Resource keys never include `C:\...` or any disk path. The key is the path inside the project tree only.
- **Stray surrounding whitespace.** Trim the input; leading and trailing spaces are not stripped.

If you intended the project root, pass an empty string `""` rather than `/` or `.`.

## Verifying the corrected key exists

After fixing the syntax, the resource may still be missing on disk. Use `file_get_tree("")` to list the top level, or pass a folder key to list its contents. The `resource_keys` concept guide carries the full rule set if you need a refresher.
