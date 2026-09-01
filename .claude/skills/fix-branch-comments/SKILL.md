---
name: fix-branch-comments
description: Review and fix the code comments this branch added or rewrote, enforcing the Celbridge comment conventions (full stops not semicolons, no <remarks>, no history / doc cross-refs / restated calls, terse inline comments, contract-only interface xmldoc). Use only when the user asks to "fix comments", "clean up comments", "review branch comments", or invokes /fix-branch-comments — do not offer it proactively.
---

# Fix branch comments

Comments drift from the conventions in `docs/development/coding_conventions.md` as a branch is built. This skill makes one pass before the PR: it reviews the comments the branch wrote and rewrites or removes the ones that break the rules, keeping any genuinely useful information by moving it to the right place.

## Scope

**Only comments the branch added or rewrote.** Not whole files, not comments the branch left alone. A file the branch barely touched contributes only the lines it touched.

This is the difference between reading a few hundred comment lines and reading every line of every file the branch opened, and it also keeps the resulting diff inside the change under review.

## 1. Build the worklist

Main agent, shell only. The script is tested — run it, do not re-derive it inline:

```bash
bash .claude/skills/fix-branch-comments/extract-comments.sh > /tmp/comment-worklist.txt
wc -l < /tmp/comment-worklist.txt
```

Each line is `path:line<TAB>text`. A multi-line comment appears once, anchored on its opening line. If the worklist is empty, report that and stop.

## 2. Pre-compute the mechanical hits

These rules need no judgement, so decide them in the shell rather than spending a read on them:

```bash
WL=/tmp/comment-worklist.txt
echo "== prose semicolons =="
grep -nE '(///|//|#).*;[[:space:]]*$|(///|//|#).*[[:alnum:])"'"'"']; [[:alnum:]]' "$WL"
echo "== banned doc constructs =="
grep -nE '<remarks>|<c>|<list>|<item>|/// <param' "$WL"
echo "== section markers =="
grep -nE '// ?-- ' "$WL"
echo "== regions (checked against the diff, not the worklist) =="
git diff -U0 "$(git merge-base main HEAD)" -- '*.cs' | grep -E '^\+[[:space:]]*#(region|endregion)'
echo "== arrows and emoji =="
grep -nE '→|⇒|←|✓|✗|✔|✖' "$WL"
echo "== phrasing tells, judge each =="
grep -niE 'rather than|instead of|unlike |as opposed to|so that|so callers|which is why|used by|called by|populated during|set via|used to|previously|no longer|there is no|future maintainers|see [A-Z][a-zA-Z]+[.]|per the |Phase [0-9]|TDD|design doc' "$WL"
```

Everything above the tells is a violation to fix. The tells are a **priority list, not a filter** — those phrasings are the tells the conventions below name most often, but every line in the worklist still gets read.

## 3. Review in one subagent

The review opens files, so it does not belong in the main context. Launch **one** `general-purpose` subagent (split by file into two only if the worklist exceeds ~250 lines) and give it:

- the worklist contents,
- the shell output from step 2,
- the **Conventions to enforce** section below, verbatim.

Instruct it to:

- Work only on the worklist anchors. Comments outside the worklist are out of scope even when they look wrong, and even in the same file.
- Read each anchor with `Read` using `offset`/`limit` to see the full comment and the code beneath it. Do not read whole files.
- Fix the mechanical hits from step 2 first, then judge the rest.
- Edit in place. Never `git add`, never commit.
- Return **only** a terse summary, one line per file: `path — N edits: <short note per edit type>`, or `path — clean`. No file contents, no diffs.

## 4. Report

Relay the combined summaries grouped by file so the user can review the edits in GitHub Desktop. Do not re-read the edited files in the main agent.

## Conventions to enforce

Pass this whole section to the subagent. It applies to all in-repo prose a future reader meets without the surrounding conversation: `//`, `///`, `/** */`, XAML `<!-- -->`, Python docstrings, and any touched markdown.

### Mechanics
- **Full stops, never semicolons, in English prose.** C# statement terminators are unaffected. This is the most-corrected rule, and the most common slip is leaving a pre-existing semicolon on a line you reflowed for another reason.
- **No `<remarks>` blocks**, no inline doc tags (`<c>`, `<list>`, `<item>`), no `/// <param>` — except MCP tool methods in `Celbridge.Tools`, where the SDK source generator requires `<param>`.
- No emoji or special characters. ASCII `->` in prose is fine; `→ ⇒ ✓ ✗` are not.
- No `#region` / `#endregion`, no section markers (`// -- Initialization --`).

### XML doc scope
- **Foundation** interface members and public types always carry a concise `<summary>`, one or two sentences saying *what* it does.
- **Concrete-class members: skip xmldoc by default.** The interface already documents them. Keep one only for non-obvious behaviour: threading constraints, hidden side effects, subtle invariants.
- **Interface xmldoc is the caller contract only.** Delete populate paths ("set via X", "populated during Y"), cross-references to sibling APIs, perf rationale ("so callers can…"), named consumers, and anything restating what an enum or return type already says.
- A `<summary>` says *what*, not *why it was designed this way*.

### Class summaries
- A class summary says what the type **is**, at the altitude of the whole type.
- **Test: would this sentence have been written a month ago, before the current change?** A summary that details the aspect the branch happened to touch is recency bias, not documentation. That aspect belongs on the member that owns it, if anywhere.
- Do not describe how consumers bind to or drive the type. That drifts as consumers change.

### Keep out of comments entirely
- **History** — "used to", "previously tried", "removed for".
- **Cross-references** — to docs, proposals, or phases, named *or* vague ("per the design doc", "lands in Phase 3"), and to other classes or files by name.
- **Design rationale** — the "so that…" why belongs in the commit message. The tell is contrastive phrasing: "unlike", "rather than", "instead of", "not X".
- **Absences and warding** — "there is no X because…", "future maintainers should not…".
- **Consumer lists** — who reads a value, even when it is only two consumers. The exception is a cross-language sync obligation the reader cannot discover from the code, e.g. "Mirrored by the --cel-rail-width design token".
- **Inferable examples** — the parenthetical the reader can read straight off the code. Keep a list only when it is the closed type union a signature hides, not examples of a category.
- **Restate-the-call pointers** — e.g. `// The credential store is selected per platform (see Platform/)` above a self-named `PlatformServiceConfiguration.ConfigureServices(...)`.
- **Stale descriptions** of behaviour an earlier design had.

### Inline body comments
Terse. Only what a first-time reader cannot read off the code. Do not narrate the change, recap rationale visible in the surrounding code, or enumerate edge cases the reader can infer. A comment approaching paragraph length means the code should be restructured instead.

### Relocate, do not just delete, genuinely useful information
When an over-long comment contains a real fact — a threading constraint, a fragile reflected field name, why a workaround exists — move it to the single line it is about as a terse inline comment, then trim. Deleting useful information is as wrong as burying it.

### The test each surviving comment must pass
1. Would a reader who never saw the prior code understand it, and will it still be true a year from now? If no, delete.
2. Am I keeping this to prove to the PR reviewer that I thought hard? If yes, delete — that belongs in the commit message.

## Rules

- Do not `git add` and do not commit. The user reviews diffs in GitHub Desktop before staging.
- Touch only comments. If a comment is wrong because the code is wrong, surface that to the user rather than rewriting the code here.
- Do not invent rules. Enforce exactly the conventions above; when a comment is merely plain rather than wrong, leave it.
