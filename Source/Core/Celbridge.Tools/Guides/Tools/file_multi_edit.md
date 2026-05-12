# file_multi_edit

Applies an atomic batch of text-match edits to a single file. Use this when several distinct surgical edits should land together — either all succeed or none does. Same matching semantics as `file_edit`: each entry quotes the exact text to replace and the substitution, with optional `replaceAll`.

## Parameters

- `fileResource` — the resource key of the file to edit.
- `editsJson` — a JSON array of edit objects, each with the same shape as `file_edit`'s parameters:

```json
[
  { "oldString": "function add(a, b) { return a - b; }", "newString": "function add(a, b) { return a + b; }" },
  { "oldString": "// TODO: implement add", "newString": "" },
  { "oldString": "import { sub } from 'math';", "newString": "import { add, sub } from 'math';" }
]
```

Each edit takes `oldString` (required, non-empty), `newString` (required, may be empty), and `replaceAll` (optional, defaults to `false`).

## Sequential application

Edits apply sequentially against an in-memory buffer in array order. Each edit anchors against the post-previous-edit state — a later edit's `oldString` may match text that an earlier edit produced. Order matters; the array order is the application order. Useful when an edit deliberately depends on a previous edit, for example renaming a function in its declaration first, then anchoring against the new name in subsequent edits.

When edits are independent, order them earliest-in-file first or by disjoint regions. Reasoning about which `oldString` will match what is much easier when edits flow top-to-bottom or never touch the same line.

## Atomicity

The whole batch is atomic. If any edit fails its match or uniqueness check, the entire batch fails and nothing is written to disk. The final buffer is written in one operation only after every edit has succeeded.

Line endings are normalised at match time: pass `\n` or `\r\n` indifferently and the tool converts both sides of every edit to the file's existing convention. The file's line-ending style is preserved across the batch.

## Returns

A JSON object with:

- `appliedCount` — the number of edits that landed (equal to the input array length on success).
- `affectedLines` — array of `{ from, to }` ranges, in 1-based inclusive line numbers locating each replacement in the **post-batch document state** (not mid-batch positions). Sorted ascending by `from` regardless of input order. `contextLines` is intentionally omitted from this surface to keep the payload bounded; run a follow-up `file_read` on a `from`–`to` range if you want to verify a specific edit landed.

An empty `editsJson` array (`"[]"`) succeeds as a no-op: `appliedCount` is `0`, `affectedLines` is empty, and no disk write occurs.

## Failure modes

- **Edit N could not be matched** fails with `Edit N: oldString not found in file. Tried to match: '<quote>'` — `N` is the 0-based index of the failing edit and the quote shows the first 80 characters of the normalised `oldString`. Apply the same disambiguation strategies as `file_edit`: extend `oldString` with context, or set `replaceAll` on that entry if every occurrence should change.
- **Edit N has multiple matches without `replaceAll`** fails with `Edit N: oldString matched K occurrences; add surrounding context to disambiguate, or set replaceAll: true`.
- **Edit N has empty `oldString`** fails with `Edit N: oldString must be non-empty`.

Whichever edit fails, no part of the batch is written. The file on disk is unchanged.

## When to use a different tool

- One surgical edit? Use `file_edit` — same shape without the array wrapping.
- Regex or a line-range scope? Use `file_replace`.
- Whole-file rewrite? Use `file_write`.

## Gotchas

- Edits write straight to disk. If the file is open in an editor, the buffer reloads from disk after the batch lands and Monaco's undo history is wiped — the batch is not Ctrl-Z-revertable.
- A later edit anchoring against an earlier edit's output is intentional and supported. If the resulting positions overlap (a later edit overwrites part of an earlier edit's replacement), the earlier edit's location is dropped from `affectedLines` because its content no longer exists in the final buffer; the later edit's location is what remains.
