# data_read_block

Returns the verbatim content of a named block in the resource's `.cel` sidecar.

Errors when the resource has no sidecar, when the sidecar is broken, or when the named block does not exist.

For partial reads of large blocks, prefer `file_read docs/notes.md.cel` with the existing line offset / limit parameters.
