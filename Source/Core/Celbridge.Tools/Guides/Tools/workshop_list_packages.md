# workshop_list_packages

Returns the packages currently published to the connected workshop. Use it to discover what is available before calling `workshop_install_package`, or to check whether a package you intend to publish would collide with an existing entry. To list the packages the project loaded, use `app_list_packages` instead.

## Returns

A JSON array of objects, one per package:

- `packageName` (string) — the package's unique name on the workshop.
- `latestWorkshopVersion` (int or null) — the highest workshop version the server reports for the package. **See the caveat below**: this may name a deleted workshop version when no live one remains, pending a server-side fix. Treat a non-null value as advisory, and confirm with `workshop_get_package_info` before trusting it as installable.
- `publishedAt` (datetime or null) — UTC timestamp of when the latest workshop version was published.
- `workshopVersionCount` (int) — total number of workshop versions the package has, deleted ones included.

The array is in the order the workshop returns, not sorted alphabetically.

## Caveat: `latestWorkshopVersion` after delete or unpublish

Until the server's delete contract is aligned, `latestWorkshopVersion` is **not** filtered to live workshop versions:

- After `workshop_delete_package` removes the highest workshop version, the server may still report it under `latestWorkshopVersion` until the next publish.
- After `workshop_unpublish_package` removes every workshop version, every entry's `latestWorkshopVersion` is non-null, but installing that version fails because its content has been deleted.

When you need certainty, call `workshop_get_package_info(packageName)` and select the highest `workshopVersion` whose `deleted` is false. The resolver inside `workshop_install_package` already does this for `latest`, so resolving `latest` keeps working, and only a caller reading `workshop_list_packages` directly needs the caveat.
