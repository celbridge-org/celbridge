---
name: spreadsheet_format_ranges
description: Format spec keys, units (column widths in character units, row heights in points), and clear/reset sentinels for spreadsheet_format_ranges.
---

# spreadsheet_format_ranges

Each edit is an object with `sheet`, `range`, and `format` fields. `range` is an A1 cell range, column letter or range, or row number or range. Edits may target different sheets and run in order. If any edit fails, the whole batch fails and nothing is saved.

Only fields present in each edit's `format` are applied — formatting outside the listed keys (or on cells the target range does not cover) is preserved.

## Format spec keys

`textFormat`, `backgroundColor`, `borders`, `horizontalAlignment`, `verticalAlignment`, `wrapText`, `numberFormat`, `columnWidth`, `rowHeight`, `autoFitColumns`, `mergeRange`.

## Units

- `columnWidth` is in **Excel character units, not pixels**. Default is 8.43, typical column is 10-60, anything above 100 is almost certainly wrong. Use `autoFitColumns: true` to fit width to content automatically.
- `rowHeight` is in **points**. Default is 15, typical row is 12-30.

## Clearing or resetting a value

To clear a colour or reset a value back to the workbook default:

| Field | Sentinel value |
|---|---|
| Colour fields (`backgroundColor`, `foregroundColor`, border colour) | empty string |
| `fontFamily` | empty string |
| `fontSize` | non-positive number |
| `columnWidth`, `rowHeight` | negative number |
| `mergeRange` | `false` (unmerges an existing merge) |
