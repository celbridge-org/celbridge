# workshop_set_package_alias

Creates an alias pointing at a workshop version, or moves an existing alias to a different workshop version. Aliases are named pointers such as `stable` that let installers track a moving target without naming a fixed number. This is publisher curation. To read a package's current aliases, use `workshop_get_package_info`.

Setting an alias never changes version content, it only repoints a label, so it is not gated with a confirmation prompt.

## Parameters

### packageName

The name as published on the workshop (lowercase alphanumeric with single hyphen separators, 1-64 characters).

### alias

The alias to create or move (e.g. `stable`). Same character rule as a package name. `latest` is reserved for the highest live workshop version and is never an alias, so it is rejected here.

### workshopVersion

The workshop version the alias should point at, a positive integer the workshop assigned. `workshop_get_package_info` lists the available workshop versions.

## Returns

A JSON object echoing `packageName`, `alias`, and `workshopVersion`.

## Gotchas

- The workshop validates that the target workshop version exists, and a missing or deleted one surfaces as an error.
- Moving an alias is the same call as creating one, because setting always overwrites the alias's current target.
