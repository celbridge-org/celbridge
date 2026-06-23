# app_spotlight

Highlights a named UI landmark with a teaching-tip callout that points at the element, so you can orient a new user by showing them where something is rather than describing it in prose. The callout stays put until you clear it, replace it, set a duration, or the user interacts with the highlighted element.

Use it while explaining the interface: spotlight the panel you are talking about, keep talking, then move the spotlight or clear it. It only points; it never clicks or activates anything.

## Landmarks

`target` must be one of these catalogued landmarks:

- `landmark.explorer` — the Explorer panel (left).
- `landmark.documents` — the Documents area (centre).
- `landmark.console` — the Console panel (bottom), where the user talks to you.
- `landmark.inspector` — the Inspector panel (right).

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
