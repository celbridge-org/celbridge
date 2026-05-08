---
name: spreadsheet_export_csv
description: Inline-versus-destination output for spreadsheet_export_csv and the RFC 4180 dialect it produces.
---

# spreadsheet_export_csv

Exports a sheet (or a sub-range of one) as RFC 4180 CSV text:

- Comma delimiter.
- Double-quote quoting.
- Embedded quotes doubled.
- CRLF line endings between rows.

## Inline vs. destination

- With **no** `destination`, the CSV body is returned inline.
- With a `destination` resource key, the CSV is written to that file via the audited file-write command and a small JSON metadata object is returned instead of the body. Useful so a large export does not have to round-trip through the agent or script context.

## Empty sheet / range

When the sheet or requested range is empty:

- Inline response is an empty body.
- File destination is a zero-byte file; metadata reports `rowCount` and `columnCount` of zero.
