# data_remove_block

Removes a named block from the resource's `.cel` sidecar. No-op when the block is absent or the sidecar does not exist.

The `file_*` byte-write tools refuse `.cel` targets to protect the sidecar's TOML and block-fence structure; this is the structured route they point at.
