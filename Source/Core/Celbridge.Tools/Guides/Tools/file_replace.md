# file_replace

Finds and replaces text within a single file, with plain text or a ripgrep regex. Use this for a change like "replace every X with Y". Use it too for a pattern that needs regex (capture groups, alternation, character classes), or a substitution scoped to a line range. For a single surgical text-match edit, prefer `file_edit`. For multiple distinct edits that should land atomically, prefer `file_multi_edit`. For multi-file find/replace, run `file_grep` first to enumerate matches, then call this tool per file.

## Parameters

### pattern / replacement

The pattern to find and the text to substitute. A line break in `replacement` takes the file's own line ending on disk, so `\n` works in any file.

### literal

**Defaults to `true`, so `pattern` is plain text.** Set `literal: false` to read `pattern` as a regex in ripgrep's dialect. The rules are in `regex_syntax`. Then `replacement` follows ripgrep's `--replace`: `$1` or `${1}` writes a group, `$name` or `${name}` a named group, and `$$` a `$`. Write `${1}_new` for group 1 followed by `_new`. `$1_new` names a group called `1_new`, and the tool refuses a reference to a missing group. With `literal: true`, `replacement` is plain text, so a `$` stays a `$`.

### matchCase

**Defaults to `true` (case-sensitive).** Set `matchCase: false` for a case-insensitive search (matching `Foo`, `foo`, and `FOO` against `pattern: "foo"`). In a regex, `(?i)` does the same.

### wholeWord

Defaults to `false`. When `true`, a match counts only when no word character touches it on either side. Word characters are letters, digits, marks and `_`. Use this to avoid substring matches when renaming short tokens: `pattern: "foo"` with `wholeWord: true` will not hit `food`, `foobar`, or `myfoo`. It works in both modes. On a regex, every match must start and end with a word character, so `wholeWord` refuses a pattern like `-foo`.

### multiline

Defaults to `false`, so a pattern matches within one line, and a line break in `pattern` is refused. When `true`, a pattern can match across lines. Write `\n` for a line break, whatever the file's line endings: the tool reads each `\r\n` as `\n`, and keeps the file's own line endings. In a regex, `.` still stops at a line break unless the pattern sets `(?s)`, and `^` and `$` still match at each line.

### fromLine / toLine

Restrict the replacement to a 1-based, inclusive line range. `0` on either side disables that bound, so the defaults `0, 0` apply the replacement to the whole file. Useful for limiting a substitution to a known function body or import block. With `multiline`, a pattern matches across the lines inside the range.

## Returns

A JSON object with:

- `replacementCount` — the total number of matches replaced.
- `affectedLines` — array of `{ from, to, matchCount, contextLines }`. `contextLines` is the post-edit content of the affected range plus one surrounding line on each side, so you can verify the substitution without a follow-up `file_read`. Ranges are 1-based inclusive line numbers in the post-edit file, sorted ascending by `from`. **Ranges are per-line, not per-match:** multiple matches on the same line collapse into one entry whose `matchCount` reports the per-line hit total. The sum of `matchCount` across all entries equals the top-level `replacementCount`. **`contextLines` is included on every returned entry, including the sample entries in a truncated response** — when the list is capped, the first/last sample is the verification signal, so keeping its context attached is the point.
- `truncated` — `true` when the response was capped because the number of merged `affectedLines` entries exceeded the verbose threshold (5). The first 3 entries and the last 1 entry are returned; `replacementCount` still reflects the real total. `false` when the full list is returned.

## Editing `.cel` sidecars

`file_replace` accepts `.cel` targets, but text-level replacements over TOML are fragile and a bad replacement puts the sidecar into `Broken` status (visible through `data_inspect`, blocks `data_*` mutations until repaired). For field and tag mutations, prefer `data_set_fields` / `data_add_tags` and their siblings — they handle quote-style autoselection and enforce the reserved-namespace rules. Reach for `file_replace` on a `.cel` only for one-off byte-level fixes the structured tools don't express.

## Gotchas

- Edits write straight to disk. If the file is open in an editor, the buffer reloads from disk and Monaco's undo history is wiped — the edit is not Ctrl-Z-revertable.
- **A plain-text `pattern` matches substrings by default.** Replacing `the` with `THE` will hit `the` inside `other`, producing `oTHEr`. The simplest fix is `wholeWord: true`, which constrains the match to whole words. Alternatively, set `literal: false` and wrap the pattern in `\b...\b`, or extend `pattern` with surrounding context (a leading or trailing space, punctuation, newline).
- A regex like `import .+\nfrom` needs `multiline: true`. Without it the tool refuses the pattern, since a pattern matches within one line.
