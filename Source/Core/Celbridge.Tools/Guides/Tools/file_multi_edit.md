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

### Worked example: post-batch positions

Suppose the input file is:

```
1: alpha
2: beta
3: gamma
```

and the batch is:

```json
[
  { "oldString": "alpha",           "newString": "ONE\nTWO\nTHREE" },
  { "oldString": "gamma",           "newString": "FOUR" }
]
```

After edit 0, the in-memory buffer has 5 lines (`ONE`, `TWO`, `THREE`, `beta`, `gamma`). Edit 1 then matches `gamma` at what is now line 5. The final on-disk file is:

```
1: ONE
2: TWO
3: THREE
4: beta
5: FOUR
```

The response reports `affectedLines` against this **final** state, not against the intermediate buffer:

- Edit 0's range: `{ editIndex: 0, from: 1, to: 3 }` (covers `ONE`, `TWO`, `THREE`).
- Edit 1's range: `{ editIndex: 1, from: 5, to: 5 }` — shifted from its original line 3 because edit 0 grew the file by two lines above it.

If you need to navigate to a specific edit's result, the reported numbers point to the line you will see on disk, not the line that existed during sequencing.

### Worked example: an overwritten edit drops out

Now change edit 1 to anchor against text edit 0 produces, then rewrite that text again:

```json
[
  { "oldString": "alpha",   "newString": "FIRST_PASS" },
  { "oldString": "FIRST_PASS", "newString": "SECOND_PASS" }
]
```

Edit 0 finds `alpha` and writes `FIRST_PASS`. Edit 1 then finds `FIRST_PASS` (the text edit 0 just produced) and writes `SECOND_PASS` on top of it. The response is:

- `edits[0]`: `{ matchCount: 1, truncated: false }` — edit 0 still matched at its turn.
- `edits[1]`: `{ matchCount: 1, truncated: false }`.
- `affectedLines`: a single entry `{ editIndex: 1, from: 1, to: 1, matchCount: 1 }`.

Edit 0's range is absent from `affectedLines` because its replacement was overwritten by edit 1 — there is no longer a position in the final file that holds edit 0's output. The "did edit 0 succeed?" question is answered by `edits[0].matchCount: 1`, not by `affectedLines`.

## Atomicity

The whole batch is atomic. If any edit fails its match or uniqueness check, the entire batch fails and nothing is written to disk. The final buffer is written in one operation only after every edit has succeeded.

Line endings are normalised at match time: pass `\n` or `\r\n` indifferently and the tool converts both sides of every edit to the file's existing convention. The file's line-ending style is preserved across the batch.

## Returns

A JSON object with:

- `appliedCount` — the number of edits in the batch (all of them, since the batch is atomic).
- `edits` — per-edit summary array indexed by input order. Each entry has `{ matchCount, truncated }`. `matchCount` is the number of matches the edit found at its turn in the sequence (before any later edit could overwrite that region). `truncated` is `true` when the edit's contribution to `affectedLines` was capped to a first + last sample because the number of merged entries exceeded 5.
- `affectedLines` — flat array of `{ editIndex, from, to, matchCount, contextLines }` ranges in 1-based inclusive line numbers, locating each match in the **post-batch document state** (not mid-batch positions). Sorted ascending by `from`. `editIndex` identifies which entry in the input batch produced each range, so you can group ranges back to their originating edit. `matchCount` is the number of matches from that edit collapsed into the range — same-line hits from one edit's `replaceAll` merge into a single entry. Entries from different edits never merge across edits, even when their ranges coincide. `contextLines` is included on every returned entry (the post-batch content of the affected lines plus one surrounding line on each side), including the sample entries when an edit's contribution to the list was capped to a first + last sample — those samples are the only verification signal for the truncated edit, so the context stays attached.

If you expected to see an edit's range in `affectedLines` but it is missing, a later edit's region overlapped and overwrote it — the earlier edit's content no longer exists in the final buffer, so its range is dropped. The authoritative per-edit count is `edits[N].matchCount`, which records what the edit matched at its turn in the sequence regardless of whether the result survived the rest of the batch.

An empty `editsJson` array (`"[]"`) succeeds as a no-op: `appliedCount` is `0`, `edits` and `affectedLines` are empty, and no disk write occurs.

## Failure modes

- **Edit N could not be matched** fails with `Edit N: oldString not found in file. Tried to match: '<quote>'` — `N` is the 0-based index of the failing edit and the quote shows the first 80 characters of the normalised `oldString`. Apply the same disambiguation strategies as `file_edit`: extend `oldString` with context, or set `replaceAll` on that entry if every occurrence should change.
- **Edit N has multiple matches without `replaceAll`** fails with `Edit N: oldString matched K occurrences; add surrounding context to disambiguate, or set replaceAll: true`.
- **Edit N has empty `oldString`** fails with a hint pointing at anchoring on the last line to append, or `file_write` to overwrite/create.

Whichever edit fails, no part of the batch is written. The file on disk is unchanged.

## When to use a different tool

- One surgical edit? Use `file_edit` — same shape without the array wrapping.
- Regex or a line-range scope? Use `file_replace`.
- Whole-file rewrite? Use `file_write`.

## Gotchas

- Edits write straight to disk. If the file is open in an editor, the buffer reloads from disk after the batch lands and Monaco's undo history is wiped — the batch is not Ctrl-Z-revertable.
- A later edit anchoring against an earlier edit's output is intentional and supported. See the Returns section for how overlapping edits affect `affectedLines` (the earlier edit's range is dropped from the list when its content no longer exists in the final buffer; `edits[N].matchCount` is unaffected).
- **`replaceAll: true` on an entry matches substrings, not whole words.** Replacing `the` with `THE` will hit `the` inside `other`. When targeting a short token that could appear inside longer words, extend `oldString` with surrounding context (a leading or trailing space, punctuation, newline). Or use `file_replace` with `useRegex: true` and `\b` word boundaries for a single-pass alternative.
