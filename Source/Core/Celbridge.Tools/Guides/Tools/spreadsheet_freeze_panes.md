---
name: spreadsheet_freeze_panes
description: Freezes the first N rows and/or M columns of a worksheet so they stay visible while the rest scrolls; setting both to 0 clears the freeze.
---

# spreadsheet_freeze_panes

Freezes the first `rows` rows and the first `columns` columns of a worksheet so they stay visible while the rest of the sheet scrolls. Use this after writing a header row that should remain in view, or to anchor a leftmost label column for wide tables.

## rows and columns

Both default to `0`. Either axis may be `0` to leave that axis unfrozen. Negative values are rejected.

- `rows: 1, columns: 0` freezes the header row.
- `rows: 0, columns: 1` freezes the leftmost column.
- `rows: 1, columns: 1` freezes both — the typical "header row plus key column" layout.
- `rows: 0, columns: 0` clears any existing freeze on the sheet.

## One freeze per sheet

Each worksheet has at most one freeze configuration. Calling the tool replaces whatever was there, so freezing a different number of rows or columns does not require a separate clear step.

## Persistence

Freeze state is saved into the workbook on disk and is part of the document's view, so it survives reload by the Celbridge editor and by Excel.
