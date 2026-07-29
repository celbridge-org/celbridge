---
name: fix-branch-comments
description: Review and fix code comments across every file modified on the current branch, enforcing the Celbridge comment conventions (full stops not semicolons, no <remarks>, no history / doc cross-refs / restated calls, terse inline comments, contract-only interface xmldoc). Use only when the user asks to "fix comments", "clean up comments", "review branch comments", or invokes /fix-branch-comments — do not offer it proactively.
---

# Fix branch comments

Comments drift from the conventions in CLAUDE.md as a branch is built, especially in files written or rewritten whole. Rather than policing every edit inline, this skill makes one pass over the branch: it reviews every comment in the files the branch touched and rewrites or removes the ones that break the rules, keeping any genuinely useful information by moving it to the right place.

Scope is **every comment in every file the branch modified**, not only the changed lines — a file the branch touches should leave this pass clean throughout. Files the branch did not modify are left alone.

## Run the review in subagents, not the main context

The review reads whole files, so doing it inline would pull the entire text of every touched file into the main conversation. Do not. The main agent only computes the file list and relays a terse summary; **all reading and editing happens in background subagents**, whose contexts are thrown away afterward.

1. **Compute the file list (main agent — this is the only cheap part that stays here).** Committed, uncommitted, and new, relative to the base branch (`main` unless the user names another):

   ```bash
   BASE=$(git merge-base main HEAD)
   { git diff --name-only "$BASE" HEAD; git diff --name-only HEAD; git ls-files --others --exclude-standard; } | sort -u
   ```

   Then **filter to hand-written text files**: keep `.cs`, `.py`, hand-authored `.js`/`.ts`, and any touched `.md`. Drop generated and vendored files, which never get hand-reviewed: anything under `obj/`, `bin/`, `node_modules/`, `/lib/`, `/min/`; `*.g.cs`, `*.designer.cs`; minified bundles (`*.min.js`, `*.map`); and lockfiles (`package-lock.json`). If the kept list is empty, report that and stop.

2. **Fan out the review to background subagents, batched.** Split the kept files into batches of ~8 and launch one `general-purpose` subagent per batch (a single batch is fine for a small change set). Give each subagent: its file list, the **Conventions to enforce** section below verbatim, and the instruction to read every comment in each assigned file, apply the conventions, edit in place, and **return only a terse summary** — one line per file: `path — N edits: <short note per edit type>`, or `path — clean`. The subagent must not read files outside its list, must not `git add`/commit, and must not return file contents or full before/after diffs.

3. **Relay the combined terse summaries** to the user, grouped by file, so they can review the actual edits in GitHub Desktop. Do not re-read the edited files in the main agent.

## Conventions to enforce

Pass this whole section to each subagent. It applies to all in-repo prose a future reader meets without the surrounding conversation: `//`, `///`, `/** */`, Python docstrings, and any touched markdown or design docs.

### Mechanics
- **Full stops, never semicolons, in English prose.** (C# statement terminators are unaffected.) This is the most-corrected rule — scan every `//`, `///`, and docstring line, including a `;` at a line-end.
- **No `<remarks>` blocks**, no inline doc tags (`<c>`, `<list>`, `<item>`), no `/// <param>` — except MCP tool methods in `Celbridge.Tools`, where the SDK source generator requires `<param>`.
- No emojis, arrows, or other special characters (`->` as ASCII in prose is fine; `→ ⇒ ✓ ✗` and emoji are not).
- No `#region` / `#endregion`, no section-marker comments (`// -- Initialization --`).

The first three are greppable; a subagent should scan its batch with these before the read-through:

```bash
# Prose semicolons in C#/JS line comments and doc comments (judge each; prose only)
grep -rnE '^\s*(///|//).*;' <files>
# Banned XML doc constructs
grep -rnE '<remarks>|<c>|<list>|<item>|/// <param' <files>
# Region and section markers
grep -rnE '#(region|endregion)|^\s*// ?-- ' <files>
```

### XML doc scope
- **Foundation** interface members and public types always carry a concise `<summary>` (one or two sentences, *what* it does).
- **Concrete-class members: skip xmldoc by default** — the interface already documents them. Keep a member comment only for non-obvious behaviour (threading constraints, hidden side effects, subtle invariants).
- **Interface xmldoc is the caller contract only.** Delete populate-path ("set via X", "populated during Y"), cross-refs to sibling APIs, perf rationale ("so callers can…"), naming of consumers, and anything that just restates what an enum or return type already says.
- A `<summary>` says *what*, not *why it was designed this way*. Two short sentences at most; do not pad to restate the member name.

### Keep out of comments entirely
- **History** — "used to", "previously tried", "removed for".
- **Cross-references** — to docs, proposals, or phases (named *or* vague: "per the design doc", "lands in Phase 3"), and to other classes or files by name.
- **Design-doc rationale** — the "so that…" why belongs in the commit message.
- **Absences and warding** — "there is no X because…", "future maintainers should not…".
- **Inferable examples** — the parenthetical list the reader can read straight off the code.
- **Restate-the-call pointers** — e.g. `// The credential store is selected per platform (see Platform/)` above a self-named `PlatformServiceConfiguration.ConfigureServices(...)` call. Delete it; the call and folder are self-documenting.
- **Stale descriptions** of behaviour an earlier design had — when a refactor changed the behaviour, the comment must change with it.

### Inline body comments
- Terse. Only what a first-time reader cannot read off the code. Do not narrate the change, recap rationale visible in the surrounding code, or enumerate edge cases the reader can infer. A comment approaching paragraph length means the code should be restructured instead.

### Relocate, do not just delete, genuinely useful information
When a `<remarks>` wall or an over-long summary contains a real fact (a threading constraint, a fragile reflected field name, why a workaround exists), move it to the single line it is about as a terse inline comment, then trim the summary. Deleting useful information is as wrong as burying it.

### The test each surviving comment must pass
1. Would a reader who never saw the prior code understand it, and will it still be true a year from now? If no, delete.
2. Am I keeping this to prove to the PR reviewer that I thought hard? If yes, delete — that belongs in the commit message.

## Rules

- Do not `git add` and do not commit. The user reviews diffs in GitHub Desktop before staging (per CLAUDE.md).
- Touch only comments. This pass does not change code behaviour; if a comment is wrong because the code is wrong, surface that to the user rather than rewriting the code here.
- Do not invent rules. Enforce exactly the conventions above (sourced from CLAUDE.md and the comment-style memory); when a comment is merely plain rather than wrong, leave it.
- Stay inside files the branch modified. Do not sweep the whole repo.
