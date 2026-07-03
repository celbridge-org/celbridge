# Troubleshoot: spotlight target not found

`app_spotlight` rejected the `target` because it is not a catalogued landmark. The spotlight vocabulary is a fixed allow-list; only those identifiers can be highlighted.

## Recovering

- Use one of the catalogued landmarks. The error message lists every valid identifier, so pick the one that matches what you want to point at.
- Landmark identifiers use the `landmark.` prefix and are culture-invariant (for example `explorer-panel`, `console-panel`). They are not the panel's display name.
- To clear the current spotlight, pass an empty string as the target rather than a name.

If the landmark you need is not in the list, it is not part of the current vocabulary. Explain that part of the interface in prose instead.
