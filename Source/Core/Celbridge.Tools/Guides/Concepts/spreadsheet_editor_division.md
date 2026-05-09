# Spreadsheet editor division of labour

The MCP `spreadsheet_*` tools and the SpreadJS editor share state through disk: every write the agent makes is visible to the editor on its next reload, and every save the editor makes is visible to the next agent read.

## What the agent tools cover

Data, formulas, sheet lifecycle, CSV interchange, common cell formatting, conditional formatting, auto-filter, and per-workbook view state.

| Concern | Tool |
|---|---|
| Read cells | `spreadsheet_get_info`, `spreadsheet_read_sheet`, `spreadsheet_find` |
| Write cells / formulas | `spreadsheet_write_cells`, `spreadsheet_append_rows` |
| Add / remove / rename / move / duplicate sheets | `spreadsheet_add_sheets`, `spreadsheet_remove_sheet`, `spreadsheet_rename_sheet`, `spreadsheet_move_sheet`, `spreadsheet_duplicate_sheet` |
| Insert / delete / clear ranges | `spreadsheet_insert`, `spreadsheet_delete`, `spreadsheet_clear` |
| Cell formatting (fonts, fill, borders, alignment, number format, column width, autofit, merges) | `spreadsheet_format_ranges`, `spreadsheet_read_format` |
| Freezing panes | `spreadsheet_freeze_panes` |
| Auto-filter | `spreadsheet_set_auto_filter` |
| Conditional formatting | `spreadsheet_set_conditional_formatting` |
| Sorting | `spreadsheet_sort` |
| CSV interchange | `spreadsheet_export_csv`, `spreadsheet_import_csv` |
| View state (active sheet, selection, scroll) | `spreadsheet_get_active_view`, `spreadsheet_set_active_view` |

## What the SpreadJS editor covers

Charts, pivot tables, data validation, named styles, themes — anything that is presentation-heavy and benefits from interactive authoring.

For advanced styling not exposed by the agent tools, open the workbook in the SpreadJS editor and let the user (or the agent driving the editor through `webview_*`) work there.

## Preservation guarantee

The agent tools preserve any presentation that already exists on cells they don't touch. A `spreadsheet_write_cells` to `B3` does not strip styles on `A1`; a `spreadsheet_format_ranges` on `A1:C1` does not touch `D1`. This means an agent can safely round-trip data through a styled workbook without losing the user's formatting.

## When the workbook is open in the editor

Most write tools trigger an external-reload path on the open workbook so the change is visible immediately, but the editor's in-memory undo history is cleared by the reload. See `undo_semantics` for the full undo model. The document tab does not close; the workbook is re-imported from disk.

`spreadsheet_set_active_view` also follows this path — call it at the end of an authoring workflow ("leave the user looking at the Summary sheet with cursor at A1") and the change is reflected in the editor immediately.
