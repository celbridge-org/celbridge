# Spreadsheet

The grid and the surrounding Designer chrome are two different kinds of input surface in one document, and
almost every defect here has been an edit meant for one landing in the other. Read the
[README](README.md) for the invariants, evidence rules and levels.

The Designer has far more controls than are worth naming. Test a representative field of each kind — a
ribbon field, a dialog field, the in-place cell editor — rather than enumerating them.

## Surfaces

The grid, the cell editor, the name box and formula bar, and the Designer's dialogs.

## Cases

| Situation | Action | Expected | Level |
|---|---|---|---|
| A selected cell or range | cut, copy, paste | the verb acts on the cells | 1 |
| A text field in a Designer dialog | paste | the text enters the field; no cell changes | 1 |
| A text selection inside the cell editor | cut, copy | only the selected text moves, and only it reaches the clipboard | 2 |
| The cell editor, mid-edit | paste | the text is inserted at the caret; the cell behind is not overwritten and the edit is not ended | 2 |
| The cell editor, mid-edit | Escape | the edit is abandoned and the cell keeps its previous value | 2 |
| The name box or formula bar | paste | text enters that field; no cell changes | 2 |
| The grid | Tab | the active cell advances; Shift+Tab moves back | 2 |
| A Designer dialog with several fields | Tab | focus moves to the next control; the grid selection does not move | 2 |
| A Designer dialog | Escape | the dialog closes and the grid selection does not move | 2 |
| The cell editor | select all | the selection covers the editor's text, not the sheet | 3 |
| A text field in a Designer dialog | cut, copy, select all | the verb acts on the field; no cell changes | 3 |
| A Designer dialog field | undo | the field's own text reverts; the workbook is not rolled back | 3 |
| A read-only or framework-locked sheet | cut, paste | refused, and the sheet is unchanged | 3 |
| A multi-cell range copied, then pasted | copy, paste | the shape of the range is preserved | 3 |

## Not covered

SpreadJS's own correctness — formulas, formatting, import and export. These cases are about which surface
an edit reaches, not what the spreadsheet does with it afterwards.

## Notes

Diagnostics inside this package are reachable in a debug build only; a release build blocks them
deliberately, so run this plan against a debug build.
