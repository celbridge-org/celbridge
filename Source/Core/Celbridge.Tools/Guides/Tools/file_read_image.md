# file_read_image

Reads an image file and returns it as an inline image content block plus a JSON metadata payload, so the agent can visually inspect the picture. Use `file_read_binary` for any other binary format.

## Supported formats

`.jpg`, `.jpeg`, `.png`, `.gif`, `.webp`. Other extensions are rejected with an error directing you to `file_read_binary`.

## Size cap

Images larger than 5 MB are rejected to avoid saturating the agent's context. The error message says how many bytes the file is. To recover, resize or recompress the image first, or — if the source is the application itself — capture a smaller screenshot via `webview_screenshot` with a `maxEdge` parameter.

## Returns

An inline image content block (the image itself, ready for the model to view) plus a JSON metadata block containing:

- `resource` — the resource key as supplied.
- `mimeType` — one of `image/jpeg`, `image/png`, `image/gif`, `image/webp`.
- `sizeBytes` — original file size on disk.
