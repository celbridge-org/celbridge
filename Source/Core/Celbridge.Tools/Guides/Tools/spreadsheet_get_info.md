---
name: spreadsheet_get_info
description: Cheap workbook overview — per-sheet metadata and named ranges — typically the first call before reading or paging a workbook.
---

# spreadsheet_get_info

Returns a workbook overview: every sheet with its name, tab position, used range, dimensions, and frozen-pane counts, plus any defined named ranges. Always cheap, and almost always the right first call before any read or write — it tells the agent the sheet names, whether the workbook is empty, and how big the data is.

## Response shape

```
{
  "sheets": [
    {
      "name": "Q1",
      "position": 1,
      "usedRange": "A1:E20",
      "rowCount": 20,
      "columnCount": 5,
      "frozenRows": 1,
      "frozenColumns": 0
    }
  ],
  "namedRanges": [
    {"name": "TaxRate", "refersTo": "Q1!$B$1", "scope": "workbook"}
  ]
}
```

- `position` is the 1-based tab position.
- `usedRange` is `null` for sheets with no used range.
- `frozenRows` and `frozenColumns` are 0 on axes with no frozen panes.
- Named-range `scope` is `"workbook"` for workbook-scoped names, or the owning sheet name for sheet-scoped names.

## Detecting an inflated used range

If `usedRange` looks suspicious (e.g. `A1:XFD1`) or `columnCount` is much larger than expected, the sheet's used range has likely been inflated by a stray write to a far-right cell. `spreadsheet_read_sheet` clamps columns by default; compare its `totalColumnCount` against the value here to confirm.

## See also

- `spreadsheet_paging` for how to use `totalRowCount` and `totalColumnCount` to decide whether to page a sheet.
- `spreadsheet_workflows` for the inspect-then-read pattern.
