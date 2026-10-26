# data_find_tag

Enumerates every resource in the project whose `.cel` sidecar `tags` list contains the given tag value. Runs an on-demand parallel scan over the project's sidecar files; results are sorted by resource key.

The scan skips what the project's search list, `[celbridge.search] exclude`, excludes. Set `applySearchExclude: false` to scan those sidecars too.

The response is a bare JSON array of resource keys (e.g. `["docs/notes.md", "drafts/article.md"]`), not an object. An empty list when no sidecar carries the tag.

For non-tag searches across `.cel` contents, use `file_grep` with `include: "*.cel"` and parse hits caller-side.
