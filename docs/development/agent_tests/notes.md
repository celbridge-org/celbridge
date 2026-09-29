# Notes

A rich text document, which makes it the one editing surface where the platform's own clipboard must be
preserved: routing it through plain text would flatten formatting. Its toolbar also reaches the host's own
file pickers. Read the [README](README.md) for the invariants, evidence rules and levels.

## Surfaces

The note body and the toolbar's popovers.

## Cases

| Situation | Action | Expected | Level |
|---|---|---|---|
| The note body, with a selection | cut, copy, paste | the verb acts on the selection | 1 |
| A formatted run, such as bold | copy, then paste back | the formatting survives the round trip | 1 |
| The note body | select all, undo, redo | each acts on the note | 2 |
| Text selected in the note body | click the toolbar's Insert Link button with the pointer | the link popover opens and stays open, and the note is unchanged until it is answered | 2 |
| The caret in the middle of a line of the note body | click the toolbar's Insert Image button with the pointer | an empty image takes the caret's place, and its popover opens and stays open; Escape then removes the image | 2 |
| An image just inserted, its popover open with no source given | click in the note body | the popover closes and the empty image goes with it | 2 |
| A text field in a toolbar popover, such as a link | paste | text enters the field; the note body is unchanged | 2 |
| The note body | Tab | whatever the editor does with Tab, and not focus leaving the document | 3 |
| A read-only note | cut, paste | refused, and the note is unchanged | 3 |
| The image popover's picker, left open a minute before answering | choose an image | the image lands in the note | 3 |
| The link popover's picker, left open | choose a file | the link lands in the note | 3 |

Open the popovers with a real click, since a script's click opens them even where a person's does not. The
link popover needs text selected first. Clicking away from a popover, the other toolbar buttons included,
applies what it holds, and only Escape discards it, so answer one popover with Escape before opening the next.
A new image given no source is the exception: it is removed however its popover closes. Escape after Insert
Image removes only the image. The line the image split stays split, and text the image replaced stays gone.

The popover also closes when the application loses the foreground. Bring the application forward just before
sending Escape, or the popover may already have closed.

Leave the image picker open for well over half a minute before choosing: a dialog answered promptly cannot
show the failure, which is the note losing an answer it waited too long for. It is the one case that waits
the clock out in real time. For the link picker, read the deadline instead: while its dialog stands open,
`__celPendingRequests()` should show the request carrying no timeout.

## Not covered

The formatting commands themselves. What matters here is that the clipboard keeps its richness and that
verbs land on the right control.
