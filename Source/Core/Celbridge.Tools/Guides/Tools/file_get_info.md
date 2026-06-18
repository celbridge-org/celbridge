# file_get_info

Returns metadata for a file or folder without reading its content. Useful before reading large files (check `size` and `lineCount` to decide whether to page via `file_read`'s `offset` and `limit`) or to confirm a resource exists at the expected key.

## Parameters

### resource

The resource key of the file or folder.

### computeHash (optional, default `false`)

When `true` and the resource is a file, reads the bytes once and returns a lowercase-hex SHA-256 in the result's `hash` field. When `false` (or for folders), `hash` is `null` and no bytes are read. The default is `false` because hashing a large file is expensive — opt in only when you actually need to compare content (e.g. walking two trees during a three-way merge and identifying which files differ without reading each one).

The hash format matches the rest of the codebase (`RemotePackageVersion.contentHash`, `HISTORY.md`'s short fingerprint): compare two `hash` strings with ordinary equality.

The hash is captured after the rest of the metadata snapshot. In the microsecond gap between the two reads a file could in principle change so that `size` and `hash` disagree; for session-mid agent usage this is acceptable.

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
- `hash` — lowercase-hex SHA-256 of the file's bytes when `computeHash: true` was passed; otherwise `null`. Stable across reads of the same content; flipping a single byte changes the whole hex string. Compare two `hash` values with string equality to decide whether two files are byte-identical without reading both.

For folders the result is `type`, `modified`, and `isReadOnly`. The `computeHash` parameter has no effect on folders.

The call fails with a "Resource not found" error if the resource does not exist.
