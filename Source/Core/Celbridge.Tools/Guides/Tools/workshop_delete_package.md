# workshop_delete_package

Deletes a single published workshop version of a package. Its content (the ZIP bytes) is removed permanently and cannot be downloaded again. The workshop version's history entry and content hash are retained, so the number is never reused and a vendored copy stays verifiable, but the bytes are gone. This is a deletion, not the server's hidden tombstone state.

This is destructive administration and **always prompts for confirmation**. There is no `confirmWithUser` opt-out, unlike `workshop_install_package` and `workshop_publish_package`. The bar is deliberately firmer than `workshop_unpublish_page`, because deleted bytes are not recoverable through the workshop, whereas a page is re-publishable static content.

## Parameters

### packageName

The name as published on the workshop (lowercase alphanumeric with single hyphen separators, 1-64 characters).

### workshopVersion

The workshop version to delete, **required**. There is no default, because a destructive call must name its target. Accepts the same selectors as `workshop_install_package`: a workshop version number (`"3"`), an alias (`"stable"`), or `"latest"` (the highest live workshop version). The confirmation prompt names the resolved workshop version.

## Returns

A JSON object echoing `packageName`, the resolved `workshopVersion`, and `deleted: true`.

## Gotchas

- **No default target.** Calling without a workshop version is an error. Name the number or alias explicitly.
- **Aliases that point at the deleted workshop version are named in the confirmation.** Aliases are static pointers, so deleting a workshop version leaves any alias still pointing at it, and installing through that alias afterwards resolves to the deleted version and then fails at download. `latest` is the exception, because it resolves client-side to the highest live workshop version and so always skips deleted ones.
- **Deleting an already-deleted workshop version reports that state** rather than failing silently.
- **Durability is the consumer's responsibility.** The workshop does not promise eternal availability, so a consumer who needs a workshop version permanently should vendor it.
- To remove the entire package and every workshop version at once, use `workshop_unpublish_package`.
