# file_read_many

Reads multiple text files in a single call. Each file is read independently, so a missing file or invalid resource key produces a per-entry error rather than failing the entire request. Useful after `file_grep` or `file_search` returns a candidate set you want to inspect together.

## Parameters

### resources

JSON array of resource keys, e.g. `["src/foo.cs", "src/bar.cs"]`. The array must be non-empty.

### offset / limit

Applied uniformly to every file in the batch. `offset` is the 1-based starting line (`0` means start at the beginning), `limit` is the maximum number of lines to return per file (`0` means read to the end). For different ranges per file, issue separate `file_read` calls.

## Returns

A JSON object with a `files` array, one entry per resource key in the order supplied. Each entry has:

- `resource` — the resource key as supplied.
- On success: `content` (string) and `totalLineCount` (int, the whole file's line count, not the returned range's).
- On failure: `error` (string) describing why this file could not be read.

`content` and `error` are mutually exclusive within a single entry.

## See also

- `file_read` — single-file read with the same offset/limit semantics.
- `file_grep` with `includeContent: true` — combined locate-and-read in one call.
- `resource_keys`.
