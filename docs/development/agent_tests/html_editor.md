# HTML Editor

HTML documents open in the HTML editor: the page's source beside a preview of the page, served from the
project as it will be served anywhere and reloaded when the user asks. Read the [README](README.md) for the
invariants, evidence rules and levels.

## Surfaces

The previewed page, its links and the fields in it, and the source beside it, across the source, split and
preview view modes. Also where the preview's links lead, the page's own navigations, the toolbar's Reload
button, and a document renamed while it is open.

## Cases

| Situation | Action | Expected | Level |
|---|---|---|---|
| An HTML document | open it | it opens in preview mode, showing the rendered page | 1 |
| The same document in split mode | edit the source, and click Reload straight away | the preview shows the edit about a second later, once the edit is saved, with no save action from the user | 1 |
| A page with a relative image and an inline script that writes to the page | preview it, then edit the page and click Reload | the image shows and the script's output appears, before the edit and after the reload | 2 |
| A text field in the previewed page | paste | text enters the field, and the source is unchanged | 2 |
| A link to a place further down the previewed page | click it | the preview scrolls there, and the document stays open in the same editor | 2 |
| Links to another HTML document in the project and to a page on another site, each in place and with `target="_blank"` | click each | each project link opens that file as a document in the same editor, each external link opens the system browser with no prompt, and the preview stays on the page the links were on | 2 |
| `download` links to a file in the project, one in place and one asking for a new window | click each | both land in the project's downloads folder, and the badge counts them | 2 |
| The page scrolled part way down, with text in its field, and a stylesheet it links changed on disk | click Reload | the field is empty, the page is scrolled where it was, and it now shows the changed stylesheet, which it did not before the click | 2 |
| An open document | rewrite its file outside the editor, with an agent write or another tool, then click Reload | the source follows the new file at once and the Reload button fills with the accent color, and the preview shows the new file only after the click, which clears the fill | 2 |
| A document in split mode, with an edit made in the source | rename it from the Explorer, edit the source again, then click Reload | the preview moves to the new name at once, shows both edits after the click, and undo in the source still steps back through them | 2 |
| The same document | switch to source mode | the source shows with HTML highlighting | 3 |
| The same document, still in source mode | read the Reload button's state, then read it again in split mode and in preview mode | it is disabled in source mode, and enabled in the other two | 3 |
| A text field in the previewed page, with a selection | cut, copy, select all | each acts on the field, and the source is unchanged | 3 |
| A text field in the previewed page | Tab | focus moves to the next control in the page, and the source is not indented | 3 |
| A text field in the previewed page, after focus has moved to the app and back | paste | text enters the field, and the source is unchanged | 3 |
| Preview mode, with nothing in the page focused | paste | nothing changes, and in particular the hidden source is not edited | 3 |
| A page that, a few seconds after a click, navigates itself to another project file, or opens one in a new window | let each happen, then click Reload | neither opens the file as a document or in a browser: the preview shows where the page went until Reload brings the document back, and the new window opens nowhere | 3 |
| An open document | call `webview_get_html` | it returns the previewed page, not the editor around it | 3 |
| A document in split mode with a dragged divider | close and reopen it | the mode and the split ratio are restored | 3 |
| A document left in split mode | reopen it with the general code editor from the tab menu | it opens at the top in source mode, with nothing of the HTML editor's view carried over | 3 |

Most cases run down one page in the project, which the application serves itself: a text field, a
relative image and a stylesheet beside the page, an inline script that writes to the page, a link to a
place far enough down to need scrolling, links to another HTML document in the project and to a page on
another site, each in place and with `target="_blank"`, and `download` links to a file in the project, in
place and asking for a new window. Serve the other site's page from a small server of your own on a
loopback port, and make it say it came from a test, since the system browser opens it. For the page's own
navigations, give it buttons that start a timer of a few seconds, long enough that the page rather than the
click moves the preview. For the Reload case, change a color in the stylesheet rather than anything that
moves the layout, so the scroll position can be compared.

Click the download links with real pointer input. On Windows a page may start one download without a user
gesture and has the next held back, and a synthetic click carries none. Reopening the document between the
two clicks also resets the limit.

The `webview_*` tools reach the previewed page, so read field values, the image, the script's output, the
stylesheet's effect and the scroll position back through them rather than off the screen. The toolbar
belongs to the editor page around the preview, so read the Reload button's state with the tools' `frame`
set to `top`. For the rename case, make the first edit before renaming, since a document opened again has
no undo history to step back through.

## Not covered

Editing the source, which is the code editor's own and which the [Code Editor](code_editor.md) plan
covers. Downloads beyond the two link shapes here, which the [Downloads](downloads.md) plan covers.
`.webview` documents, which the [Web Documents](web_documents.md) plan covers.

## Platform

A link within the page is scrolled by the editor itself on both heads. On macOS Uno cancels every
same-document navigation, and the head's own answer to that reaches the page and not a frame inside it,
so a link that stops scrolling on macOS alone means the editor's scroll has been lost.

Reload fetches the saved file again, which the application's server tells every head not to cache. A
Reload that shows an older version on one head only, while the source goes on saving, is that head
answering from its cache.
