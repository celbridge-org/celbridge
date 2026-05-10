# spreadsheet_sort

Sorts the rows of a range by one or more columns. Sort keys are applied in priority order — the first key is primary, later keys break ties on equal values from the keys above. The sort is in place: cells outside the range are untouched, cells inside the range are physically reordered.

## range

A1 cell range to sort (e.g. `"A2:F100"`). Empty string sorts the worksheet's used range. Column-letter and row-number ranges are not accepted — sort needs a concrete rectangle.

## sortByJson

A JSON array of sort keys with at least one entry. Each key is an object with:

- `column` — absolute column reference, given as an A1 column letter (e.g. `"B"`) or a 1-based column number (e.g. `2`). Must lie within `range`.
- `ascending` — bool. `true` sorts smallest-first, `false` sorts largest-first.

```json
[
  {"column": "C", "ascending": false},
  {"column": "A", "ascending": true}
]
```

Columns are absolute references against the worksheet, not range-relative — so when `range` is `"B2:F100"`, the valid `column` values are `"B"` through `"F"`, not `"A"`.

## hasHeaderRow

When `true`, the first row of `range` is pinned in place and excluded from the sort. The response's `rowCount` reflects the rows that were re-ordered (so it excludes the header row in this case).

## matchCase

Defaults to `false`, matching Excel's case-insensitive default. Set to `true` for case-sensitive text comparisons.

## Atomicity

If the sort fails (e.g. a sort-key column lies outside `range`), the workbook is not saved.
