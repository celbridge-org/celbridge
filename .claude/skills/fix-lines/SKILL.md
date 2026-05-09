---
name: fix-lines
description: Convert LF line endings to CRLF on modified source files in this Windows/CRLF repo. Use when git warns "LF will be replaced by CRLF", when the user asks to "fix line endings", "fix CRLF", or invokes /fix-lines.
---

# Fix line endings

Celbridge is a Windows project; CRLF is required (see CLAUDE.md). Files written through the Write tool sometimes land with LF endings if the writer didn't preserve them. This skill detects modified files with LF endings and converts them to CRLF in place. Edited and untouched files are left alone.

## Steps

1. **List modified files and their current line endings.** This includes both unstaged and staged changes; keep them separate so the user can see what scope is being touched.

   ```bash
   git diff --name-only | while read f; do
     if [ -n "$f" ] && [ -f "$f" ]; then
       if file "$f" | grep -q 'CRLF'; then echo "CRLF: $f"; else echo "LF:   $f"; fi
     fi
   done
   ```

   Repeat with `git diff --name-only --cached` if there are staged changes too.

2. **Collect the LF-only files** (skip CRLF and skip anything `file` reports as binary data — `file` does not say "CRLF" or "LF" for those).

3. **Convert in place** with PowerShell .NET APIs. Build the file list dynamically rather than hard-coding paths.

   ```powershell
   $files = @(
     'Source\Path\To\File1.cs',
     'Source\Path\To\File2.cs'
   )
   foreach ($file in $files) {
       $path = Join-Path 'C:\GitHub\celbridge' $file
       $text = [System.IO.File]::ReadAllText($path)
       $text = $text -replace "`r`n", "`n"
       $text = $text -replace "`n", "`r`n"
       $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
       [System.IO.File]::WriteAllText($path, $text, $utf8NoBom)
       Write-Output "fixed: $file"
   }
   ```

4. **Re-run the detection** from step 1 and confirm every modified text file now reports CRLF.

5. **Re-run any tests that touch the changed files** if the change set is non-trivial. Edit-tool round-trips can mask problems; a test pass after the conversion is the cheap verification.

## Rules

- UTF-8 without BOM. Match the encoding of the rest of the codebase.
- Preserve a trailing newline if one was present; do not append one if it wasn't.
- Skip binary files. `file <path>` reports them as `data`, `image`, `Zip archive`, etc. — none of those match the CRLF/LF detection regex above, so they fall through naturally, but double-check before processing if the diff includes anything unusual.
- Do not run `git add` afterwards. The user reviews diffs in GitHub Desktop before staging (per CLAUDE.md).
- Do not commit. Same reason.

## Why a skill rather than a hook

A PostToolUse hook on Write/Edit could enforce CRLF automatically, but skills are explicit and reviewable: the user sees the file list before the fix and can spot a wrongly-included file. Hooks would also rewrite files the user is actively editing, which can fight with the editor's own line-ending settings.
