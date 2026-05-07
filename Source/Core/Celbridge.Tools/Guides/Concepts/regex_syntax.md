---
name: regex_syntax
description: The regex flavour Celbridge tools (file_grep, guides_search, etc.) use, and the gotchas relative to PCRE/Python.
---

# Regex syntax

Tools that accept regex patterns use **.NET `System.Text.RegularExpressions` syntax**. This applies to:

- `file_grep` (when called with `useRegex: true`)
- `guides_search`
- Any other tool that documents its `pattern` parameter as a regex

## Key differences from other flavours

- **Named groups use `(?<name>...)`** — not the PCRE-style `(?P<name>...)`.
- **Variable-length lookbehinds are supported.** `(?<!foo)bar` works regardless of the `foo` length.
- **`\w` and `\d` are Unicode-aware by default.** `\d` matches any Unicode decimal digit, not just `[0-9]`. Use `[0-9]` if you need ASCII-only.
- **`\K` (PCRE keep) is not available.** Use a lookbehind instead.
- **Case-insensitive matching** — `guides_search` defaults to case-insensitive; for `file_grep` use the `(?i)` inline flag or the tool's case-insensitivity option.

## Common patterns

| Goal | Pattern |
|---|---|
| Literal substring | `TODO` |
| Anchored at start of line | `^import ` (in multi-line mode, e.g. `(?m)^import `) |
| Word boundary | `\bregex\b` |
| Alternation | `(grep|search)` |
| Optional segment | `colou?r` |

Plain words are valid regex without metacharacters, so a literal substring search and a regex search look the same when no special characters are involved.

## Performance

A pathological pattern can cause exponential backtracking on a hostile input. Tools wrap regex execution in a short timeout (typically a few hundred milliseconds for `guides_search`) and surface a timeout error rather than hanging. For very large inputs, prefer a substring or a fixed-length pattern over heavy alternation.
