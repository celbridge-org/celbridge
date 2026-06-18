# package_info

Returns a workshop package's full metadata — every version and every alias — in one call. Use it before installing to choose a version or alias, or before curating aliases with `package_set_alias` / `package_remove_alias`. For the catalogue of packages rather than one package's detail, use `package_list`.

## Parameters

### packageName

The name as published on the workshop (lowercase alphanumeric with single hyphen separators, 1-64 characters).

## Returns

A JSON object:

- `packageName` (string) — the package's name.
- `createdAt` (datetime) — when the package was first registered.
- `versions` (array) — one object per version, each with:
  - `version` (int) — the version number.
  - `author` (string) — who published it.
  - `date` (datetime) — when it was published.
  - `deleted` (bool) — true if the version's content has been removed; it cannot be installed. (The server's wire field is still `tombstoned`; the client maps it to `deleted` because Celbridge does not model a dead-but-retained state.)
  - `contentHash` (string) — the uploaded content's hash; retained even when `deleted` is true so vendored copies stay verifiable.
  - `summary` (string) — the publisher's change summary, as written at publish time. Retained when `deleted` is true (the workshop does not erase it on delete).
- `aliases` (array) — one object per alias, each with `alias` (string) and `version` (int). `latest` is managed by the workshop; others such as `stable` are publisher-defined.

## Gotchas

- A `404` from the workshop (no such package) surfaces as an error; check the name with `package_list`. After a `package_unpublish`, the package is **not** 404 — it stays listed with every version flagged `deleted: true` and an empty `aliases[]`, pending the server-side full-removal endpoint tracked in the migration follow-up.
- Deleted versions still appear in the list with `deleted: true` so the history numbering stays intact and `HISTORY.md` can render the gap. Filter on `!deleted` when choosing what to install. The `[package_deleted]` text the `HISTORY.md` renderer substitutes for a deleted version's body is keyed off the `deleted` flag, not off an emptied `summary`.
- The version flag is `deleted` (not `tombstoned`) on the client side — an agent that filters on `tombstoned` will silently include deleted versions as live.
