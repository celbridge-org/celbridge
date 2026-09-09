# Web Documents

`.webview` and `.html` documents show a page the application does not author, so everything must work
without the page cooperating. Read the [README](README.md) for the invariants, evidence rules and levels.

## Surfaces

Text fields inside the page, and the document's own chrome: the find bar and, for `.webview`, the address
bar.

## Cases

| Situation | Action | Expected | Level |
|---|---|---|---|
| A text field in the page | paste | text enters the field | 1 |
| The find bar used first, then a page field clicked | paste | text enters the page field | 1 |
| The document's find bar | paste | text enters the find bar | 2 |
| A text field in the page, with a selection | cut, copy, select all | each acts on the field | 2 |
| A page field, after focus has moved to the app and back | paste | text enters the page field | 2 |
| A page field | Tab | focus moves to the next control in the page, and does not leave the document | 2 |
| The address bar | paste, select all | each acts on the address bar | 2 |
| A page with no editable field focused | paste | nothing changes anywhere | 3 |

Returning focus to the page after using the chrome is the case that has failed, so reach the find bar by
its shortcut as well as by clicking.

## Not covered

Navigation, history and page rendering.
