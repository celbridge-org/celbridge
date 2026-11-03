# file_get_tree

Returns a recursive folder tree rooted at the given resource as a JSON object. Use this when you need a structural view of multiple levels at once — for the immediate children of a single folder, `file_list_contents` is cheaper.

## Parameters

### resource

The folder to start from. Pass the empty string for the project root.

### depth

Maximum depth to traverse, counted from the root node. Folders at the depth limit that have unexplored children come back with `truncated: true` and an empty `children` array. A glob keeps a folder at the depth limit only when the glob could match inside it.

### glob

Optional search filter applied to files. The rules are in `search_filters`. A folder stays in the tree when at least one file below it matches. So a glob like `*.py` collapses the tree to the branches containing Python files. A pattern with a `/` is relative to the root, not to the folder walked.

### type

Filter by node type. `"file"` lists files and the folders that contain them, and leaves out each folder with no file below it. `"folder"` lists only folders, and a glob narrows them to the folders that have a matching file below them. Empty string returns both. Any other value fails the call.

## Returns

A JSON tree of nodes. Each folder node has `name`, `type: "folder"`, `children`, and an optional `truncated` flag. Each file node has `name` and `type: "file"`. The root node always uses the folder shape, even when the resource itself is the project root. The root comes back with its name and an empty `children` array when nothing matches.
