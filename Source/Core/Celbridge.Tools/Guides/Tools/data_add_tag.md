# data_add_tag

Appends a tag string to the resource's `tags` frontmatter list. Creates the sidecar if it does not exist. Idempotent: adding a tag that is already present is a no-op.

Use the `tag:value` convention (`priority:high`, `status:draft`) to piggyback structured queries onto the tag surface — `data_find_tag "priority:high"` then enumerates resources carrying that value.

The `file_*` byte-write tools refuse `.cel` targets to protect the sidecar's TOML and block-fence structure; this is the structured route they point at.
