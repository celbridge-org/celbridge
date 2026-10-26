# file_grep

Searches the contents of text files for a ripgrep regex or plain text. Returns matches with line numbers, optional surrounding context, and optional full-file content for collapsing a grep-then-read workflow into a single call.

There are two modes:

- **Path search**: supply `pattern` plus any of `path`, `include`, `exclude`. The tool walks all text files under the chosen path.
- **Targeted search**: supply `files` as a JSON array of resource keys to search exactly those files. When `files` is non-empty, `path`, `include`, and `exclude` are ignored.

## `.cel` sidecar contents are included

Both modes grep `.cel` sidecar files. The in-app Search panel hides sidecar contents (they are plumbing the user should never see), but `file_grep` lets agents locate metadata text without re-implementing the structured read. Use `file_grep` to **find** text inside a `.cel`; use the `data_*` tools (`data_get_fields`, `data_inspect`) to **read** it structurally, and `data_set_fields` / `data_add_tags` etc. to **modify** it — `file_write` will also accept a `.cel` directly, but the structured tools handle quote-style autoselection and the reserved-namespace rules for you, so they're the right path for routine mutation.

## Match parameters

### pattern

A regex in ripgrep's dialect. The rules are in `regex_syntax`. `foo|bar`, `\bName\b` and `(?i)todo` work as they do in `rg`. A pattern outside the dialect, like a lookahead, fails the call with a message that says what to write instead. A pattern matches within one line.

### literal

When `true`, `pattern` is plain text, the same as `rg -F`. Use it for text full of regex characters, like a resource key or `a.b(c)`.

### matchCase

Defaults to `true`, the same as `rg`. Set `matchCase: false`, or start the pattern with `(?i)`, to ignore letter case.

### wholeWord

Accepts a match only when no word character touches it on either side, the same as `rg -w`. Word characters are letters, digits, marks and `_`. It works in both modes. On a regex, every match must start and end with a word character, so `wholeWord` refuses a pattern like `-foo`.

## Scope parameters

### path

A folder or a file. Only files within the folder (and its descendants) are searched, or only that one file. Empty string searches the whole project. A path that names nothing fails the call.

A path under another root (e.g. `temp:staging/my-pkg`, `logs:`) is searched too. The tool walks that root's tree directly. `include`/`exclude` still apply. A whole-project search with an empty path covers only the project root, so name another root explicitly to search it.

### include / exclude

Search filters, like `"*.cs,*.xaml"`, `"*.{ts,tsx}"`, `"src/**"` and `"*.js,!*.min.js"`. The rules are in `search_filters`. `exclude` wins when a file matches both. The patterns are relative to the root, even when `path` names a folder. So `include: "src/lib/**"` with `path: "src"` searches `src/lib`. A `warning` in the result reports include patterns that matched no file.

### applySearchExclude

A path search skips what the project's search list, `[celbridge.search] exclude`, excludes. Set `applySearchExclude: false` to search those files too. Defaults to `true`.

An excluded `path` is searched in full, and so is a folder inside an excluded folder. So `path: "node_modules/lib"` searches every file in that folder, whatever the flag says. The list never applies to `files`, which names each file directly.

### files

JSON array of resource keys, e.g. `["src/foo.cs","src/bar.cs"]`. When non-empty the tool searches only those files and ignores `path`, `include`, and `exclude`. Useful for a follow-up pass after another tool has narrowed the candidate set.

## Output parameters

### maxResults

Maximum number of matches to return across all files. Defaults to `100`. When the cap is hit (or the underlying search is cancelled) the response sets `truncated: true`.

### contextLines

Number of lines of surrounding context to include before and after each match. `0` (default) returns the matching line only; `2` mirrors `grep -C 2`.

### includeContent

When `true`, each file entry also carries the file's full content. Lets a single call serve as both a locator and a reader.

### summaryOnly

When `true`, returns only the totals and per-file `matchCount`. The `matches` array stays empty, and per-file `content` is left out, even if `includeContent` is set. Use this as a probe before a full call when the search may match many lines — pulling 100 `lineText` payloads can blow the response budget for short well-named patterns. The follow-up call narrows scope via `path`, `include`/`exclude`, or `files`. Defaults to `false`.

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

`matchCount` is always populated. `warning` appears only when the include patterns matched no file. When `summaryOnly` is true, `matches` is an empty array. When `contextLines > 0`, each match additionally carries `contextBefore` and `contextAfter` arrays. When `includeContent` is true (and `summaryOnly` is false), each file entry adds `content`. Match positions (`matchStart`, `matchLength`) are character offsets within `lineText`.

## Response size cap

`file_grep` caps its serialised response at 50,000 characters. When a search would exceed that — typically a broad pattern with many matches, especially with `includeContent` or `contextLines` set — the tool returns an error result instead of the usual payload:

```json
{
  "error": "result_too_large",
  "totalMatches": 412,
  "totalFiles": 38,
  "responseChars": 113824,
  "limitChars": 50000,
  "hint": "Re-run with summaryOnly:true to probe, then narrow scope via path, include/exclude, or files."
}
```

When you hit the cap, the totals tell you the search's scale. Rerun with `summaryOnly: true` to see per-file `matchCount` values, then narrow `path`, `include`/`exclude`, or `files` until the full response fits. `summaryOnly` results almost never trip the cap.
