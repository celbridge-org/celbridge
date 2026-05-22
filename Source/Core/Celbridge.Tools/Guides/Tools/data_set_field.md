# data_set_field

Writes a single frontmatter field on a resource's `.cel` sidecar. Creates the sidecar if it does not exist.

The `value_json` argument carries the value as a JSON-encoded string: `"high"`, `42`, `true`, `["a", "b"]`. Only scalars (string, number, bool) and lists of scalars are accepted; nested objects are rejected at write time.

To mutate the `tags` list prefer `data_add_tag` / `data_remove_tag` so concurrent edits don't clobber each other's append.
