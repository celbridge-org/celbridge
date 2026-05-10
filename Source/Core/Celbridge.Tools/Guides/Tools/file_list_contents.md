# file_list_contents

Lists the immediate children of a folder. For a recursive view, use `file_get_tree`; for project-wide name searches, use `file_search`.

## Parameters

### resource

Folder resource key. The empty string `""` lists the project root — the canonical way to discover what the project contains when you do not yet know its top-level layout.

### glob

Optional glob matched (case-insensitively) against entry names. The match is on the bare name only, not the full path, so `*.py` filters to Python files in that folder.

## Returns

A JSON array of entries, each with `name`, `type` (`"file"` or `"folder"`), `modified` (ISO 8601 UTC), and `size` for files. Folders do not carry a `size` field.
