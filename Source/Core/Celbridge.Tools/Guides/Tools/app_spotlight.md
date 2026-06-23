# app_spotlight

Highlights a named UI landmark with a teaching-tip callout that points at the element, so you can orient a new user by showing them where something is rather than describing it in prose. The callout stays put until you clear it, replace it, set a duration, or the user interacts with the highlighted element.

Use it while explaining the interface: spotlight the panel you are talking about, keep talking, then move the spotlight or clear it. It only points; it never clicks or activates anything.

## Landmarks

`target` must be one of these catalogued landmarks.

Shell panels:

- `landmark.explorer` — the Explorer panel (left).
- `landmark.documents` — the Documents area (centre).
- `landmark.console` — the Console panel (bottom), where the user talks to you.
- `landmark.inspector` — the Inspector panel (right).

Affordances inside those panels:

- `landmark.add-file` — the new-file button in the Explorer toolbar.
- `landmark.add-folder` — the new-folder button in the Explorer toolbar.
- `landmark.project-settings` — the project-settings button in the Explorer toolbar.
- `landmark.activity-explorer` — the Explorer icon in the activity bar.
- `landmark.activity-search` — the Search icon in the activity bar.
- `landmark.search-input` — the search box (only resolves while Search is the active activity).
- `landmark.console-input` — the console area where the user types to you.
- `landmark.console-maximize` — the console maximise/restore button.
- `landmark.document-tabs` — the open-document tab strip (only resolves with a document open).
- `landmark.split-editor` — the split-editor button on the document toolbar.

Title bar:

- `landmark.settings` — the app Settings button.
- `landmark.toggle-explorer` — the button that shows or hides the Explorer panel.
- `landmark.toggle-console` — the button that shows or hides the Console panel.
- `landmark.toggle-inspector` — the button that shows or hides the Inspector panel.

A `target` outside this list returns an error that lists the valid names; see `troubleshoot_spotlight_target`.

## Parameters

- `target` — the landmark to highlight. Pass an empty string to clear the current spotlight; there is no separate clear tool.
- `label` — the callout text. Keep it to a sentence or two; a teaching tip is not meant for paragraphs. Write it in the user's language, since the app does not translate it. Omit it to show the callout with no text.
- `durationMs` — auto-clear delay in milliseconds. Leave it at `0` (the default) to keep the spotlight up until you clear or replace it.

## Gotchas

- A landmark in a collapsed panel is revealed first. Spotlighting `landmark.inspector` opens the Inspector if it is hidden.
- Only one spotlight is visible at a time; a new call moves the existing callout to the new target.
- A catalogued landmark that still cannot be shown (its panel is not instantiated) is not an error: the call succeeds, no callout appears, and a warning is logged. Open or reveal the relevant area first, then spotlight it.
- Call `app_get_state` to see which panels are visible and which one has focus before deciding what to spotlight.
