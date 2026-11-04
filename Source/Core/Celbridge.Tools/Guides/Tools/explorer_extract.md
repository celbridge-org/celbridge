# explorer_extract

Extracts a zip archive that lives somewhere under the project content root into a destination folder. The counterpart to `explorer_archive`.

The resource policy refuses writes to some paths, for example anything inside a `.git` folder. Extraction skips each entry at a refused path, extracts the rest, and counts the skipped entries in `skipped`.

## Parameters

- `archive` — resource key of the zip file to extract.
- `destination` — resource key of the target folder. Created if it does not exist.
- `overwrite` — when `false` (default), an entry that would clobber an existing file causes the call to fail. Set to `true` to replace existing files.

## Returns

A JSON object:

- `entries` (int) — number of files extracted.
- `destination` (string) — resource key of the destination folder.
- `skipped` (int) — number of entries skipped because the resource policy refused their paths.
