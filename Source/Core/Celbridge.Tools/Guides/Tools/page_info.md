# page_info

Inspects one published page by its served path.

## Parameters

### path

The page's served path as it appears on the workshop (e.g. `my-site/home`). This is the `[publish].path` value from the page's `pages.toml`, not a local folder name. Multi-segment paths are written with `/` separators.

## Returns

A JSON object with `path`, `url` (the full public URL the page is served at), `publishedAt`, `publishedBy`, and `contentHash`. Fails with a clear message when no page is published at the given path.

## Gotchas

- The path is the served path from the manifest, not the local folder you published from.
- There is no way to download the page bundle back; this returns metadata only. See `pages_overview`.
