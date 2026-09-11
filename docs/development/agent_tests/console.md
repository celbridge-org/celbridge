# Console

A terminal and a settings form share one document, and the settings form replaces the terminal rather than
covering it. Read the [README](README.md) for the invariants, evidence rules and levels.

## Surfaces

The terminal, and the fields of the settings form across its tabs, including the cards its list tabs
create.

## Cases

| Situation | Action | Expected | Level |
|---|---|---|---|
| The terminal | paste | the text arrives at the prompt | 1 |
| A field in the settings form | paste | text enters the field; nothing reaches the terminal behind it | 1 |
| The terminal, with a selection confirmed to exist | copy | the selection reaches the clipboard | 2 |
| The terminal, with no selection | copy | the clipboard is left alone rather than overwritten with nothing | 2 |
| The terminal | Tab | the shell completes | 2 |
| A settings form with several fields | Tab | focus moves to the next field | 2 |
| Settings opened and closed again | paste | the terminal takes it once more | 2 |
| A settings field | cut, copy, select all, undo | each acts on the field | 3 |
| A field in a card created by one of the list tabs | paste | text enters the field | 3 |
| The settings form open, nothing focused | paste | nothing reaches the hidden terminal | 3 |

The terminal is a live shell: check that a paste reaches the prompt as text, and never assume it did
because something appeared on screen.

## Not covered

Session behaviour — launching, restarting, runners and triggers — beyond what is needed to have a prompt
to type at.
