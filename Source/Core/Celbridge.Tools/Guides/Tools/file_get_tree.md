# file_get_tree

Returns a recursive folder tree rooted at the given resource as a JSON object. Use this when you need a structural view of multiple levels at once — for the immediate children of a single folder, `file_list_contents` is cheaper.

## Parameters

### resource

The folder to start from. Pass the empty string for the project root.

### depth

Maximum depth to traverse, counted from the root node. Folders at the depth limit that have unexplored children come back with `truncated: true` and an empty `children` array.

### glob

Optional search filter applied to files. The rules are in `search_filters`. A folder stays in the tree when at least one file below it matches. So a glob like `*.py` collapses the tree to the branches containing Python files. A pattern with a `/` is relative to the root, not to the folder walked.

### type

Filter by node type. `"file"` keeps only files (folders remain in the structure when they contain matches). `"folder"` keeps only folders. Empty string returns both.

## Returns

A JSON tree of nodes. Each folder node has `name`, `type: "folder"`, `children`, and an optional `truncated` flag. Each file node has `name` and `type: "file"`. The root node always uses the folder shape, even when the resource itself is the project root.
