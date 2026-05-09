# Editing documents

Celbridge offers several ways to change a file's content. Choose based on the shape of the change.

## `file_apply_edits` — line/column edits

The default tool for targeted edits. Each edit specifies `line`, `endLine`, and `newText`, with optional `column` and `endColumn` for sub-line precision. The line and column numbers are 1-based; `endColumn: -1` means "end of line".

```python
file.apply_edits("scripts/build.py", [
    {"line": 12, "endLine": 12, "newText": "import logging\n"},
])
```

Use this when you know the line number — typically after a `file_grep` or `file_read`. The tool is the most precise option and survives concurrent reads cleanly.

## `file_find_replace` — pattern-based edits

When the change is "replace every X with Y", `file_find_replace` is more appropriate than reading and computing line numbers. Supports literal and regex modes.

```python
file.find_replace("scripts/build.py", find="DEBUG=False", replace="DEBUG=True")
```

For multi-file find/replace, run `file_grep` first to locate matches, then call `file_find_replace` per file.

## `file_delete_lines` — line-range deletion

A specialised case for removing whole lines without computing replacement strings. Equivalent to a `file_apply_edits` with empty `newText`, but reads more clearly.

## `file_write` — wholesale replace

Overwrites the entire file with new content. Use it when creating a new file or when the file's full content needs to change. **Don't** use `file_write` for a small targeted edit — you'll clobber concurrent edits and you have to reproduce the entire file content, which is brittle.

## `file_write_binary` — binary content

Use for non-text files. Pass content as a base64 string. Targeted edits aren't supported for binary files.

## Editor behaviour

Every editing tool writes straight to disk. If the document is open in the editor, its buffer reloads from disk and Monaco's undo history is wiped — see `file_changes` for the full save model. To open a document for editing, use `document_open`; to bring an already-open document to the front, use `document_activate`.

## When to open the document first

You don't need to open a document to edit its file. `file_apply_edits` works on any file under the content root. Open the document when:

- You want the user to see the result.
- You want to use `webview_*` devtools against an HTML viewer or contribution editor afterwards.
- The user is already looking at it — modifying the file under their feet still reloads the buffer, but at least they can see what's happening.
