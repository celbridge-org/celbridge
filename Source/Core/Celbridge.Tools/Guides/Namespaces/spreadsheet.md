---
name: spreadsheet
description: Workbook tools for .xlsx files — read and write cells, manage sheets, apply formatting and views. Even reads need A1 notation and a cell-typing decision before you trust the result.
---

# spreadsheet

The `spreadsheet` namespace operates on `.xlsx` workbooks: reading and writing cells, adding and removing sheets, sorting and filtering, formatting, and capturing or restoring the active view. Spreadsheet operations are non-trivial: the cell-typing model, A1 notation, and headers-mode flag interact, and a misuse of any of them will silently produce wrong results — including on read-only operations. Read the relevant concept guides before you call.

## Must-knows

- **Read the concept guides before relying on cell values.** Spreadsheet reads can silently lie. Even a successful `spreadsheet_read_sheet` call returns subtly wrong values if you misread the headers-mode flag or misinterpret the cell-typing semantics. The `spreadsheet_a1_notation`, `spreadsheet_cell_typing`, and `spreadsheet_headers_mode` concept guides are mandatory pre-reading.
- **Ranges are A1-notation strings.** `"A1:B10"` not `(0,0,9,1)`, not `"A:B"`, not `"row 1 to row 10"`. See `spreadsheet_a1_notation`.
- **Cell typing is explicit.** Every cell carries a value and a type tag (`number`, `string`, `boolean`, `date`, `formula`, `error`). When writing, the type you supply is the type that's stored — `1` written as a string stays a string, even if it looks like a number. See `spreadsheet_cell_typing`.
- **`headers: true` shifts row indexing.** `spreadsheet_read_sheet` with `headers: true` returns rows as `{header: value}` objects; the header row is consumed and `totalRowCount` excludes it. With `headers: false` you get raw arrays. See `spreadsheet_headers_mode`.
- **Pagination is opt-in.** `spreadsheet_read_sheet` returns up to `pageSize` rows starting at `offset`; large sheets must be paged through. See `spreadsheet_paging`.
- **The editor and tool surface share a workbook model.** Edits via these tools are visible to the open spreadsheet editor and vice versa. See `spreadsheet_editor_division`.

## Tools

**Reading.**

- `spreadsheet_get_info` — workbook structure: sheet names, used range per sheet, row and column counts.
- `spreadsheet_read_sheet` — read a range as values. Honours `headers` and pagination.
- `spreadsheet_read_format` — read formatting (fonts, fills, borders, number formats) for a range.
- `spreadsheet_find` — search for a value within a range.

**Writing cells and rows.**

- `spreadsheet_write_cells` — write values (with explicit types) to specific cells.
- `spreadsheet_append_rows` — append one or more rows at the end of a sheet.
- `spreadsheet_insert` — insert blank rows or columns.
- `spreadsheet_delete` — delete rows, columns, or a range of cells.
- `spreadsheet_clear` — clear values or formatting from a range without removing structure.

**Formatting and views.**

- `spreadsheet_format_ranges` — apply font, fill, border, alignment, and number formats.
- `spreadsheet_set_conditional_formatting` — add conditional rules to a range.
- `spreadsheet_freeze_panes` — freeze rows / columns at a split point.
- `spreadsheet_set_auto_filter` — apply a sortable / filterable header bar to a range.
- `spreadsheet_sort` — sort a range by one or more keys.

**Sheet management.**

- `spreadsheet_add_sheets`, `spreadsheet_remove_sheet`, `spreadsheet_rename_sheet`, `spreadsheet_duplicate_sheet`, `spreadsheet_move_sheet` — manipulate the sheet collection.

**Active view.**

- `spreadsheet_get_active_view`, `spreadsheet_set_active_view` — read or change the editor's active sheet, selection, and scroll position.

**CSV interchange.**

- `spreadsheet_import_csv`, `spreadsheet_export_csv` — convert between a sheet and a `.csv` file.

## See also

- `spreadsheet_a1_notation` — full A1-notation rules (mandatory).
- `spreadsheet_cell_typing` — how cell types interact on read and write (mandatory).
- `spreadsheet_headers_mode` — how `headers: true` changes the response shape.
- `spreadsheet_paging` — pagination model for large sheets.
- `spreadsheet_workflows` — common multi-step recipes (find-and-write, header-aware updates).
- `spreadsheet_editor_division` — how the editor and tool surface share state.
