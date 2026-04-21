# celbridge.code-editor

Monaco-based text editor contribution package. Hosts arbitrary code files and — when
the package's options opt in — an adjacent preview pane driven by a format-specific
renderer module. Markdown is the built-in preview consumer; the same bundle serves any
future preview format.

## Adding a new file type

Drop a new `<name>.document.toml` next to `code.document.toml` and reference it from
`package.toml`'s `contributes.document_editors`. Use the `[options]` table to opt into
the preview pane and snippet toolbar. Booleans are serialized as the literal strings
`"true"` / `"false"`.

### Options

| Key | Values | Default | Effect |
|---|---|---|---|
| `preview_renderer_url` | URL to an ES module | (unset) | When set, enables the preview pane, the view-mode toolbar, and the source-to-preview content pipeline. The module must export `initialize`, `render`, `setBasePath`, `setScrollPercentage`, `getScrollPercentage` (see `markdown-preview/preview-module.js`). |
| `initial_view_mode` | `"source"`, `"split"`, `"preview"` | `"source"` | Starting layout. Only honored when `preview_renderer_url` is set. |
| `enable_snippet_toolbar` | `"true"`, `"false"` | `"false"` | Shows the snippet dropdown in the toolbar. Disabled automatically in Preview mode. |
| `snippet_set` | `"markdown"` | (unset) | Selects the snippet definitions populated into the dropdown. Additional sets are registered in `js/snippets.js`. |

### Example

Register `.adoc` files with a custom AsciiDoc preview:

```toml
[document]
id = "asciidoc-document"
type = "custom"
entry_point = "index.html"
priority = "specialized"

[options]
preview_renderer_url = "https://pkg-celbridge-code-editor.celbridge/asciidoc-preview/preview-module.js"
initial_view_mode = "preview"
enable_snippet_toolbar = "true"
snippet_set = "asciidoc"

[[document_file_types]]
extension = ".adoc"
display_name = "CodeEditor_FileType_AsciiDoc"
```

The preview module bundle would live alongside `markdown-preview/` and a matching
`snippet_set` entry would need to be added to `js/snippets.js`.

## References

- `markdown.document.toml` — live example of a preview-enabled document.
- `js/main.js` (`parseOptions`) — authoritative option parsing.
- `markdown-preview/preview-module.js` — reference implementation of the preview contract.
