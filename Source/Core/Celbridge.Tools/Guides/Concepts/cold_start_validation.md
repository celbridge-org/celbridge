# Cold-Start Validation

This guide carries the prompt set and evaluation rubric used to gate the tool-surface redesign. A developer triggers a validation run by asking the agent to "run cold start validation"; the agent fetches this guide on demand and works through it. The guide is intentionally not loaded by default — it must not bias cold-start behaviour the rest of the time.

## How to run

1. Read this guide with `guides_read(['cold_start_validation'])`.
2. For each prompt below, work the task as if it were a fresh user request. Do not consult any other guide unless the task itself requires it.
3. After each task, fill in the rubric scoresheet at the bottom of this guide.
4. Report the aggregate result back to the developer.

## Prompts

### P1 — File overview

> *"What's in this project? Give me a tree and a one-line description of each top-level folder."*

Expected first calls: `app_get_state`, then `file_get_tree("")`. Should not call any guides_*.

### P2 — Targeted edit

> *"Add a `# TODO: review` line above the `main` function in `scripts/build.py`."*

Expected: `file_grep` (or `file_read`) to locate the function, then `file_apply_edits`. Should not write a new file. Should not call `file_write` (that overwrites). Should not navigate the guide library before acting.

### P3 — Spreadsheet read

> *"What columns does `data/sales.xlsx` have, and how many rows in Q1?"*

Expected: `spreadsheet_get_info`, then `spreadsheet_read_sheet` with `headers: true` (or no headers if the agent reads row 1 separately). The agent may consult `spreadsheet_a1_notation` or `spreadsheet_headers_mode` if uncertain — that's acceptable.

### P4 — Ambiguous file reference

> *"Can you fix the typo on line 12?"*

Expected: the agent resolves "the file" against the active document via `document_get_state` rather than searching the whole project. Failure mode: calling `file_grep` for "typo" or asking the user which file before checking the active document.

### P5 — Regex search

> *"Find every TODO comment in the project."*

Expected: `file_grep` with a regex that matches `TODO`. Should not iterate `file_read` over every file.

### P6 — Edit-reload-inspect (WebView)

> *"The button in `widgets/counter/index.html` doesn't increment. Have a look."*

Expected: `document_open`, edit/inspect with `webview_*` tools. Agents should not attempt to run the page outside the editor.

### P7 — Resource key form

> *"Create a folder called `archive` next to `scripts`."*

Expected: `explorer_create_folder("archive")` (top-level) — confirm what the user means if `scripts` is nested. The key correctness signal here is that the agent passes a forward-slash relative path, not an absolute path or a backslash form.

### P8 — Feature flag awareness

> *"Run `console.log('hi')` in the WebView devtools."*

Expected: check `featureFlags['webview-dev-tools-eval']` from `app_get_state` before calling `webview_eval`, and explain the flag if it is off.

## Rubric

For each prompt, score on a 0/1/2 scale:

| Score | Meaning |
|---|---|
| 0 | Wrong tool selected, or correct tool called with wrong shape |
| 1 | Correct tool, minor parameter friction (one retry) |
| 2 | Correct tool, correct parameters, no retry |

A prompt that requires the agent to fetch a doc before succeeding scores 1, not 2 — successful selection from the trimmed surface alone is the headline goal.

## Scoresheet

| Prompt | Score | Notes |
|---|---|---|
| P1 — File overview | | |
| P2 — Targeted edit | | |
| P3 — Spreadsheet read | | |
| P4 — Ambiguous file reference | | |
| P5 — Regex search | | |
| P6 — Edit-reload-inspect (WebView) | | |
| P7 — Resource key form | | |
| P8 — Feature flag awareness | | |
| **Aggregate** | **/16** | |

Report the aggregate, the per-prompt notes, and any tool-selection or parameter mistakes the agent observed. Anything below 12/16 means the trim went too far somewhere; anything below 8/16 is a regression that has to be addressed before merge.
