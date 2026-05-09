# file_read_binary

Reads a binary file and returns its bytes as a base64 string with a MIME type guessed from the extension. Use this for non-text content the agent needs to forward (downloaded archives, PDFs, fonts, etc.).

For images that should display inline in the agent's view, prefer `file_read_image` — it returns a real image content block in addition to metadata, so the model can actually look at the picture.

## Returns

A JSON object with:

- `base64` — the file's bytes as a standard base64 string.
- `mimeType` — derived from the file extension; falls back to a generic value when the extension is unknown.
- `size` — byte count of the original file (not the encoded length).

## See also

- `file_read_image` — JPEG/PNG/GIF/WebP with inline visual content.
- `file_write_binary` — write base64 content back to disk.
- `file_get_info` — size and extension before reading.
- `resource_keys`.
