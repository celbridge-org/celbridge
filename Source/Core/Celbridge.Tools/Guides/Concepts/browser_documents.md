# `.browser` documents

A `.browser` file is a TOML file listing the bookmarks of a browser document. The document shows external web pages in tabs, with a tab bar and a URL bar above the page.

```toml
[[bookmarks]]
url = "https://example.com/"
name = "Example"
open_on_start = true

[[bookmarks]]
url = "https://example.com/docs/"
name = "Docs"
```

Each bookmark has a `url`, an optional `name`, an optional `open_on_start` and an optional `favicon`. A `url` must be an external `http://` or `https://` address. Local paths and resource keys are not supported here. For project-local HTML, open the `.html` file itself, which shows it in the HTML editor.

A bookmark with `open_on_start = true` opens in a tab when the document opens with no saved tabs. With no such bookmark, the document opens one blank tab. A document always has at least one tab, so closing the last tab leaves a blank tab. The bookmark button at the left of the URL bar lists every bookmark. Choosing one loads it in a blank shown tab. With any other tab shown, it opens in a new tab. The file stores the bookmarks, so a team shares them. The open tabs are saved for each user, and they come back when the project loads again. Closing a document discards its open tabs, so the next open shows the Open on Start bookmarks.

A read-only `.browser` file has fixed bookmarks, and the editor grays out the controls that change them. `file_set_writeable` makes a file read-only, or writable again. The Community document is read-only, and every project load writes it again.

Celbridge writes the `favicon` key itself. The key contains the site's icon as a base64 PNG. Celbridge adds the key when a tab first shows the bookmark's page. Leave the key out of a new bookmark. When editing a file, keep each `favicon` line as it is. To change a bookmark's `url`, also remove its `favicon` line. Celbridge then stores the new page's icon on the next visit.

Use `file_write` to create a `.browser` file in one step:

```python
file.write("references/example.browser", '[[bookmarks]]\nurl = "https://www.example.com"\nopen_on_start = true\n')
```

Open it with `document_open` like any other project file. To show a new page, write a new `.browser` file and open that. `document_open` with reload only reads the bookmarks again, and it leaves the open tabs in place.

A page's request for a new window opens a tab when the user started the request. The new page keeps a link to the page that opened it. Presentation mode hides the URL bar and the tab bar.

Pages in `.browser` documents are external and untrusted, so they have no tool bridge, and the `webview_*` tools refuse them. See `webview_devtools` for the documents those tools can reach.
