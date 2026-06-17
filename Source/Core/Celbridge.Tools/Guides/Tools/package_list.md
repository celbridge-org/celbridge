# package_list

Returns the packages currently published to the connected workshop. Use this to discover what is available before calling `package_install`, or to check whether a package you intend to publish would collide with an existing entry.

## Returns

A JSON array of objects, one per package:

- `packageName` (string) — the package's unique name on the workshop.
- `latestVersion` (int or null) — the highest version number the server reports for the package. **See caveat below**: this may name a deleted version when no live versions remain, pending a server-side fix. Treat a non-null value as advisory — confirm with `package_info` before trusting it as installable.
- `publishedAt` (datetime or null) — UTC timestamp of when the latest version was published.
- `versionsCount` (int) — total number of versions the package has (deleted versions included).

The array is in the order returned by the workshop; it is not sorted alphabetically.

## Caveat: `latestVersion` after delete or unpublish

Until the server delete-contract alignment lands (tracked in the migration follow-ups), `latestVersion` is **not** filtered to live versions:

- After `package_delete` removes the highest version, the server may still report it under `latestVersion` until the next publish.
- After `package_unpublish` removes every version, every entry's `latestVersion` is non-null but installing that version fails because its content has been deleted.

When you need certainty, call `package_info(packageName)` and select the highest `version` whose `deleted` is false. The version resolver inside `package_install` already does this for `latest`, so resolving `latest` continues to work correctly — only consumers reading `package_list` directly need the caveat.
