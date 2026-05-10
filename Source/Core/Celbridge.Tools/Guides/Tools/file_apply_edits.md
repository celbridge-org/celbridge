# file_apply_edits

Applies a batch of targeted text edits at 1-based line and column positions and writes the result to disk. The default tool when you already know the line numbers (typically after `file_grep` or `file_read`) and want surgical changes rather than a full-file rewrite.

## Edit shape

`editsJson` is a JSON array of edit objects:

```json
[
  { "line": 12, "endLine": 12, "newText": "import logging\n" },
  { "line": 30, "column": 5, "endLine": 30, "endColumn": 18, "newText": "renamed" }
]
```

- `line`, `endLine` — 1-based line numbers, both inclusive.
- `column` — 1-based column, defaults to `1` (start of line).
- `endColumn` — 1-based column, defaults to `-1` (end of line).
- `newText` — replacement text. Use `\n` to embed line breaks. The empty string deletes the range.

Whole-line replacements only need `line`, `endLine`, and `newText`. To insert a new line above line 12, use `line: 12, endLine: 12, newText: "new line\n"` (the trailing newline pushes the original line 12 down).

## Returns

A JSON object with:

- `affectedLines` — array of `{ from, to, contextLines }`. `contextLines` is the post-edit content of the affected range plus one surrounding line on each side, so you can verify the edit without a follow-up `file_read`.
- `totalLineCount` — post-edit line count.

When the edits array is empty the tool returns the literal string `"ok"` instead.

## Edge cases

- Edits are validated and applied as a batch. If any edit is out of bounds or overlaps another, the whole call fails and nothing is written.
- For deleting whole lines without a residual blank line, prefer `file_delete_lines` — `apply_edits` with empty `newText` over a full-line range still leaves the line terminator behind.
