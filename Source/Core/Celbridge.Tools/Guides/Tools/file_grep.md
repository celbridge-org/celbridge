# file_grep

Searches the contents of project text files for a string or regex. Returns matches with line numbers, optional surrounding context, and optional full-file content for collapsing a grep-then-read workflow into a single call.

There are two modes:

- **Project search** — supply `searchTerm` plus any of `resource`, `include`, `exclude`. The tool walks all text files under the chosen scope.
- **Targeted search** — supply `files` as a JSON array of resource keys to search exactly those files. When `files` is non-empty, `resource`, `include`, and `exclude` are ignored.

## `.cel` sidecar contents are included

Both modes grep `.cel` sidecar files. The in-app Search panel hides sidecar contents (they are plumbing the user should never see), but `file_grep` lets agents locate metadata text without re-implementing the structured read. Use `file_grep` to **find** text inside a `.cel`; use the `data_*` tools (`data_get_field`, `data_get_info`) to **read** it structurally and `data_set_field` / `data_add_tag` etc. to **modify** it — direct byte writes to `.cel` files are refused by `file_write` and the other byte-write tools to protect the TOML structure.

## Match parameters

### searchTerm

The text or pattern to search for. Plain strings without regex metacharacters work the same in both modes.

### useRegex

When `true`, `searchTerm` is a .NET regex.

### matchCase

Defaults to `false`. Ignored when `useRegex` is true — embed `(?-i)` in the pattern to force case-sensitive matching, or rely on the default case-sensitive regex behaviour.

### wholeWord

Wraps the search term in `\b` boundaries. Ignored when `useRegex` is true — write the boundaries into the pattern yourself.

## Scope parameters

### resource

A folder resource key. Only files within this folder (and its descendants) are searched. Empty string searches the whole project.

### include / exclude

Comma-separated glob lists matched against file names: `"*.cs,*.xaml"`, `"*.generated.cs,*.g.cs"`. `exclude` wins when a file matches both. Globs follow the project-wide convention — see `file_search` for `**` semantics.

### files

JSON array of resource keys, e.g. `["src/foo.cs","src/bar.cs"]`. When non-empty the tool searches only those files and ignores `resource`, `include`, and `exclude`. Useful for a follow-up pass after another tool has narrowed the candidate set.

## Output parameters

### maxResults

Maximum number of matches to return across all files. Defaults to `100`. When the cap is hit (or the underlying search is cancelled) the response sets `truncated: true`.

### contextLines

Number of lines of surrounding context to include before and after each match. `0` (default) returns the matching line only; `2` mirrors `grep -C 2`.

### includeContent

When `true`, each file entry also carries the file's full content. Lets a single call serve as both a locator and a reader.

### summaryOnly

When `true`, returns the totals and per-file `matchCount` without materialising the `matches` array (and without per-file `content`, even if `includeContent` is set). Use this as a probe before a full call when the search may match many lines — pulling 100 `lineText` payloads can blow the response budget for short well-named patterns. The follow-up call narrows scope via `resource`, `include`/`exclude`, or `files`. Defaults to `false`.

## Returns

```json
{
  "totalMatches": 7,
  "totalFiles": 3,
  "truncated": false,
  "files": [
    {
      "resource": "src/foo.cs",
      "fileName": "foo.cs",
      "matchCount": 1,
      "matches": [
        { "lineNumber": 42, "lineText": "...", "matchStart": 8, "matchLength": 5 }
      ]
    }
  ]
}
```

`matchCount` is always populated. When `summaryOnly` is true, `matches` is an empty array. When `contextLines > 0`, each match additionally carries `contextBefore` and `contextAfter` arrays. When `includeContent` is true (and `summaryOnly` is false), each file entry adds `content`. Match positions (`matchStart`, `matchLength`) are character offsets within `lineText`.

## Response size cap

`file_grep` caps its serialised response at 50,000 characters. When a search would exceed that — typically a broad pattern with many matches, especially with `includeContent` or `contextLines` set — the tool returns an error result instead of the usual payload:

```json
{
  "error": "result_too_large",
  "totalMatches": 412,
  "totalFiles": 38,
  "responseChars": 113824,
  "limitChars": 50000,
  "hint": "Re-run with summaryOnly:true to probe, then narrow scope via resource, include/exclude, or files."
}
```

When you hit the cap, the totals tell you the search's scale. Rerun with `summaryOnly: true` to see per-file `matchCount` values, then narrow `resource`, `include`/`exclude`, or `files` until the full response fits. `summaryOnly` results almost never trip the cap.
