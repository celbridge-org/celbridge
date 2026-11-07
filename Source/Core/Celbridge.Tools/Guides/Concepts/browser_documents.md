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

Each bookmark has a `url`, an optional `name` and an optional `open_on_start`. A `url` must be an external `http://` or `https://` address. Local paths and resource keys are not supported here. For project-local HTML, open the `.html` file itself, which shows it in the HTML editor.

A bookmark with `open_on_start = true` opens in a tab when the document opens with no saved tabs. The bookmark button at the left of the URL bar lists every bookmark, and choosing one opens a new tab. The file stores the bookmarks, so a team shares them. The open tabs are saved for each user, and they come back when the project loads again. Closing a document discards its open tabs, so the next open shows the Open on Start bookmarks.

Use `file_write` to create a `.browser` file in one step:

```python
file.write("references/example.browser", '[[bookmarks]]\nurl = "https://www.example.com"\nopen_on_start = true\n')
```

Open it with `document_open` like any other project file. To show a new page, write a new `.browser` file and open that. `document_open` with reload only reads the bookmarks again, and it leaves the open tabs in place.

A page's request for a new window opens a tab when the user started the request. The new page keeps a link to the page that opened it. Presentation mode hides the URL bar and the tab bar.

Pages in `.browser` documents are external and untrusted, so they have no tool bridge, and the `webview_*` tools refuse them. See `webview_devtools` for the documents those tools can reach.
