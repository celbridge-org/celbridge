# package_set_alias

Creates an alias pointing at a version, or moves an existing alias to a different version. Aliases are named pointers such as `stable` that let installers track a moving target without naming a fixed version number. This is publisher curation; to read a package's current aliases use `package_info`.

Setting an alias never changes version content — it only repoints a label — so it is not gated with a confirmation prompt.

## Parameters

### packageName

The name as published on the workshop (lowercase alphanumeric with single hyphen separators, 1-64 characters).

### alias

The alias to create or move (e.g. `stable`). Same character rule as a package name. The `latest` alias is managed by the workshop and is rejected here.

### version

The version number the alias should point at. A positive integer assigned by the workshop; see `package_info` for the available versions.

## Returns

A JSON object echoing `packageName`, `alias`, and `version`.

## Gotchas

- The workshop validates that the target version exists; a missing or tombstoned version surfaces as an error.
- Moving an alias is the same call as creating one — `set` always overwrites the alias's current target.
