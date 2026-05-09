# file_list_contents

Lists the immediate children of a folder. For a recursive view, use `file_get_tree`; for project-wide name searches, use `file_search`.

## Examples

```python
# What's at the project root?
cel.file.list_contents("")

# Files and folders in a specific folder
cel.file.list_contents("Scripts")

# Filter to Python files in a folder
cel.file.list_contents("Scripts", glob="*.py")
```

## Parameters

### resource

Folder resource key. The empty string `""` lists the project root — that's the canonical way to discover what the project contains when you don't yet know its top-level layout.

### glob

Optional glob matched (case-insensitively) against entry names. The match is on the bare name only, not the full path, so `*.py` filters to Python files in that folder.

## Returns

A JSON array of entries, each with `name`, `type` (`"file"` or `"folder"`), `modified` (ISO 8601 UTC), and `size` for files. Folders do not carry a `size` field.

## See also

- `file_get_tree` — recursive listing with depth limit.
- `file_search` — name-only search across the project.
- `file_get_info` — metadata for a known resource.
- `resource_keys`.
