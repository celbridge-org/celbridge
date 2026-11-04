# explorer_extract

Extracts a zip archive that lives somewhere under the project content root into a destination folder. The counterpart to `explorer_archive`.

An entry the project cannot hold, for example an entry inside a `.git` folder, is left out. The rest of the archive is extracted, and `skipped` counts the entries left out.

## Parameters

- `archive` — resource key of the zip file to extract.
- `destination` — resource key of the target folder. Created if it does not exist.
- `overwrite` — when `false` (default), an entry that would clobber an existing file causes the call to fail. Set to `true` to replace existing files.

## Returns

A JSON object:

- `entries` (int) — number of files extracted.
- `destination` (string) — resource key of the destination folder.
- `skipped` (int) — number of entries left out because the project cannot hold them.
