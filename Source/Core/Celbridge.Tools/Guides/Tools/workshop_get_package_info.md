# workshop_get_package_info

Returns a workshop package's full metadata, every workshop version and every alias, in one call. Use it before installing to choose a workshop version or alias, or before curating aliases with `workshop_set_package_alias` and `workshop_remove_package_alias`. For the catalogue of packages rather than one package's detail, use `workshop_list_packages`.

## Parameters

### packageName

The name as published on the workshop (lowercase alphanumeric with single hyphen separators, 1-64 characters).

## Returns

A JSON object:

- `packageName` (string) — the package's name.
- `createdAt` (datetime) — when the package was first registered.
- `workshopVersions` (array) — one object per workshop version, each with:
  - `workshopVersion` (int) — the number the workshop assigned.
  - `author` (string) — who published it.
  - `date` (datetime) — when it was published.
  - `deleted` (bool) — true if the workshop version's content has been removed, so it cannot be installed. The server's wire field is still `tombstoned`, and the client maps it to `deleted` because Celbridge does not model a dead-but-retained state.
  - `contentHash` (string) — the uploaded content's hash, retained even when `deleted` is true so vendored copies stay verifiable.
  - `summary` (string) — the publisher's change summary, as written at publish time. It is retained when `deleted` is true, because the workshop does not erase it on delete.
- `aliases` (array) — one object per alias, each with `alias` (string) and `workshopVersion` (int). The workshop manages `latest`, and others such as `stable` are publisher-defined.

## Gotchas

- A `404` from the workshop (no such package) surfaces as an error. Check the name with `workshop_list_packages`. After a `workshop_unpublish_package`, the package is **not** 404. It stays listed with every workshop version flagged `deleted: true` and an empty `aliases` array, pending a server-side full-removal endpoint.
- Deleted workshop versions still appear in the list with `deleted: true`, so the numbering stays intact and `HISTORY.md` can render the gap. Filter on `!deleted` when choosing what to install. The `[package_deleted]` text `HISTORY.md` renders for a deleted workshop version's body is keyed off the `deleted` flag, not off an emptied `summary`.
- The flag is `deleted` (not `tombstoned`) on the client side. An agent that filters on `tombstoned` silently includes deleted workshop versions as live.
