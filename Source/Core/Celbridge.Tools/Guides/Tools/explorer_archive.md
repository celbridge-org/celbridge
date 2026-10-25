# explorer_archive

Creates a zip archive from a file or folder inside the project. When archiving a folder, the archive contains the folder's *contents* at the root, not the folder itself — extracting it back over an empty folder reproduces the original layout.

## Parameters

### resource

Resource key of the file or folder to archive.

### archive

Resource key for the output zip file. The parent folder must already exist.

### include

Optional search filter. The rules are in `search_filters`. Patterns are separated by commas. When set, only matching files are added (e.g. `"*.py,*.md"`). When empty, all files are included.

### exclude

Optional search filter of files to skip (e.g. `"__pycache__,*.log"`). An exclude wins over an include. The patterns match each file's path from the project folder, so a name pattern like `*.log` is the simplest choice.

### overwrite

When `false` (default), the call fails if the archive resource already exists. Set to `true` to replace it.

## Returns

A JSON object:

- `entries` (int) — number of files written into the zip.
- `size` (long) — archive size in bytes.
- `archive` (string) — resource key of the created archive.
