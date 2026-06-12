# package_list

Returns the packages currently published to the connected workshop. Use this to discover what is available before calling `package_install`, or to check whether a package you intend to publish would collide with an existing entry.

## Returns

A JSON array of objects, one per package:

- `packageName` (string) — the package's unique name on the workshop.
- `latestVersion` (int or null) — the number of the latest live version, or null when the package has no live versions.
- `publishedAt` (datetime or null) — UTC timestamp of when the latest live version was published.
- `versionsCount` (int) — total number of versions the package has.

The array is in the order returned by the workshop; it is not sorted alphabetically.
