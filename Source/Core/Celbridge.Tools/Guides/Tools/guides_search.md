---
name: guides_search
description: Regex-search the built-in agent guide library by frontmatter and body. Single regex pattern — not fuzzy phrase search; multi-word inputs match as a literal phrase.
---

# guides_search

Searches Celbridge's built-in agent guide library by regex pattern. The library is meta-documentation about Celbridge — tool usage and conventions — and is distinct from project files. To search project file contents, use `file_grep` instead.

Use this when you know what you want but not the exact guide name. The search covers frontmatter (`name`, `description`) and body content; results are ranked by relevance and the strongest matching snippet is returned for each hit.

## Parameters

### pattern

A .NET regex pattern, matched case-insensitively. A literal substring is a valid pattern — plain words like `freeze` or `regex` work without any regex syntax. Use anchors (`^`, `$`), alternation (`a|b`), and character classes when refinement is needed.

Multi-word inputs match as a literal phrase, not a conjunction: `python package` finds the exact sequence `python package`, not guides that mention both words separately. For OR-style matching, use alternation (`python|package`); for ordered loose matching, use `python.*package`.

If the pattern fails to compile, the call still returns successfully but the `error` field carries the compile error and `matches` is empty.

### limit

Maximum number of matches to return. Default 10, hard-capped at 25. Values above the cap are silently clamped. The full match count (regardless of limit) is returned in `totalMatches`, so you can tell whether the limit was exceeded.

## Returns

A JSON object with three fields:

- `matches` — array of `{name, kind, description, snippet}`. Pass any `name` to `guides_read` to fetch the full body.
- `totalMatches` — the unclamped count of guides that matched the pattern.
- `error` — regex compile error message, or null when the pattern compiled successfully.

## Workflow

1. Search to find candidate names.
2. Pick the names that look most relevant from the descriptions and snippets.
3. Call `guides_read` with those names to fetch the full content.

## See also

- `guides_list` — enumerate everything when you don't have a search keyword.
- `guides_read` — fetch full bodies once you have names.
- `regex_syntax` — concept guide on the .NET regex flavour used here and by `file_grep`.
