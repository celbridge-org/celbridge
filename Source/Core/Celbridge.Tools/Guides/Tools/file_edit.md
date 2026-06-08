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

- `matchCount` — the total number of occurrences replaced.
- `affectedLines` — array of `{ from, to, matchCount, contextLines }`. `contextLines` is the post-edit content of the affected range plus one surrounding line on each side, so you can verify the edit without a follow-up `file_read`. **Use this for self-auditing:** a clean line deletion shows the surrounding lines adjacent to each other; a partial delete that left an empty line behind shows an empty string `""` between them. At the very start or end of the file the surrounding-line slot on the missing side is simply absent, so `contextLines` has fewer than 3 entries — not a bug, just no neighbour to show. Ranges are 1-based inclusive line numbers in the post-edit file, sorted ascending by `from`. **Ranges are per-line, not per-match:** multiple matches on the same line (only possible under `replaceAll`) collapse into one entry whose `matchCount` reports the per-line hit total. The sum of `matchCount` across all entries equals the top-level `matchCount`. **`contextLines` is included on every returned entry, including the sample entries in a truncated response** — when the response is capped, the first/last sample is the only verification signal you have, so keeping its context attached is the point.
- `truncated` — `true` when the response was capped because `matchCount` exceeded the verbose threshold (5). The first 3 ranges and the last 1 range are returned; `matchCount` still reflects the real total. `false` when the full list is returned.

## Failure modes

- **Empty `oldString`** fails with a hint pointing at the two common alternatives: anchor on the existing last line to append, or use `file_write` to overwrite or create the file.
- **Zero matches** fails with `oldString not found in file. Tried to match: '<quote>'` — the quote shows the first 80 characters of the normalised `oldString` with control characters escaped, so you can see what the file actually contains.
- **Multiple matches when `replaceAll` is false** fails with `oldString matched N occurrences; add surrounding context to disambiguate, or set replaceAll: true`. Pick: either extend `oldString` to include enough surrounding context that it appears once in the file, or opt in to `replaceAll` if every occurrence really should change.

## When to use a different tool

- Multiple distinct surgical edits that should land atomically? Use `file_multi_edit` — the whole batch lands or none does.
- Need regex (capture groups, character classes, alternation) or a line-range scope? Use `file_replace`.
- Whole-file rewrite or a new file? Use `file_write`.
- Editing a `.cel` sidecar? Don't — use the `data_*` tools instead (see below).

## Not for `.cel` files

`.cel` files are project metadata sidecars with a structured TOML format. A text-level edit could corrupt the TOML, so `file_edit` refuses any `.cel` target with a typed denial. Use the `data_*` tools (`data_set_field`, `data_add_tag`, etc.) to mutate sidecar contents through the structured surface.

## Gotchas

- Edits write straight to disk. If the file is open in an editor, the buffer reloads from disk and Monaco's undo history is wiped — the edit is not Ctrl-Z-revertable.
- Deleting a whole line: include the trailing newline in `oldString` (`"my_old_line\n"`). If you delete only `"my_old_line"`, the empty line remains. The response's `contextLines` exposes this inline — a clean delete shows the lines above and below adjacent to each other, while a partial delete shows an empty string `""` between them. Check `contextLines` after every line deletion to catch the residual blank without a follow-up `file_read`.
- Appending past end-of-file: anchor against a suffix of the existing file (typically its last line) and concatenate the appended text in `newString`. Quote enough of the suffix that it's unique in the file.
- **`replaceAll: true` matches substrings, not whole words.** Replacing `the` with `THE` will hit `the` inside `other`, producing `oTHEr`. Matching is case-sensitive but not word-boundary-aware. When targeting a short token that could appear inside longer words, extend `oldString` with a leading or trailing space, punctuation, or newline so the match is naturally bounded. Or use `file_replace` with `useRegex: true` and `\b` word boundaries.
