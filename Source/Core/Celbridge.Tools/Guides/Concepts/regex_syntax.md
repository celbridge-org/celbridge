# Regex syntax

`file_grep` and `file_replace` read a pattern the way ripgrep does, in the syntax of the Rust `regex` crate. Each pattern matches what `rg --crlf` would match. A pattern outside the dialect fails the call with a message that says what to write instead. `file_grep` reads a regex by default, and `literal: true` makes the pattern plain text. `file_replace` reads plain text by default, and `literal: false` makes the pattern a regex.

## How a pattern matches

- A pattern matches within one line. A line ends at `\n`, `\r\n` or a lone `\r`. In `file_replace`, `multiline` lets a pattern match across lines. `.` still stops at a line break unless the pattern sets `(?s)`.
- `^` and `$` match at the start and end of each line, with or without `multiline`.
- Matching takes time linear in the length of the text, so no pattern can hang a search.
- Letter case is matched exactly by default. `matchCase: false` or `(?i)` ignores it, with Unicode's case folding, so `(?i)s` also matches `ſ`.
- `.`, `\w` and every class match a whole character, even one above U+FFFF like an emoji.
- Matches never overlap. An empty match right after a match is skipped, the same as in ripgrep.

## The same as in ripgrep

- Groups: `(...)`, `(?:...)`, `(?<name>...)` and `(?P<name>...)`. Groups are numbered by their opening parenthesis.
- Classes: `\d`, `\s` and `\w` are Unicode classes. `\w` includes `_`, letters, marks and digits of every script. `\pL`, `\p{Lu}` and `\PL` name a general category. `[[:alpha:]]` is an ASCII class.
- Class operations: `[a-z&&[^aeiou]]` intersects, `[a-z--aeiou]` subtracts and `[a-c~~b-d]` keeps the characters in one side only.
- Escapes: `\x41`, `\x{1F600}`, `\u0041`, `\u{41}`, `\t` and `\n`. A `\` before any ASCII punctuation is literal.
- Flags: `(?i)`, `(?s)`, `(?U)` to swap greedy and lazy, and `(?x)` to ignore whitespace. `(?i:...)` sets a flag for a group.
- Repetition: `*`, `+`, `?`, `{2}`, `{2,}` and `{2,5}`, each with `?` for lazy. A `{` that starts no repetition is refused, so write `\{` for a brace.

## Refused

- Lookaround, like `(?=...)` and `(?<!...)`, and backreferences, like `\1`. ripgrep refuses them too, since they rule out linear-time matching.
- `\A` and `\z`, which match only at the start and end of the whole file in ripgrep. Use `^` and `$` for a line.
- `\<`, `\>` and `\b{start}`. Use `\b`.
- `\p{...}` names other than general categories, `Any`, `ASCII` and `Assigned`. Scripts like `\p{Greek}` and properties like `\p{Alphabetic}` are refused.
- `(?-m)`, `(?-u)` and `(?-R)`, and `#` comments with `(?x)`.
- A line break in a pattern without `multiline`. With `multiline`, a `\r`: write `\n` for any line break.
- `wholeWord` on a regex that can match empty text, or match text that starts or ends with a non-word character, like `a*` or `-foo`. Use `\b` where the pattern needs it.
- A repetition too large to match in linear time, like `a{100000}`.
- A pattern nested 250 or more levels deep, like 250 groups inside one another. ripgrep refuses these too. Each group, class, repetition, sequence and alternation counts as a level.

## Replacements

`file_replace` follows ripgrep's `--replace`. `$1` and `${1}` write a group by number, `$name` and `${name}` write a group by name, and `$0` writes the whole match. `$$` writes a `$`, and any other `$` is literal text. A name runs on through letters, digits and `_`, so `$1a` names a group called `1a`. A reference to a missing group is refused, so write `${1}a` for group 1 followed by `a`. A group that took no part in the match writes empty text. With `literal: true`, the replacement is plain text.

## Differences from ripgrep

- `\b`, `\B` and `wholeWord` on a regex read some of ripgrep's word characters as non-word characters. These are spacing marks like Devanagari's vowel signs, letter numbers like `Ⅻ`, circled letters like `Ⓐ`, enclosing marks, and every character above U+FFFF. So `\b` can find a boundary inside a Devanagari word.
- With `multiline`, each line break reads as one `\n`, even in a file that ends its lines with `\r\n`. So `a\nb` matches `a\r\nb`, and `a[^x]b` matches it too.
- An empty match falls between characters. ripgrep can report one between the bytes of a character above U+007F.
- With `literal: true`, a replacement is plain text. ripgrep expands `$` even with `-F`.
