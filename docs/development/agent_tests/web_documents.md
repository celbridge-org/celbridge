# Web Documents

`.webview` and `.html` documents show a page the application does not author, so everything must work
without the page cooperating. Read the [README](README.md) for the invariants, evidence rules and levels.

## Surfaces

Text fields inside the page, and the document's own chrome: the find bar and, for `.webview`, the address
bar. For an HTML document, also where its links, and the page's own navigations, lead.

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
| An HTML document with a link to another project file | click the link | the file opens in Celbridge, and the page stays as it was | 2 |
| An HTML document with a link to a page on another site | click the link, then cancel the prompt | the document asks before leaving, and the other site receives no request | 2 |
| An HTML document with `target="_blank"` links to another project file and to a page on another site | click each | the project file opens in Celbridge, the page opens in the system browser, and the application keeps running | 2 |
| A page with no editable field focused | paste | nothing changes anywhere | 3 |
| An HTML document whose page, a few seconds after a click, navigates to another project file or opens one in a new window | let each happen | neither opens the file in Celbridge: the navigation asks before leaving, and the new window goes to the system browser | 3 |

Returning focus to the page after using the chrome is the case that has failed, so reach the find bar by
its shortcut as well as by clicking.

These cases need a page with several text fields, and a `.webview` document takes an http or https address
only — a `file://` one fails to open. Serve a small page of your own over loopback and point the document
at that. Making the page report its field values, focus and selection to the same server also gives a way
to read page state back, which the `webview_*` tools do not offer for these documents.

The navigation cases need an HTML document in the project whose page links to another project file, both in
place and with `target="_blank"`, and to a page on a loopback server of your own, whose log shows whether a
request arrived. The system browser opens that page, so make it say it came from a test. For the delayed
cases, have a button start a timer of a few seconds, long enough that the page rather than the click starts
the navigation.

## Not covered

History and page rendering. A `.webview` document follows any link, and where its new windows go is in the
Downloads plan.

## Platform

Every navigation case has failed on macOS before, where the application tells a user's click from the page's
own navigation by other means than on Windows, so run them on both heads.

The heads keep a refused destination unfetched by different means, so the case that checks a server receives
no request is worth reading closely on a run that finds it failing. macOS refuses in WebKit's navigation
policy, before any request is made. Windows cancels the navigation, which WebView2 does not take as a reason
to drop the request it has ready, and stops that request where it intercepts it instead. A WebView2 update
that moved either point could break the case on Windows while the prompt itself still behaves.
