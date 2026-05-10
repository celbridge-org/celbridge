# file_delete_lines

Removes one or more whole lines from a file, including their line terminators. Use this rather than `file_apply_edits` with empty `newText` when you want clean line removal — `apply_edits` over a full-line range leaves a residual blank line because the trailing terminator is still present.

## Parameters

- `startLine` — 1-based, inclusive. Must be at least 1.
- `endLine` — 1-based, inclusive. Must be greater than or equal to `startLine`. Pass the same value as `startLine` to delete a single line.

## Returns

A JSON object with:

- `deletedFrom`, `deletedTo` — the deleted range as supplied.
- `totalLineCount` — post-deletion line count.
- `contextLines` — a small window of lines around the deletion point so you can verify the result without a follow-up `file_read`.
