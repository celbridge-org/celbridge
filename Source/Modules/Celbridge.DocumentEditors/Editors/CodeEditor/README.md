# celbridge-code-editor

Monaco-based text editor contribution package. Hosts arbitrary code files and — when
the package's options opt in — an adjacent preview pane driven by a format-specific
renderer module. Markdown and HTML are the built-in preview consumers, and the same
bundle serves any future preview format.

## Adding a new file type

Drop a new `<name>.editor.toml` next to `code.editor.toml` and reference it from
`package.toml`'s `contributes.editors`. Use the `[options]` table to opt into
the preview pane and snippet toolbar. Booleans are serialized as the literal strings
`"true"` / `"false"`.

### Options

| Key | Values | Default | Effect |
|---|---|---|---|
| `preview_renderer_url` | Package-root-relative path to an ES module | (unset) | When set, enables the preview pane, the view-mode toolbar, and the source-to-preview content pipeline. The module implements the preview contract described under Preview modules. |
| `initial_view_mode` | `"source"`, `"split"`, `"preview"` | `"source"` | Starting layout. Only honored when `preview_renderer_url` is set. |
| `enable_snippet_toolbar` | `"true"`, `"false"` | `"false"` | Shows the snippet dropdown in the toolbar. Disabled automatically in Preview mode. |
| `snippet_set` | `"markdown"` | (unset) | Selects the snippet definitions populated into the dropdown. Additional sets are registered in `js/snippets.js`. |

### Example

Register `.adoc` files with a custom AsciiDoc preview:

```toml
[editor]
id = "asciidoc"
type = "document"
entry-point = "index.html"
display-name = "CodeEditor_Editor_AsciiDoc"

[options]
preview_renderer_url = "asciidoc-preview/preview-module.js"
initial_view_mode = "preview"
enable_snippet_toolbar = "true"
snippet_set = "asciidoc"

[[file-types]]
extension = ".adoc"
display-name = "CodeEditor_FileType_AsciiDoc"
```

`display-name` in `[editor]` is required — it labels the editor in the
Reopen-with dialog. Use a dedicated localization key per editor so adding a
second editor to the package doesn't collide with the first.

The preview module bundle would live alongside `markdown-preview/` and a matching
`snippet_set` entry would need to be added to `js/snippets.js`.

## Preview modules

`js/preview-controller.js` imports the module named by `preview_renderer_url`. Every module
exports `initialize(iframe, callbacks)`, `render(content)`, `setBasePath(basePath)` and
`setScrollPercentage(percentage)`. The rest of the contract is optional:

| Export | Effect |
|---|---|
| `getScrollPercentage()` | Saves the preview's scroll position with the document's state. |
| `scrollToSourceLine(line, fraction)`, `getTopSourceLine()` | Keeps the preview scrolled to the source in Split mode. Without them the two panes scroll independently. |
| `beginFind()` | Opens a find bar of the module's own, for a WebView that has none built in. |
| `refresh(url)` | Shows the file at the URL, for a module that previews the file on disk rather than the buffer. Called when the document opens, after each save and after an external change, and held back while Source mode hides the preview. |

Link clicks are the controller's, so every preview treats them alike: a link to a place in the page
scrolls there, a relative path or an `http` or `https` URL goes to the host, and any other link is
left to the page.

## References

- `markdown.editor.toml` — live example of a preview-enabled document.
- `html.editor.toml` — a preview of the file itself rather than the buffer.
- `js/main.js` (`parseOptions`) — authoritative option parsing.
- `markdown-preview/preview-module.js` — reference implementation of a module that renders the buffer.
- `html-preview/preview-module.js` — reference implementation of a module that shows the file.
