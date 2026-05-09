# spreadsheet_delete

Deletes contiguous ranges of rows or columns from one or more sheets in a single open/save cycle. Cell ranges (e.g. `A1:C3`) are not accepted — Excel's "shift cells up/left" is intentionally not exposed.

## Original-coordinate semantics

Indices are interpreted against the **original workbook state**, so an agent can specify "rows 3:5 and 10" without mentally shifting indices after earlier deletes. The implementation applies deletes in descending order to make the original-coordinate semantics work, and overlapping ranges are deduped.

## Shift direction

- Rows below a deleted row range shift up.
- Columns to the right of a deleted column range shift left.

## Batch atomicity

Formulas are recalculated as part of the save. If any operation fails, the whole batch fails and nothing is saved.

## Mirror

`spreadsheet_delete` and `spreadsheet_insert` are inverse structural operations.
