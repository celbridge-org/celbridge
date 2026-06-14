# page_unpublish

Removes a page's served content from the workshop, identified by its served path.

By default a confirmation dialog is shown; pass `confirmWithUser: false` only when the user has explicitly asked for unattended operation. This is held to a more lenient bar than the package admin tools (`package_delete`, `package_unpublish`, which always prompt) because a page is re-publishable static content, not irreversible version history.

## Parameters

### path

The page's served path as it appears on the workshop (e.g. `my-site/home`) — the `[publish].path` from its `pages.toml`. Multi-segment paths use `/` separators.

### confirmWithUser

When `true` (default), shows a confirmation dialog naming the path before removing the page. Leave at the default unless the user has asked for an unattended run.

## Returns

A JSON object echoing `path` and `unpublished: true`.

## Gotchas

- **The workshop keeps no recoverable copy.** Re-publishing the page after unpublishing requires the original source folder. See `pages_overview`.
- Unpublishing a page does not touch any package; pages and packages are separate subsystems.
- Fails with a clear message when no page is published at the given path.
