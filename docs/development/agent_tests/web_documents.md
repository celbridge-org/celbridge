# Web Documents

`.webview` documents show a page the application does not author, so everything must work without the
page cooperating. Read the [README](README.md) for the invariants, evidence rules and levels.

## Surfaces

Text fields inside the page, links within it, the canvas under a page with no background of its own, and
the document's own chrome: the find bar and the address bar.

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
| A link to a place further down the page itself | click the link | the page scrolls there | 2 |
| A page with no editable field focused | paste | nothing changes anywhere | 3 |
| A page with no background of its own, and another with a dark background, in the dark theme | open each, then switch to another tab and back to each | the first shows on white, as in a browser, and neither shows a flash of another color on the way back | 3 |

Returning focus to the page after using the chrome is the case that has failed, so reach the find bar by
its shortcut as well as by clicking.

These cases need a page with several text fields, and a `.webview` document takes an http or https address
only — a `file://` one fails to open. Serve a small page of your own over loopback and point the document
at that. Making the page report its field values, focus, selection and scroll position to the same server
also gives a way to read page state back, which the `webview_*` tools do not offer for these documents.
Make the page long enough for a link within it to have somewhere to scroll to.

Read the canvas from a real screen capture, never from PrintWindow, which draws a WebView on white whatever
the screen shows. A flash lasts a frame or two, so sample a pixel of the page every frame while switching
back to its tab.

## Not covered

History, and page rendering beyond the canvas under a page. A `.webview` document follows any link, and
where its new windows go is in the Downloads plan. HTML documents open in the HTML editor, which the
[HTML Editor](html_editor.md) plan covers.

## Platform

A link within the page is a macOS case in its own right: the head answers those itself, because Uno cancels
every one of them, so a link that stops scrolling means that answer has been lost rather than that the page
is at fault.

On Windows the find bar can be reached only by its shortcut, since the menu's Edit and View submenus have
no Find item. Reaching it by clicking as well applies to macOS only.

On Windows the close shortcuts do nothing while the keyboard is in the page. A key typed in a page never
reaches the application there, and the page runs no client to forward it. Ctrl+W works once the document's
tab has the keyboard.

The white canvas is a background the document gives its WebView. A page with no background that shows dark
text on the dark theme on one head only means that head is not applying it.
