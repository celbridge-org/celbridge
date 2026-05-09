# Spreadsheet A1 notation

Every range parameter on the `spreadsheet_*` tools uses A1 notation: `"A1"`, `"B2:D10"`. The sheet name is always a separate parameter. **Do not** embed the sheet inside the range as `"Sheet1!A1"` — that form is rejected with an error.

## Range forms

| Form | Example | Meaning |
|---|---|---|
| Cell | `"B2"` | One cell |
| Cell range | `"A1:C3"` | A rectangular block |
| Column letter | `"B"` | The entire column |
| Column range | `"B:D"` | Multiple entire columns |
| Row number | `"3"` | The entire row |
| Row range | `"3:5"` | Multiple entire rows |

Different `spreadsheet_*` tools accept different subsets — for example, `spreadsheet_read_format` accepts only cell ranges, while `spreadsheet_format_ranges` accepts every form above. The tool's parameter description spells out the supported subset.

## Empty range

Many tools accept `range: ""` to mean "the worksheet's used range" (for read tools) or "the entire sheet" (for `spreadsheet_clear`). The exact meaning is documented per tool. When in doubt, call `spreadsheet_get_info` to see the used range explicitly.

## Creating a workbook

Create a new `.xlsx` workbook with `explorer_create_file` and a resource key ending in `.xlsx`. Celbridge produces a real ClosedXML-backed empty workbook with a default `Sheet1` worksheet — never write a zero-byte file with `file_write` and expect it to open. Rename the initial sheet with `spreadsheet_rename_sheet`.
