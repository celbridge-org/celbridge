# workshop_remove_package_alias

Removes an alias from a workshop package. Only the named pointer is detached. The workshop version it pointed at, and all version content, are untouched. This is non-destructive curation, the inverse of `workshop_set_package_alias`, and is not gated with a confirmation prompt.

## Parameters

### packageName

The name as published on the workshop (lowercase alphanumeric with single hyphen separators, 1-64 characters).

### alias

The alias to remove (e.g. `stable`). `latest` is reserved for the highest live workshop version and is never an alias, so it is rejected here.

## Returns

A JSON object echoing `packageName` and `alias`, with `removed: true`.

## Gotchas

- Removing an alias does not delete any workshop version, so installers pinned to a specific workshop version number are unaffected.
- Removing an alias that does not exist surfaces as an error from the workshop.
