# page_publish

Zips a folder of static web content and publishes it to the connected workshop as a page. The served path is read from the folder's `pages.toml`, so the local folder name does not matter. The page is then served publicly at that path.

By default a confirmation dialog is shown before publishing; pass `confirmWithUser: false` only when the user has explicitly asked for unattended operation.

## Parameters

### resource

Resource key of the page folder, or of its `pages.toml` manifest. Defaults to `pages/` in the project root when omitted. The folder is what gets zipped and uploaded. Because this is a resource key, any readable root works — including assembling a page under `temp:` and publishing it from there.

### confirmWithUser

When `true` (default), shows a confirmation dialog naming the served path before uploading. Leave at the default unless the user has asked for an unattended run.

## Validation

Before uploading, the tool verifies that:

- A project is loaded.
- An **Author** is set in Workshop settings (it is recorded as the page's publisher).
- `resource` resolves to a folder containing a `pages.toml` (or to that manifest).
- The manifest is valid TOML with a `[publish]` section whose `path` is a non-empty string.
- The folder contains at least one file.

If any check fails, no upload is attempted. If a `page.toml` (singular) is present but no `pages.toml`, the error names the near-miss so you can rename it.

## Returns

A JSON object:

- `path` (string) — the served path the workshop assigned (read back from the server, authoritative).
- `url` (string) — the full public URL the page is served at.
- `entries` (int) — number of files included in the uploaded zip.
- `size` (long) — uploaded zip size in bytes.

## Gotchas

- **The served site excludes `pages.toml`, but the bundle includes it.** The whole folder is zipped (the server reads the manifest from the bundle); everything except `pages.toml` is published verbatim.
- **A path overlap fails.** If a page is already published at the manifest's path, the workshop rejects the publish; unpublish the existing page first, or change `[publish].path`.
- **Pages are publish-only and not recoverable.** The workshop keeps no copy of the bundle you can pull back, so keep the source folder. For a versioned, pullable site, wrap the content in a package instead. See `pages_overview`.
- Symlinks and other reparse points inside the folder are skipped, not followed.
