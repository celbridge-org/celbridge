# spreadsheet_remove_sheet

Removes a worksheet from a workbook. The deletion is destructive — every cell, formula, formatting, and per-sheet view state on that sheet is lost.

## Last-sheet guarantee

A workbook must contain at least one sheet, so removing the only remaining sheet fails. To wipe a workbook to a single empty sheet, use `spreadsheet_clear` with an empty range on the surviving sheet (which preserves sheet identity) rather than removing and re-adding.

## Failure modes

- Sheet not found.
- Sheet is the only sheet in the workbook.
