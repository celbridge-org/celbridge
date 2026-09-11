# Code Editor

Markdown and code documents. One editor serves both, so they are tested together; the preview and its find
bar apply only to Markdown. Read the [README](README.md) for the invariants, evidence rules and levels.

## Surfaces

The editor text, the editor's own find widget, the preview pane and the preview's find bar, across the
source, split and preview view modes.

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

Reach the find bars by their shortcut, not only by clicking, and return focus to the editor by clicking
after using one. Focus leaving and returning without a click has been a distinct failure.

## Not covered

Editing behaviour that belongs to the editor component itself — completion, folding, multi-cursor
gestures, syntax highlighting — beyond the interaction between a cursor and a clipboard verb.
