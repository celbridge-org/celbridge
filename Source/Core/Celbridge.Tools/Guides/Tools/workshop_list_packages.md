# workshop_list_packages

Returns the packages currently published to the connected workshop. Use it to discover what is available before calling `workshop_install_package`, or to check whether a package you intend to publish would collide with an existing entry. To list the packages the project loaded, use `app_list_packages` instead.

## Returns

A JSON array of objects, one per package, ordered by package name:

- `packageName` (string) — the package's unique name on the workshop.
- `latestWorkshopVersion` (int or null) — the highest live workshop version, the one `workshop_install_package` installs for `latest`. Deleted workshop versions are skipped, so it is null when the package has no live workshop version.
- `publishedAt` (datetime or null) — UTC timestamp of when that workshop version was published, or null when `latestWorkshopVersion` is null.
- `workshopVersionCount` (int) — total number of workshop versions the package has, deleted ones included.

## Gotchas

- A package whose workshop versions have all been deleted with `workshop_delete_package` stays listed, with a null `latestWorkshopVersion` and every deleted workshop version still counted. A package removed with `workshop_unpublish_package` is no longer listed.
