# app_spotlight

Highlights a named UI landmark with a teaching-tip callout that points at the element, so you can orient a new user by showing them where something is rather than describing it in prose. The callout stays put until you clear it, replace it, set a duration, or the user interacts with the highlighted element.

Use it while explaining the interface: spotlight the panel you are talking about, keep talking, then move the spotlight or clear it. It only points; it never clicks or activates anything.

## Landmarks

`target` must be one of the registered landmarks. The built-in ones are listed below with what
they point at; `app_get_state` returns the authoritative live list of valid ids, including any
contributed by packages.

Shell panels:

- `explorer-panel` — the Explorer panel (left).
- `documents-panel` — the Documents area (centre).
- `console-panel` — the Console panel (bottom), where the user talks to you.
- `inspector-panel` — the Inspector panel (right).

Activity bar (the icon strip on the far left that switches the primary panel):

- `explorer-activity-button` — the Explorer icon in the activity bar.
- `search-activity-button` — the Search icon in the activity bar.

Explorer toolbar (its buttons are revealed automatically when spotlighted):

- `new-file-button` — the new-file button in the Explorer toolbar.
- `new-folder-button` — the new-folder button in the Explorer toolbar.
- `collapse-folders-button` — the collapse-all-folders button in the Explorer toolbar.
- `project-settings-button` — the project-settings button in the Explorer toolbar.

Search panel (all switch to the Search activity first; the replace ones also enable replace mode):

- `search-input` — the search text box.
- `search-run-button` — the run-search button (the magnifying glass).
- `search-history-button` — the recent-searches dropdown.
- `search-match-case-button` — the match-case toggle.
- `search-whole-word-button` — the match-whole-word toggle.
- `search-collapse-results-button` — the collapse-all-results button.
- `search-replace-toggle-button` — the toggle that shows the replace controls.
- `search-replace-input` — the replace text box.
- `search-replace-history-button` — the recent-replacements dropdown.
- `search-replace-all-button` — the replace-all button.

Console and documents:

- `console-input` — the console area where the user types to you.
- `console-maximize-button` — the console maximise/restore button.
- `document-tab-strip` — the open-document tab strip (only resolves with a document open).
- `split-editor-button` — the split-editor button on the document toolbar.

Title bar:

- `home-button` — the Home page button.
- `community-button` — the Community page button.
- `workspace-button` — the Workspace button (only resolves while a project is loaded).
- `panel-layout-button` — the layout-mode selector (Default, Focus, Presentation).
- `settings-button` — the app Settings button.
- `explorer-toggle-button` — the button that shows or hides the Explorer panel.
- `console-toggle-button` — the button that shows or hides the Console panel.
- `inspector-toggle-button` — the button that shows or hides the Inspector panel.

A `target` outside this list returns an error that lists the valid names; see `troubleshoot_spotlight_target`.

## Parameters

- `target` — the landmark to highlight. Pass an empty string to clear the current spotlight; there is no separate clear tool.
- `label` — the callout text. Keep it to a sentence or two; a teaching tip is not meant for paragraphs. Write it in the user's language, since the app does not translate it. Omit it to show the callout with no text.
- `durationMs` — auto-clear delay in milliseconds. Leave it at `0` (the default) to keep the spotlight up until you clear or replace it.

## Gotchas

- A landmark in a collapsed panel is revealed first. Spotlighting `inspector-panel` opens the Inspector if it is hidden.
- Only one spotlight is visible at a time; a new call moves the existing callout to the new target.
- A catalogued landmark that still cannot be shown (its panel is not instantiated) is not an error: the call succeeds, no callout appears, and a warning is logged. Open or reveal the relevant area first, then spotlight it.
- Call `app_get_state` to see which panels are visible and which one has focus before deciding what to spotlight.
