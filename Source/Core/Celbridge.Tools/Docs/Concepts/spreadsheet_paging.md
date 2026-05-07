---
name: spreadsheet_paging
description: How offset and limit page through large sheets in spreadsheet_read_sheet, and how to detect truncation.
---

# Spreadsheet paging

`spreadsheet_read_sheet` defaults to a row limit of 1000. `offset` and `limit` page through large sheets the same way `file_read` does.

| Goal | Call |
|---|---|
| Read the first 1000 rows | omit `offset` and `limit` |
| Read rows 1000-1999 | `offset=1000` |
| Read all rows in one call | `limit=0` (use sparingly) |

The response always includes `totalRowCount` — the total number of data rows in the requested range, **ignoring** `offset` and `limit`. This is the field to check before deciding whether to page.

```python
result = spreadsheet.read_sheet(resource="data/sales.xlsx", sheet="Q1")
if result["totalRowCount"] > len(result["rows"]):
    next_page = spreadsheet.read_sheet(
        resource="data/sales.xlsx", sheet="Q1", offset=1000)
```

## Choosing a paging strategy

For agent tasks that summarise or scan a sheet, prefer `spreadsheet_get_info` first — it's a cheap call that returns sheet names, used ranges, row/column counts, frozen-pane counts, and any defined names. Page through only when `totalRowCount` indicates the data exceeds your needs.

For tasks that need to feed sheet contents into another tool (e.g. CSV export), use `spreadsheet_export_csv` with `destination` to write the bytes directly to disk and skip the inline payload entirely.
