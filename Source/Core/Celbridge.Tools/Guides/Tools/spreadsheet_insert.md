# spreadsheet_insert

Inserts empty rows or columns into one or more sheets in a single open/save cycle. Cell ranges (e.g. `A1:C3`) are not accepted — Excel's "shift cells down/right" is intentionally not exposed.

## Range width determines insertion count

The width of the range determines how many empty rows or columns are inserted:

- `"3:5"` inserts 3 rows starting at row 3.
- `"B:D"` inserts 3 columns starting at column B.

## Original-coordinate semantics

Indices are interpreted against the **original workbook state**, so you can specify "insert rows at 3 and at 10" without mentally shifting indices after earlier inserts. The implementation applies inserts in descending order to make the original-coordinate semantics work, and overlapping ranges are deduped.

## Shift direction

- Existing rows at or below the insert position shift down.
- Existing columns at or to the right of the insert position shift right.

## Batch atomicity

Formulas are recalculated as part of the save. If any operation fails, the whole batch fails and nothing is saved.
