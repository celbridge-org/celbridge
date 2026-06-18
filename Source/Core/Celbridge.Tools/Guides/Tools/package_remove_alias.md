# package_remove_alias

Removes an alias from a workshop package. Only the named pointer is detached; the version it pointed at, and all version content, are untouched. This is non-destructive curation, the inverse of `package_set_alias`, and is not gated with a confirmation prompt.

## Parameters

### packageName

The name as published on the workshop (lowercase alphanumeric with single hyphen separators, 1-64 characters).

### alias

The alias to remove (e.g. `stable`). The `latest` alias is managed by the workshop and is rejected here.

## Returns

A JSON object echoing `packageName` and `alias`, with `removed: true`.

## Gotchas

- Removing an alias does not delete or tombstone any version; installers pinned to a specific version number are unaffected.
- Removing an alias that does not exist surfaces as an error from the workshop.
