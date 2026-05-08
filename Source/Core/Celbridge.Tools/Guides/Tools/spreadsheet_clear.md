---
name: spreadsheet_clear
description: Clear-versus-delete semantics, range forms, and sheet-identity preservation for spreadsheet_clear.
---

# spreadsheet_clear

Clears cell content, formatting, comments, merged ranges, and data validation across a batch of ranges in one or more sheets in a single open/save cycle.

## Range forms

Each operation specifies a sheet and a range. `range` may be:

- A cell range (`"A1:C3"`).
- A single cell (`"B2"`).
- A column or column range (`"E"`, `"B:D"`).
- A row or row range (`"3"`, `"3:5"`).
- An empty string to clear the entire sheet.

## Clear vs. delete

Unlike `spreadsheet_delete`, clear does **not** shift remaining cells — the cleared range is emptied in place.

## Sheet identity is preserved

When the entire sheet is cleared, the sheet's identity is preserved: tab name, position, color, frozen panes, named ranges, column widths, and row heights all carry over.

## Atomicity

If any operation fails, the whole batch fails and nothing is saved.
