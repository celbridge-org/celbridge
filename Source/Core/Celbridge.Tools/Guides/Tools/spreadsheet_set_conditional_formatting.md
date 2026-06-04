# spreadsheet_set_conditional_formatting

Conditional formatting drives cell appearance based on cell values or a formula, so highlights stay correct as data changes (vs. `spreadsheet_format_ranges`, which sets static styles).

Common cases: highlight cells over or under a threshold, top or bottom N items in a list, colour scale across a column of numbers, formula-based highlight for whole rows.

## Rule object shape

Each rule has a `type` field plus the inputs that type needs, and (for non-color-scale rules) optional formatting fields applied to matched cells.

| Type | Inputs |
|---|---|
| `colorScale2` | `minColor`, `maxColor` |
| `colorScale3` | `minColor`, `midColor`, `maxColor` |
| `greaterThan`, `greaterThanOrEqual`, `lessThan`, `lessThanOrEqual`, `equal`, `notEqual` | `value` |
| `between`, `notBetween` | `value`, `value2` |
| `containsText`, `doesNotContainText`, `beginsWith`, `endsWith` | `text` |
| `isBlank`, `isNotBlank`, `isError`, `isNotError`, `duplicateValues`, `uniqueValues` | none |
| `formula` | `formula` (Excel formula string with or without leading `=`) |
| `top`, `bottom` | `value` (positive integer count) |
| `topPercent`, `bottomPercent` | `value` (1-100) |

## Formatting fields (non-color-scale rules)

- `backgroundColor`, `fontColor` — CSS hex strings (`#RRGGBB`).
- `bold`, `italic` — booleans.

`minColor` is the colour at the low end of the value range; `maxColor` is the colour at the high end.

## Color-scale stop thresholds

Color-scale rules accept optional thresholds via `minType`/`minValue`, `midType`/`midValue`, `maxType`/`maxValue`:

- `minType` / `maxType` default to `min` / `max` (range minimum/maximum).
- `midType` defaults to `percent` at value `50`.
- Supported types: `min`, `max` (minType/maxType only), `number`, `percent`, `percentile`, `formula` (value is the formula string with or without leading `=`).

## clearExisting

With `clearExisting: true`, any pre-existing conditional rules whose ranges intersect the target range are removed before the new rules are added.
