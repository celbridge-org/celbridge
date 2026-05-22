# metadata_check_project

Reports project-wide consistency findings: dangling `project:` references and any sidecar in an attention state. Pure read — surfaces issues, does not repair them.

## Arguments

None.

## Returns

A JSON object with three lists. Empty lists mean the corresponding invariant holds.

```json
{
  "brokenReferences": [
    { "source": "docs/index.md", "missingTarget": "drafts/gone.md" }
  ],
  "orphanSidecars": [
    "archive/old.png.cel"
  ],
  "brokenSidecars": [
    "weird.cel.cel",
    "docs/notes.md.cel"
  ]
}
```

## Categories

- **`brokenReferences`** — every quoted `"project:<key>"` literal that does not resolve to an existing resource. `source` is the file containing the literal; `missingTarget` is the resource key the literal points to. A single source file can contribute multiple entries (one per missing target). A single missing target can be referenced by multiple sources (one entry per (source, target) pair).
- **`orphanSidecars`** — every `.cel` file the registry tracked but for which no parent file exists. Typical fixes: delete the orphan, rename a parent to claim it, or create the missing parent file. Some orphans are legitimate (a content type that uses standalone `.cel` files); the agent should not auto-delete.
- **`brokenSidecars`** — every `.cel` file whose frontmatter does not parse. Covers merge-conflict markers, malformed TOML, missing fences, and the `.cel.cel` invalid-suffix case. The bytes are left on disk for the user to inspect and repair — typically via `file_read` + `file_write` of the affected sidecar, or by opening the file in another editor.

## Notes

- The check runs in memory after the metadata service's initial rebuild completes. Latency is sub-second on a typical project.
- The MCP tool returns the same data the workspace surfaces at load time. Re-running after a fix is the right way to confirm the fix landed.
- Case sensitivity: a literal `"project:Foo"` does not match a resource whose canonical key is `"project:foo"` — case-mismatched references appear as `brokenReferences`. This is intentional and matches the rest of the resource system.
- Cross-root references (`temp:`, `logs:`) are not tracked; only `project:` references carry referential integrity.
