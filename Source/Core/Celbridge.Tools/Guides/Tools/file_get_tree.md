# file_get_tree

Returns a recursive folder tree rooted at the given resource as a JSON object. Use this when you need a structural view of multiple levels at once — for the immediate children of a single folder, `file_list_contents` is cheaper.

## Parameters

### resource

The folder to start from. Pass the empty string for the project root.

### depth

Maximum depth to traverse, counted from the root node. Folders at the depth limit that have unexplored children come back with `truncated: true` and an empty `children` array.

### glob

Optional glob filter applied to file names. Folders are kept in the tree when they have at least one matching descendant, so a glob like `*.py` collapses the tree to only the branches containing Python files.

### type

Filter by node type. `"file"` keeps only files (folders remain in the structure when they contain matches). `"folder"` keeps only folders. Empty string returns both.

## Returns

A JSON tree of nodes. Each folder node has `name`, `type: "folder"`, `children`, and an optional `truncated` flag. Each file node has `name` and `type: "file"`. The root node always uses the folder shape, even when the resource itself is the project root.

## See also

- `file_list_contents` — single-level listing.
- `file_search` — name-only search across the project.
- `file_get_info` — metadata for a specific resource.
- `resource_keys`.
