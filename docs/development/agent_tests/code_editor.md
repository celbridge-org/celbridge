# Code Editor

Markdown and code documents. One editor serves both, so they are tested together; the preview and its find
bar apply only to Markdown. Read the [README](README.md) for the invariants, evidence rules and levels.

## Surfaces

The editor text, the editor's own find widget, the preview pane and the preview's find bar, across the
source, split and preview view modes. Also the view the editor returns to: where it was left, and which
editor is entitled to it. And a document renamed or moved while it is open.

## Cases

| Situation | Action | Expected | Level |
|---|---|---|---|
| Editor text, with a selection | cut, copy, paste | the verb acts on the selection; paste replaces it and leaves a caret, so pasting again inserts rather than replaces | 1 |
| The editor's find widget | paste | text enters the find field; the document is unchanged | 1 |
| Editor text | select all, then type | the whole document is replaced | 2 |
| Editor text | undo | the last edit reverts | 2 |
| Editor text, caret at the start of a line | Tab | the line indents; Shift+Tab outdents | 2 |
| A find widget with more than one field | Tab | focus moves within the widget; the document is not indented | 2 |
| Editor text, caret mid-line | the platform's end-of-line and start-of-line chords | the caret moves to each end of the line | 2 |
| Preview mode, the preview's find bar | paste | text enters the find field; the source is unchanged | 2 |
| Split mode, the editor text | paste | text enters the document | 2 |
| Split mode, the preview's find bar | paste | text enters the find field; the source is unchanged | 3 |
| A markdown preview with a link to a heading further down | click the link | the preview scrolls to the heading, and the source is unchanged | 3 |
| Preview mode, nothing focused | paste | nothing changes, and in particular the hidden source is not edited | 3 |
| A read-only document | cut, paste | refused, and the document is unchanged | 3 |
| Multiple cursors, with text on the clipboard | paste | every cursor receives the text and each is left with a caret after it | 3 |
| A markdown document scrolled well down | reload the project | it comes back showing the same place, in the same view mode | 2 |
| A markdown document scrolled well down, with an edit made in the source less than a second ago | quit the application, then launch it again | the file holds the edit, and the document comes back showing the same place, in the same view mode | 2 |
| The same document | reopen it with the code editor from the tab menu | it opens at the top of the file, and nothing of the markdown editor's view carries over | 3 |
| A markdown document showing an image from its own folder, with an edit made in the source | move it from the Explorer to a folder holding a different image of the same name | the preview shows the new folder's image, and undo in the source still reaches the edit | 3 |
| A code document with an edit made | rename it from the Explorer to another language's extension | the highlighting follows the new language, and undo still reaches the edit | 3 |

Reach the find bars by their shortcut, not only by clicking, and return focus to the editor by clicking
after using one. Focus leaving and returning without a click has been a distinct failure.

To type at the end of a line, click inside the line's text and press End. The minimap runs down the editor's
right edge, so a click past the end of a line can land on it, which scrolls the editor and leaves the keys
typed next going nowhere.

Scroll far enough down for the place to be unmistakable, since a document that restores nothing still opens
at the top. In split mode read the place back from both panes, the source's first visible line as well as the
preview's scroll, since the preview can come back right while the source does not. Give the document one short
paragraph per line, so a line in the source and its place in the preview stay close together.

For the move case, make the two images easy to tell apart, and read back the address of the image the
preview shows rather than judging it by eye. The edit made before a rename or a move is what shows the
document stayed open, since a document opened again has no undo history.

A document saves itself one second after its last change. For the quit case, keep the source changing until
the moment of the quit, for instance with a timer in the page that adds a line every few hundred milliseconds.
Then only the exit can have saved the last lines. Run the case once for each way to quit: the window's close
button, the Quit menu item or its shortcut, and on macOS Quit in the Dock.

## Not covered

Editing behaviour that belongs to the editor component itself — completion, folding, multi-cursor
gestures, how well it highlights a language — beyond the interaction between a cursor and a clipboard verb.

## Platform

The split mode row for the preview's find bar is Windows only. On macOS find in split mode opens the editor's
own find wherever the keyboard is, by design, so that find bar is reached only in preview mode there.

The editor scrolls a markdown preview to a link's heading itself. On macOS the browser would not do it. The
preview's frame sits inside the editor's own package, and Uno cancels the frame's move there. A link that
stops scrolling on macOS only therefore means the editor's scroll has been lost.

On macOS, Quit in the Dock and a logout ask the application to quit while its window is still open. Uno ends
the process at once unless the application holds the quit back. If the file is missing its last lines after
that route only, the application no longer holds the quit back.
