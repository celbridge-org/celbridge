# Search filters

A tool's file filter, like `file_grep`'s `include` and `exclude`, follows ripgrep's `--glob`. Each pattern is one `.gitignore` line, plus `{a,b}` braces. Patterns are relative to the root of the keys searched: the project, or a root like `temp:` or `logs:`. They stay relative to the root when a narrower `path` names a folder, the same as ripgrep resolves `--glob` from its working folder.

## Patterns

- A pattern with no `/` matches a name at any depth: `*.py` matches `app.py` and `src/lib/util.py`.
- A pattern with a `/` at the start or in the middle is relative to the root: `src/*.cs` matches `src/app.cs` only.
- `**` spans folders: `src/**/*.cs`, `**/tests`. A trailing `/` matches folders only.
- `*` matches within one name, `?` matches one character, and `[abc]`, `[a-z]` or `[[:digit:]]` match one character from a class.
- `\` escapes a special character, like `\*` or `\[`. A `\` before an ordinary character is refused, so write `/` between folder names.
- When a pattern matches a folder, it takes everything inside the folder: `tests/` includes every file under `tests`.
- Letter case is ignored.

## Several patterns

- Commas separate patterns, except inside braces: `*.py,*.md`. Write `\,` for a literal comma. Spaces around each pattern are dropped.
- Braces expand to each alternative: `*.{ts,tsx}` is `*.ts,*.tsx`. One pattern expands to at most 100 patterns.
- In `include`, a pattern that starts with `!` excludes: `*.js,!*.min.js`. A pattern in `exclude` cannot start with `!`.
- Excludes win, whatever the order. With no include patterns, every file is included.

| Filter | Matches |
|---|---|
| `include: "*.{ts,tsx}"` | TypeScript files at any depth |
| `include: "src/**"` | Everything under `src` at the root |
| `include: "tests/", exclude: "*.snap"` | Files under any `tests` folder, without snapshots |
| `include: "*.js,!*.min.js"` | JavaScript files, without minified ones |

## Differences from ripgrep

- An include that matches a folder takes the folder's files. In ripgrep, `-g 'tests/'` finds nothing, and `-g 'src/*'` finds only the files directly in `src`.
- The order of the patterns makes no difference. In ripgrep, a later glob takes precedence over an earlier one.
- Letter case is ignored. ripgrep's `--glob` matches exact case.
- `?` and a class match one character. ripgrep matches one byte of UTF-8, so in ripgrep `caf?.txt` misses `café.txt`.
- Named classes like `[[:alpha:]]` work. ripgrep has none, so in ripgrep `[[:alpha:]].txt` misses `a.txt`.

`file_grep`, `file_search`, `file_list_contents`, `file_get_tree`, `explorer_archive` and `data_inspect` take search filters. A refused pattern fails the call with a message that says what to write instead.
