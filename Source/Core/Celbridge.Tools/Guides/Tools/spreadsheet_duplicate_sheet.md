# spreadsheet_duplicate_sheet

Duplicates an existing worksheet inside the same workbook. The copy preserves values, formulas, cell formatting, conditional formatting, freeze panes, column widths, row heights, and any other sheet-level state. Use this to scaffold a "next quarter" sheet from a template, or capture a snapshot before destructive edits.

## Position

`position` is a 1-based tab position. Use `0` to append the duplicate after the existing sheets. The valid range is `[0, sheetCount + 1]`; values outside that range fail the call.

## Naming

`newSheet` is the name to give the duplicate. Required, and must not collide with an existing sheet.

## What is copied

Everything that lives on the worksheet itself: cell values and formulas, cell formatting and conditional formatting, frozen panes, auto-filter state, column widths and row heights, merged ranges. Workbook-scoped artefacts (workbook-level named ranges, shared themes) are not duplicated because they already apply to every sheet.

## Returned position

The response includes the duplicate's final 1-based tab position. When `position` was `0`, this equals the new sheet count.
