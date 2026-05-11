# file_edit

Replaces an exact text snippet inside a single file. The default surgical-edit tool: quote the text you want to change, supply the replacement, the tool finds the unique occurrence and substitutes it. Fails closed when the snippet is absent or non-unique, so a stale read or wrong mental model surfaces immediately rather than silently editing the wrong region.

## Parameters

- `fileResource` — the resource key of the file to edit.
- `oldString` — the exact text to match. Must be present in the file. Must be unique unless `replaceAll` is set.
- `newString` — the replacement text. May be empty (deletes the matched text). To delete a whole-line block cleanly, include the trailing newline in `oldString` so the line terminator is removed with it — otherwise an empty line remains behind.
- `replaceAll` — defaults to `false`. When `true`, every occurrence of `oldString` is replaced.

Line endings are normalised at match time: pass `\n` or `\r\n` indifferently and the tool converts both sides to whatever the file uses on disk. The file's existing line-ending convention (CRLF on Windows-edited files, LF on Unix-edited files) is preserved across the edit. Matching is byte-equal after normalisation — no whitespace tolerance, no fuzzy matching, no regex.

## Returns

A JSON object with:

- `matchCount` — the number of occurrences replaced.
- `affectedLines` — array of `{ from, to, contextLines }`. `contextLines` is the post-edit content of the affected range plus one surrounding line on each side, so you can verify the edit without a follow-up `file_read`. Ranges are 1-based inclusive line numbers in the post-edit file, sorted ascending by `from`.

## Failure modes

- **Empty `oldString`** fails with `oldString must be non-empty; use file_write to overwrite a file or to create a new one`.
- **Zero matches** fails with `oldString not found in file. Tried to match: '<quote>'` — the quote shows the first 80 characters of the normalised `oldString` with control characters escaped, so you can see what the file actually contains.
- **Multiple matches when `replaceAll` is false** fails with `oldString matched N occurrences; add surrounding context to disambiguate, or set replaceAll: true`. Pick: either extend `oldString` to include enough surrounding context that it appears once in the file, or opt in to `replaceAll` if every occurrence really should change.

## When to use a different tool

- Multiple distinct surgical edits that should land atomically? Use `file_multi_edit` — the whole batch lands or none does.
- Need regex (capture groups, character classes, alternation) or a line-range scope? Use `file_find_replace`.
- Whole-file rewrite or a new file? Use `file_write`.

## Gotchas

- Edits write straight to disk. If the file is open in an editor, the buffer reloads from disk and Monaco's undo history is wiped — the edit is not Ctrl-Z-revertable.
- Deleting a whole line: include the trailing newline in `oldString` (`"my_old_line\n"`). If you delete only `"my_old_line"`, the empty line remains.
- Appending past end-of-file: anchor against a suffix of the existing file (typically its last line) and concatenate the appended text in `newString`. Quote enough of the suffix that it's unique in the file.
