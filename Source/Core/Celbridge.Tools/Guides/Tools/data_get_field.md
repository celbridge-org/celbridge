# data_get_field

Reads a single frontmatter field from a resource's `.cel` sidecar and returns the value as JSON.

Errors when the resource has no sidecar, when the sidecar fails to parse, or when the named field is not present.
