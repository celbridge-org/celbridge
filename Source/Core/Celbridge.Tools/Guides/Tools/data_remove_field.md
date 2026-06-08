# data_remove_field

Removes a single field from a resource's `.cel` sidecar. No-op when the field is absent or the sidecar does not exist.

The `file_*` byte-write tools refuse `.cel` targets to protect the sidecar's TOML structure; this is the structured route they point at.
