# HTML Editor

HTML documents open in the HTML editor: the page's source beside a preview of the page, served from the
project as it will be served anywhere and reloaded when the user asks. Read the [README](README.md) for the
invariants, evidence rules and levels.

## Surfaces

The previewed page, its links and the fields in it, and the source beside it, across the source, split and
preview view modes. Also where the preview's links lead, the page's own navigations, the toolbar's Reload
button and the F5 key, the find bar over the preview, and a document renamed while it is open.

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
| A document in split mode, with an edit made in the source | rename it from the Explorer, edit the source again, then click Reload | the preview moves to the new name at once and the document stays the active one, the preview shows both edits after the click, and undo in the source still steps back through them | 2 |
| A document in split mode, with an edit made in the source | press F5 with the keyboard in the source, then edit again and press F5 with the keyboard in the preview | each press does what the Reload button does, so the preview shows each edit once it is saved, and the editor page itself does not reload: the mode stays split and undo still steps back through both edits | 2 |
| The same document | switch to source mode | the source shows with HTML highlighting | 3 |
| The same document, still in source mode | read the Reload button's state, then read it again in split mode and in preview mode | it is disabled in source mode, and enabled in the other two | 3 |
| A document in source mode, with an edit made in the source | press F5, then Ctrl+R and Ctrl+Shift+R | nothing reloads: the source keeps the edit and its undo history, the preview shows what it showed before, and the disabled Reload button shows no fill | 3 |
| A text field in the previewed page, with a selection | cut, copy, select all | each acts on the field, and the source is unchanged | 3 |
| A text field in the previewed page | Tab | focus moves to the next control in the page, and the source is not indented | 3 |
| A text field in the previewed page, after focus has moved to the app and back | paste | text enters the field, and the source is unchanged | 3 |
| Preview mode, with nothing in the page focused | paste | nothing changes, and in particular the hidden source is not edited | 3 |
| Preview mode, with the keyboard in the previewed page | open find, then search for words the page shows and for words only its script holds | a find bar opens over the page and finds only the shown words. The source is unchanged | 3 |
| A page that, a few seconds after a click, navigates itself to another project file, or opens one in a new window | let each happen, then click Reload | neither opens the file as a document or in a browser: the preview shows where the page went until Reload brings the document back, and the new window opens nowhere | 3 |
| An open document | call `webview_get_html` | it returns the previewed page, not the editor around it | 3 |
| A document whose file name holds `#` or `%` | open it | the preview shows that file | 3 |
| A `mailto:` link in the previewed page | click it | the system's mail app opens a message to that address with no prompt, and the preview stays where it was | 3 |
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

At the Bottom area's default height the page's lower controls sit under the console, and a click there lands
in the terminal, so scroll the preview before clicking them.

Click the download links with real pointer input. On Windows a page may start one download without a user
gesture and has the next held back, and a synthetic click carries none. Reopening the document between the
two clicks also resets the limit.

The `webview_*` tools reach the previewed page, so read field values, the image, the script's output, the
stylesheet's effect and the scroll position back through them rather than off the screen. The toolbar
belongs to the editor page around the preview, so read the Reload button's state with the tools' `frame`
set to `top`. For the rename case, make the first edit before renaming, since a document opened again has
no undo history to step back through, and read `document_get_state` after the rename: `activeDocument`
names the new file and the renamed tab is the active one. In the rename and F5 cases, type the edits that
undo steps back through. Edits made by script run together into one undo step, so a single undo removes
them all.

For the paste with nothing focused, log the page's `paste` event. A paste that never arrived also leaves
everything unchanged.

For the F5 cases, set a marker on the editor page with `webview_eval` and `frame` set to `top` before
pressing anything. A reload of the editor page drops the marker, and also resets the view mode and the
theme. `app_simulate_input` presses F5 through the application's own key routing, but it refuses modifiers
on Windows, so press Ctrl+R and Ctrl+Shift+R with real keys there.

## Not covered

Editing the source, which is the code editor's own and which the [Code Editor](code_editor.md) plan
covers. Downloads beyond the two link shapes here, which the [Downloads](downloads.md) plan covers.
`.webview` documents, which the [Web Documents](web_documents.md) plan covers.

## Platform

The editor scrolls to a link within the page itself, on both heads. On macOS Uno cancels a same-document
navigation that the editor allows. The host's workaround for that reaches the page but not a frame inside it.
The preview's frame, though, shows an address the editor refuses. Uno reads that refusal the wrong way round
too, so on macOS the frame would move even without the editor's scroll. The anchor case here therefore cannot
detect a lost scroll on macOS. The Code Editor plan's markdown anchor case can.

Reload fetches the saved file again, which the application's server tells every head not to cache. A
Reload that shows an older version on one head only, while the source goes on saving, is that head
answering from its cache.

On macOS, check the preview on screen after a Reload that keeps the scroll position, as well as reading it
back. WKWebView once left such a page blank until the reader scrolled it, while every tool read the page
correctly.

The find bar in the preview differs by head. On Windows, WebView2's own find opens. On macOS the WebView has
no find, so the editor installs its own find bar in the page the first time find is used. From then on, the
page's DOM contains the bar.

WebView2 reloads the page on F5 and Ctrl+R unless the page cancels the key, so on Windows an editor page
that reloads on one of them means the cancel has been lost. WKWebView has no reload key, so on macOS the F5
cases test only the editor's own handling of the key.

On Windows, a machine whose mail association is out of date shows Windows' own "How do you want to open
this?" picker for a mail link, whichever application opens it. Before reporting a prompt in the `mailto:`
case, open the same address from outside the application, such as with `Start-Process` in PowerShell. Close
the picker without choosing an application, since a choice changes the user's default.
