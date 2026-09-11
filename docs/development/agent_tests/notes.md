# Notes

A rich text document, which makes it the one editing surface where the platform's own clipboard must be
preserved: routing it through plain text would flatten formatting. Read the [README](README.md) for the
invariants, evidence rules and levels.

## Surfaces

The note body and the toolbar's popovers.

## Cases

| Situation | Action | Expected | Level |
|---|---|---|---|
| The note body, with a selection | cut, copy, paste | the verb acts on the selection | 1 |
| A formatted run, such as bold | copy, then paste back | the formatting survives the round trip | 1 |
| The note body | select all, undo, redo | each acts on the note | 2 |
| A text field in a toolbar popover, such as a link | paste | text enters the field; the note body is unchanged | 2 |
| The note body | Tab | whatever the editor does with Tab, and not focus leaving the document | 3 |
| A read-only note | cut, paste | refused, and the note is unchanged | 3 |

## Not covered

The formatting commands themselves. What matters here is that the clipboard keeps its richness and that
verbs land on the right control.
