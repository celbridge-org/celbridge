# data_write_block

Writes a named content block in the resource's `.cel` sidecar. Creates the sidecar if missing; overwrites the existing block if one with the same ID is present, otherwise appends a new block.

Block content is opaque to the host. Whatever the editor stores round-trips through Parse and Compose unchanged, subject to the documented constraint that block content cannot contain a line matching the fence regex `^\+\+\+ "..."`.

The `file_*` byte-write tools refuse `.cel` targets to protect the sidecar's TOML and block-fence structure; this is the structured route they point at.
