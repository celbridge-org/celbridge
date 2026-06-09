# data_check_references

Enumerates every dangling `"project:<key>"` reference in the workspace — text files in the scanner allowlist that quote a resource key whose target no longer exists. Runs an on-demand parallel scan; no precomputed index.

## Parameters

None.

## Returns

```json
{
  "references": [
    { "source": "project:docs/notes.py", "missingTarget": "project:assets/missing.png" },
    { "source": "project:docs/notes.py", "missingTarget": "project:assets/old.toml" },
    { "source": "project:scripts/main.py", "missingTarget": "project:assets/missing.png" }
  ]
}
```

Sorted by `(missingTarget, source)` ordinal so two runs over the same state produce identical output. The `references` array is empty when the project has no dangling references.

Each entry pairs the file that *contains* the reference literal (`source`) with the resource key the literal points at (`missingTarget`). The same target can appear under multiple sources — one entry per source.

## When to use

- "Did my cascade leave anything dangling?" — run after a `break_references` delete or a series of edits to verify the project's reference graph is consistent.
- "Pre-commit check." — fail the agent's task gracefully if a recent operation introduced broken references.
- "Where is `project:assets/foo.png` still mentioned?" — filter the response caller-side by `missingTarget`.

## Scope

- **Always-quoted contract.** Only `"project:..."`, `'project:...'`, and `\"project:...\"` (the JSON-escaped form) are tracked. Bare prose mentions and bracketed forms like `[project:...]` are deliberately not detected.
- **Scanner allowlist.** Only files whose extension is on the cascade scanner's allowlist participate — `.cel`, scripts (`.js`, `.py`, `.ipy`, `.ipynb`), tabular (`.csv`, `.tsv`), and structured data / config (`.json`, `.jsonl`, `.ndjson`, `.yaml`, `.yml`, `.toml`, `.xml`). Any other extension — `.md`, `.txt`, source code outside the listed languages, HTML — is invisible. A reference inside a `.md` file is descriptive prose, not a tracked reference. See the `resource_keys` guide's "Where the scanner looks" section for the full table.

## Related tools

- **Sidecar health (orphan / broken / invalid `.cel` files)** lives on `data_inspect`, not here. The two cover orthogonal categories of project consistency.
- **Workspace-load reporting.** A project consistency check also runs at workspace load and writes findings to the host log. `data_check_references` lets an agent query the same state on demand without waiting for a reload.

## Notes

- The scan walks the project's allowlisted files in parallel, so cost scales with file count, not number of references.
- Restoring the missing target (e.g. moving the file back, undoing a delete) clears the dangling reference on the next call — references resolve as soon as the target exists, no manual re-scan or rewrite needed.
