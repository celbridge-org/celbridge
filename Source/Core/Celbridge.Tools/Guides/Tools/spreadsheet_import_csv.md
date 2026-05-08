---
name: spreadsheet_import_csv
description: CSV parsing rules and type-inference behaviour for spreadsheet_import_csv (per RFC 4180), including createIfMissing and inferTypes semantics.
---

# spreadsheet_import_csv

Replaces the contents of one or more worksheets with parsed CSV data in a single open/save cycle. Existing cells in each target sheet are cleared before its CSV block is written; other sheets in the workbook are untouched. Imports run in order. If any import fails, the whole batch fails and nothing is saved.

## CSV format

Parsed per RFC 4180:

- Comma delimiter.
- Double-quote quoting; embedded quotes doubled.
- CRLF or LF line endings.

All rows in each CSV must have the same field count as that CSV's row 1.

## Per-import options

- `createIfMissing` (bool, default false) — creates a missing sheet rather than failing the batch.
- `inferTypes` (bool, default true) — when true, plain integer / decimal / boolean fields are written as typed cell values so SUM, sorting, and conditional-formatting numeric rules work. When false, every field is kept as a string.

## Inference is conservative

When `inferTypes` is true:

- Integer-shaped fields with a leading zero (zip codes, IDs) stay as strings.
- Fields containing scientific notation (product codes like `1e10`) stay as strings.
- Dates are not inferred.

CSV imports do not produce formula cells, but formulas elsewhere in the workbook are recalculated as part of the save.
