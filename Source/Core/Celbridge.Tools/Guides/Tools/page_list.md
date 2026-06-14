# page_list

Lists every page published to the connected workshop.

## Parameters

None.

## Returns

A JSON array of page entries, each with:

- `path` (string) — the served path declared in the page's `pages.toml` (e.g. `my-site/home`).
- `url` (string) — the full public URL the page is served at.
- `publishedAt` (string) — when the page was last published.
- `publishedBy` (string) — the publisher.
- `contentHash` (string) — fingerprint of the published bundle.

## Gotchas

- This lists pages, not packages. The two are separate subsystems; use `package_list` for packages.
- Pages cannot be downloaded back — there is no install. See `pages_overview` for the publish-only model.
