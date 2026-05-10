# `.webview` documents

A `.webview` file is a JSON file with a single `sourceUrl` property naming an external web page to display in an embedded browser panel.

```json
{ "sourceUrl": "https://example.com" }
```

`sourceUrl` must be an external `http://` or `https://` URL. Local paths and resource keys are not supported here — for project-local HTML, use the HTML viewer document type instead.

Use `file_write` to create a `.webview` file in one step:

```python
file.write("references/anthropic.webview", '{ "sourceUrl": "https://www.anthropic.com" }')
```

Open it with `document_open` like any other project file. The resource key plays the same role as other documents — `webview_*` devtools target it by resource key — but external-URL `.webview` documents are excluded from the devtools surface (`webview_eval`, `webview_inspect`, etc.) because the host does not trust their content. See `webview_devtools` for which targets qualify.
