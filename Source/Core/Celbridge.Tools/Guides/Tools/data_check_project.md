# data_check_project

Reports project-wide consistency findings without modifying anything:

- **Broken references** — `project:` references in text files that name a missing target.
- **Orphan .cel files** — `.cel` files with no parent file present on disk and no registered factory claiming the standalone form (e.g. `package.cel`, `*.note.cel`, `*.document.cel`).
- **Broken .cel files** — `.cel` files (including invalid `.cel.cel` filenames) that fail to parse. Applies to both parent-paired sidecars and standalone `.cel` forms.

Returns a JSON object with three arrays:

```json
{
  "brokenReferences": [{"source": "...", "missingTarget": "..."}],
  "orphanCelFiles": ["..."],
  "brokenCelFiles": ["..."]
}
```

Runs an on-demand parallel scan over the project's text files; no precomputed report waits in memory. The same check runs fire-and-forget on workspace load and publishes a summary message when findings are non-empty.

Pure read; the tool does not repair anything. Resolution is the caller's responsibility: rename or restore the missing target, delete or re-parent the orphan, repair or delete the broken `.cel` file.
