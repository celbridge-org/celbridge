# file_get_info

Returns metadata for a file or folder without reading its content. Useful before reading large files (check `size` and `lineCount` to decide whether to page via `file_read`'s `offset` and `limit`) or to confirm a resource exists at the expected key.

## Returns

The shape depends on whether the resource is a file or a folder. Both shapes carry a `type` discriminator (`"file"` or `"folder"`) and a `modified` timestamp in ISO 8601 (round-trip) format.

For files:

- `type` — `"file"`.
- `size` — bytes on disk.
- `modified` — last-modified UTC timestamp.
- `extension` — lower-cased extension including the leading dot (e.g. `.cs`).
- `isText` — true when the content is treated as text. Binary files only have meaningful values for `size`, `extension`, and `modified`.
- `lineCount` — number of lines for text files, otherwise null.
- `isReadOnly` — true when the file carries the filesystem read-only attribute. Write operations (`file_write`, `file_edit`, etc.) will fail until the attribute is cleared via `file_set_writeable`. Common on files imported from archives, source-control checkouts, or network shares.
- `sidecar`, `sidecarStatus` — the paired `.cel` sidecar's resource key and parse state (`"healthy"`, `"broken"`, or `"none"` when absent).

For folders the result is `type`, `modified`, and `isReadOnly`.

The call fails with a "Resource not found" error if the resource does not exist.
