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
  - `tombstoned` (bool) — true if the version's content has been removed; it cannot be installed.
  - `contentHash` (string) — the uploaded content's hash.
  - `summary` (string) — the publisher's change summary.
- `aliases` (array) — one object per alias, each with `alias` (string) and `version` (int). `latest` is managed by the workshop; others such as `stable` are publisher-defined.

## Gotchas

- A `404` from the workshop (no such package) surfaces as an error; check the name with `package_list`.
- Tombstoned versions still appear in the list with `tombstoned: true` so history reads correctly; filter them out when choosing what to install.
