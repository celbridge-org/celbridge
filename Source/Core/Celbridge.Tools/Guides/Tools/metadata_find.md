# metadata_find

Finds every resource whose `.cel` sidecar frontmatter contains the given field with the given value.

## Arguments

- `field` — the top-level frontmatter field name. Case-sensitive.
- `value_json` — the query value as a JSON-encoded string. Must be a scalar (`"string"`, `42`, `true`). Lists are not accepted — list-of-scalar fields match by element, so pass the scalar you want to match against.

## Returns

A JSON array of resource keys (`["docs/notes.md", "drafts/idea.md"]`). Empty array when no resource matches.

## Match semantics

- Scalar fields match by equality. `metadata_find "priority" "high"` returns every resource with `priority = "high"` in its frontmatter.
- List-of-scalar fields match by contains. `metadata_find "tags" "flagged"` returns every resource whose `tags` list contains `"flagged"` (alongside any other tags it may carry).
- Object fields and lists of non-scalars are not indexed. To filter on those, read each candidate via `metadata_list` and filter locally.
- Strings match case-sensitively. Numeric queries are normalised so an `int` query matches a `long` cached value.

## Notes

- Tag-specific affordances `metadata_add_tag` / `metadata_remove_tag` exist alongside this generic tool. For "find resources tagged X" specifically, `metadata_find "tags" "X"` and `metadata_find` with `field="tags"` are the same call.
- Results are unordered. Callers needing a stable sort should sort the response array client-side.
