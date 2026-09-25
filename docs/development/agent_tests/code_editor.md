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
| Editor text | Tab | the line indents; Shift+Tab outdents | 2 |
| A find widget with more than one field | Tab | focus moves within the widget; the document is not indented | 2 |
| Editor text, caret mid-line | the platform's end-of-line and start-of-line chords | the caret moves to each end of the line | 2 |
| Preview mode, the preview's find bar | paste | text enters the find field; the source is unchanged | 2 |
| Split mode, the editor text | paste | text enters the document | 2 |
| Split mode, the preview's find bar | paste | text enters the find field; the source is unchanged | 3 |
| Preview mode, nothing focused | paste | nothing changes, and in particular the hidden source is not edited | 3 |
| A read-only document | cut, paste | refused, and the document is unchanged | 3 |
| Multiple cursors, with text on the clipboard | paste | every cursor receives the text and each is left with a caret after it | 3 |
| A markdown document scrolled well down | reload the project | it comes back showing the same place, in the same view mode | 2 |
| The same document | reopen it with the code editor from the tab menu | it opens at the top of the file, and nothing of the markdown editor's view carries over | 3 |
| A markdown document showing an image from its own folder, with an edit made in the source | move it from the Explorer to a folder holding a different image of the same name | the preview shows the new folder's image, and undo in the source still reaches the edit | 3 |
| A code document with an edit made | rename it from the Explorer to another language's extension | the highlighting follows the new language, and undo still reaches the edit | 3 |

Reach the find bars by their shortcut, not only by clicking, and return focus to the editor by clicking
after using one. Focus leaving and returning without a click has been a distinct failure.

Scroll far enough down for the place to be unmistakable, since a document that restores nothing still opens
at the top.

For the move case, make the two images easy to tell apart, and read back the address of the image the
preview shows rather than judging it by eye. The edit made before a rename or a move is what shows the
document stayed open, since a document opened again has no undo history.

## Not covered

Editing behaviour that belongs to the editor component itself — completion, folding, multi-cursor
gestures, how well it highlights a language — beyond the interaction between a cursor and a clipboard verb.
