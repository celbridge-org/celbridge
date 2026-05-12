# Editing documents

Celbridge offers several ways to change a file's content. Choose by the shape of the change.

## `file_edit` — surgical text-match replacement

The default for a single surgical edit. Quote the exact text to change in `oldString`, supply the replacement in `newString`, the tool finds the unique occurrence and substitutes it. Fails closed when the snippet is missing or non-unique, so a stale read surfaces immediately rather than silently editing the wrong region.

```python
file.edit("scripts/build.py", old_string="import os", new_string="import os\nimport logging")
```

## `file_multi_edit` — atomic batch of text-match edits

When several distinct surgical edits should land together — either all succeed or none does — use `file_multi_edit`. Each entry has the same shape as `file_edit`. Edits apply sequentially in array order, so a later edit can anchor against text an earlier edit produced.

## `file_replace` — pattern-based edits

When the change is "replace every X with Y", or when the pattern needs regex (capture groups, alternation, character classes), `file_replace` is more appropriate than a text-match edit. Supports literal and regex modes and an optional line-range scope. For multi-file find/replace, run `file_grep` first to locate matches, then call `file_replace` per file.

## `file_write` — wholesale replace

Overwrites the entire file. Use for new files or when the whole content needs to change. Do **not** use it for a small targeted edit — you clobber concurrent edits and have to reproduce the entire file.

## `file_write_binary` — binary content

Pass content as base64. Targeted edits are not supported for binary files.

## Editor behaviour

Every editing tool writes straight to disk. If the document is open, its buffer reloads and Monaco's undo history is wiped. See `file_changes` for the full save model. To open a document, use `document_open`; to bring an already-open one to the front, use `document_activate`.

## When to open the document first

You don't need to open a document to edit its file — `file_edit` works on any file under the content root. Open the document when:

- You want the user to see the result.
- You intend to drive `webview_*` against an HTML viewer or contribution editor afterwards.
- The user is already looking at it — modifying the file under their feet still reloads the buffer, but at least they can see what is happening.
