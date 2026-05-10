# file_find_replace

Finds and replaces text within a single file. Most appropriate when the change is "replace every X with Y" — for targeted edits at known line numbers, prefer `file_apply_edits`. For multi-file find/replace, run `file_grep` first to enumerate matches, then call this tool per file.

## Parameters

### searchText / replaceText

The pattern to find and the text to substitute. Multi-line replacements may use `\n` line endings in `replaceText`; the tool normalises to whatever line ending the file uses on disk.

### matchCase

Defaults to `false` (case-insensitive). Ignored when `useRegex` is true — use the inline `(?-i)` flag in the pattern instead.

### useRegex

When `true`, `searchText` is a .NET regex and `replaceText` may use `$1`, `${name}`, etc. for back-references.

### fromLine / toLine

Restrict the replacement to a 1-based, inclusive line range. `0` on either side disables that bound, so the defaults `0, 0` apply the replacement to the whole file. Useful for limiting a substitution to a known function body or import block.

## Returns

A JSON object with `replacementCount` — the number of matches replaced.
