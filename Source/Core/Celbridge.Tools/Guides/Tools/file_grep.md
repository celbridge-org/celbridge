---
name: file_grep
description: Content search across project files, with literal/regex modes, glob scoping, context lines, and result capping.
---

# file_grep

Searches the contents of project text files for a string or regex. Returns matches with line numbers, optional surrounding context, and optional full-file content for collapsing a grep-then-read workflow into a single call.

There are two modes:

- **Project search** — supply `searchTerm` plus any of `resource`, `include`, `exclude`. The tool walks all text files under the chosen scope.
- **Targeted search** — supply `files` as a JSON array of resource keys to search exactly those files. When `files` is non-empty, `resource`, `include`, and `exclude` are ignored.

## Match parameters

### searchTerm

The text or pattern to search for. Plain strings without regex metacharacters work the same in both modes.

### useRegex

When `true`, `searchTerm` is a .NET regex. See `regex_syntax` for flavour details (named groups, lookbehind, Unicode `\d`, etc.).

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
      "matches": [
        { "lineNumber": 42, "lineText": "...", "matchStart": 8, "matchLength": 5 }
      ]
    }
  ]
}
```

When `contextLines > 0`, each match additionally carries `contextBefore` and `contextAfter` arrays. When `includeContent` is true, each file entry adds `content`. Match positions (`matchStart`, `matchLength`) are character offsets within `lineText`.

## See also

- `regex_syntax` — .NET regex flavour and gotchas.
- `file_search` — name-only search; cheaper when you don't need content.
- `file_find_replace` — apply a substitution after locating matches.
- `file_read`, `file_read_many` — read full content for files identified by grep.
- `resource_keys`.
