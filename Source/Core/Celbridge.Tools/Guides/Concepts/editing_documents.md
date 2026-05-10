# Editing documents

Celbridge offers several ways to change a file's content. Choose by the shape of the change.

## `file_apply_edits` — line/column edits

The default for targeted edits. Each edit specifies `line`, `endLine`, and `newText`, with optional `column` and `endColumn` for sub-line precision. 1-based; `endColumn: -1` means "end of line".

```python
file.apply_edits("scripts/build.py", [
    {"line": 12, "endLine": 12, "newText": "import logging\n"}])
```

Use this when you know the line number — typically after `file_grep` or `file_read`.

## `file_find_replace` — pattern-based edits

When the change is "replace every X with Y", `file_find_replace` is more appropriate than reading and computing line numbers. Supports literal and regex modes. For multi-file find/replace, run `file_grep` first to locate matches, then call `file_find_replace` per file.

## `file_delete_lines` — line-range deletion

A specialised case for removing whole lines. Equivalent to `file_apply_edits` with empty `newText`, but reads more clearly and avoids the residual-blank-line trap.

## `file_write` — wholesale replace

Overwrites the entire file. Use for new files or when the whole content needs to change. Do **not** use it for a small targeted edit — you clobber concurrent edits and have to reproduce the entire file.

## `file_write_binary` — binary content

Pass content as base64. Targeted edits are not supported for binary files.

## Editor behaviour

Every editing tool writes straight to disk. If the document is open, its buffer reloads and Monaco's undo history is wiped. See `file_changes` for the full save model. To open a document, use `document_open`; to bring an already-open one to the front, use `document_activate`.

## When to open the document first

You don't need to open a document to edit its file — `file_apply_edits` works on any file under the content root. Open the document when:

- You want the user to see the result.
- You intend to drive `webview_*` against an HTML viewer or contribution editor afterwards.
- The user is already looking at it — modifying the file under their feet still reloads the buffer, but at least they can see what is happening.
