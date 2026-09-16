# workshop_unpublish_page

Removes a page's served content from the workshop, identified by its served path.

By default a confirmation dialog is shown. Pass `confirmWithUser: false` only when the user has explicitly asked for unattended operation. This is a more lenient bar than the package administration tools, `workshop_delete_package` and `workshop_unpublish_package`, which always prompt, because a page is re-publishable static content rather than irreversible version history.

## Parameters

### path

The page's served path as it appears on the workshop (e.g. `my-site/home`), the `[publish].path` from its `pages.toml`. Multi-segment paths use `/` separators.

### confirmWithUser

When `true` (default), shows a confirmation dialog naming the path before removing the page. Leave at the default unless the user has asked for an unattended run.

## Returns

A JSON object echoing `path` and `unpublished: true`.

## Gotchas

- **The workshop keeps no recoverable copy.** Re-publishing the page after unpublishing requires the original source folder. See `pages_overview`.
- Unpublishing a page does not touch any package, because pages and packages are separate.
- Fails with a clear message when no page is published at the given path.
