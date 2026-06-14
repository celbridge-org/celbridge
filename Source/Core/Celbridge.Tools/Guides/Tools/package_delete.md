# package_delete

Deletes a single published version of a package from the workshop. The version's content (its ZIP bytes) is removed permanently and cannot be downloaded again. The version's history entry and content hash are retained, so the number is never reused and a vendored copy stays verifiable, but the bytes are gone — this is a deletion, not the server's hidden tombstone state.

This is destructive administration and **always prompts for confirmation**: there is no `confirmWithUser` opt-out, unlike `package_install` and `package_publish`. The bar is deliberately firmer than `page_unpublish` because deleted version bytes are not recoverable through the workshop, whereas a page is re-publishable static content.

## Parameters

### packageName

The name as published on the workshop (lowercase alphanumeric with single hyphen separators, 1-64 characters).

### version

The version to delete, **required** — there is no default, because a destructive call must name its target. Accepts the same selectors as `package_install`: a version number (`"3"`), an alias (`"stable"`), or `"latest"` (the highest live version). The resolved version is named in the confirmation prompt.

## Returns

A JSON object echoing `packageName`, the resolved `version`, and `deleted: true`.

## Gotchas

- **No default target.** Calling without a version is an error; name the number or alias explicitly.
- **Aliases that point at the deleted version are surfaced in the confirmation.** `latest` is server-managed and repoints to the highest remaining version; whether a publisher alias such as `stable` is detached or repointed is a server behaviour. The prompt lists the affected aliases so the consequence is clear before you confirm.
- **Deleting an already-deleted version reports that state** rather than failing silently.
- **Durability is the consumer's responsibility.** The workshop does not promise eternal availability; a consumer who needs a version permanently should vendor it. See `packages_overview`.
- To remove the entire package and every version at once, use `package_unpublish`.
