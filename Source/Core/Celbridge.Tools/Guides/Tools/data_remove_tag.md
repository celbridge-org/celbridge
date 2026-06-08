# data_remove_tag

Removes a tag string from the resource's `tags` frontmatter list. No-op when the tag is absent. Drops the `tags` field entirely when the list goes empty after removal.

The `file_*` byte-write tools refuse `.cel` targets to protect the sidecar's TOML and block-fence structure; this is the structured route they point at.
