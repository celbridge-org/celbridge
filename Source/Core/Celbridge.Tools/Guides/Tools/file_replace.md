# file_replace

Finds and replaces text within a single file using a literal or regex pattern. Use this when the change is "replace every X with Y", when the pattern needs regex (capture groups, alternation, character classes), or when the substitution should be scoped to a line range. For a single surgical text-match edit, prefer `file_edit`; for multiple distinct edits that should land atomically, prefer `file_multi_edit`. For multi-file find/replace, run `file_grep` first to enumerate matches, then call this tool per file.

## Parameters

### searchText / replaceText

The pattern to find and the text to substitute. Multi-line replacements may use `\n` line endings in `replaceText`; the tool normalises to whatever line ending the file uses on disk.

### matchCase

**Defaults to `true` (case-sensitive).** Set `matchCase: false` for an explicit case-insensitive search (matching `Foo`, `foo`, and `FOO` against `searchText: "foo"`). Ignored when `useRegex` is true; use the inline `(?i)` flag in the pattern instead.

### matchWord

Defaults to `false`. When `true`, the search matches whole words only — both the start and end of the match must lie on a word boundary (the transition between `[A-Za-z0-9_]` and any other character or the string edge). Use this to avoid substring matches when renaming short tokens: `searchText: "foo"` with `matchWord: true` will not hit `food`, `foobar`, or `myfoo`. Ignored when `useRegex` is true — regex callers add their own `\b` anchors in the pattern.

### useRegex

When `true`, `searchText` is a .NET regex and `replaceText` may use `$1`, `${name}`, etc. for back-references.

### fromLine / toLine

Restrict the replacement to a 1-based, inclusive line range. `0` on either side disables that bound, so the defaults `0, 0` apply the replacement to the whole file. Useful for limiting a substitution to a known function body or import block.

**Multi-line patterns silently match nothing inside a scope.** When `fromLine` or `toLine` is set, candidate lines are tested in isolation — a regex like `import .+\nfrom` will never find `\n` because the engine never sees one inside a single line. The call still succeeds but returns `replacementCount: 0` with no error. Drop the scope (`fromLine: 0, toLine: 0`) when the pattern needs to span line breaks. See the matching note in Gotchas.

## Returns

A JSON object with:

- `replacementCount` — the total number of matches replaced.
- `affectedLines` — array of `{ from, to, matchCount, contextLines }`. `contextLines` is the post-edit content of the affected range plus one surrounding line on each side, so you can verify the substitution without a follow-up `file_read`. Ranges are 1-based inclusive line numbers in the post-edit file, sorted ascending by `from`. **Ranges are per-line, not per-match:** multiple matches on the same line collapse into one entry whose `matchCount` reports the per-line hit total. The sum of `matchCount` across all entries equals the top-level `replacementCount`. **`contextLines` is included on every returned entry, including the sample entries in a truncated response** — when the list is capped, the first/last sample is the verification signal, so keeping its context attached is the point.
- `truncated` — `true` when the response was capped because the number of merged `affectedLines` entries exceeded the verbose threshold (5). The first 3 entries and the last 1 entry are returned; `replacementCount` still reflects the real total. `false` when the full list is returned.

## Not for `.cel` files

`.cel` files are project metadata sidecars with a structured TOML format. A text-level replacement could corrupt the TOML, so `file_replace` refuses any `.cel` target with a typed denial. Use the `data_*` tools (`data_set_fields`, `data_add_tags`, etc.) to mutate sidecar contents through the structured surface.

## Gotchas

- Edits write straight to disk. If the file is open in an editor, the buffer reloads from disk and Monaco's undo history is wiped — the edit is not Ctrl-Z-revertable.
- **Literal `searchText` matches substrings by default.** Replacing `the` with `THE` will hit `the` inside `other`, producing `oTHEr`. The simplest fix is `matchWord: true`, which constrains the match to word boundaries. Alternatively, set `useRegex: true` and wrap the pattern in `\b...\b`, or extend `searchText` with surrounding context (a leading or trailing space, punctuation, newline).
- The `fromLine`/`toLine` scope applies line-by-line: a multi-line regex pattern will not match across line breaks within the scope. For example, `useRegex: true` with `searchText: "import .+\\nfrom"` and `fromLine: 1, toLine: 20` will find nothing, because each candidate line is matched in isolation — the `\n` never appears inside the line being tested. Drop the scope (`fromLine: 0, toLine: 0`) when the pattern needs to span line breaks.
