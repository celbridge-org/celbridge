# package_list

Returns the packages currently published to the remote package registry. Use this to discover what is available before calling `package_install`, or to check whether a package you intend to publish would collide with an existing entry.

Only registry entries that look like packages (`.zip` extension, valid package name) are returned; any other files in the registry are filtered out.

## Returns

A JSON array of objects, one per package:

- `packageName` (string) — derived from the registry file name (without the `.zip` extension).
- `size` (long) — file size in bytes.
- `uploadedAt` (datetime) — UTC timestamp of when the entry was uploaded.

The array is in the order returned by the registry; it is not sorted alphabetically.

## See also

- `package_install` — download and extract a listed package.
- `package_publish` — upload a package to the registry.
- `packages_overview` — the registry workflow.
