# Regex syntax

Tools that accept regex patterns use **.NET `System.Text.RegularExpressions` syntax**. This applies to `file_grep` (with `useRegex: true`), `file_find_replace` (with `useRegex: true`), and any other tool that documents its `pattern` parameter as a regex.

## Key differences from other flavours

- **Named groups use `(?<name>...)`** — not the PCRE-style `(?P<name>...)`.
- **Variable-length lookbehinds are supported.** `(?<!foo)bar` works regardless of `foo` length.
- **`\w` and `\d` are Unicode-aware by default.** `\d` matches any Unicode decimal digit. Use `[0-9]` for ASCII-only.
- **`\K` (PCRE keep) is not available.** Use a lookbehind instead.
- **Case-insensitive matching** — use the inline `(?i)` flag, or the tool's case-insensitivity option.

## Common patterns

| Goal | Pattern |
|---|---|
| Literal substring | `TODO` |
| Anchored at start of line | `^import ` (in multi-line mode, e.g. `(?m)^import `) |
| Word boundary | `\bregex\b` |
| Alternation | `(grep|search)` |
| Optional segment | `colou?r` |

Plain words without metacharacters are valid regex, so a literal substring search and a regex search look the same when no special characters are involved.

## Performance

A pathological pattern can backtrack exponentially on a hostile input. Tools wrap regex execution in a short timeout and surface a timeout error rather than hanging. For very large inputs, prefer a substring or a fixed-length pattern over heavy alternation.
