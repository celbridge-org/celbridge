# data_list_tags

Enumerates every distinct tag string across every healthy `.cel` sidecar in the workspace. Powers tag autocomplete and lets agents survey the project's taxonomy without N round-trips through `data_find_tag`.

## Parameters

None.

## Returns

```json
{
  "tags": ["draft", "priority:high", "priority:low", "published", "status:wip"]
}
```

The tag list is sorted ordinal for diff stability. Broken sidecars are skipped (they surface through `data_inspect` instead). An empty workspace returns `{ "tags": [] }`.

## When to use

- "What tags exist in this project?" → one call returns the universe.
- "Tag autocomplete UI" — the sorted list feeds directly into a picker.
- "Survey the namespaces in use" — filter caller-side for `priority:*` etc. to see the values already in circulation.

## Notes

- Runs an on-demand parallel scan over the project's sidecar files; there is no precomputed index.
- The convention `prefix:value` (e.g. `priority:high`, `status:draft`) is supported by the tagging tools as plain strings — namespacing is a string convention, not a parse rule.
- A future `prefix?` parameter to filter namespaces server-side is anticipated but not yet implemented; for now, filter the returned array caller-side.
