# Emoji Sprite

A utility that rasterises Noto Color Emoji artwork into PNG sprites and writes them
into a project folder.

## Sprite sets

Work is organised into named **sprite sets**. A sprite set is a glyph list, a resolution,
and a destination folder — everything needed to regenerate one consumer's artwork in a
single click, without touching another's. The utility ships with a set for
`meteoroids`. The toolbar rail creates, duplicates, and deletes sets; the picker
below it chooses one, and the fields under that edit whichever is selected.

The term is deliberate. "Profile" is overloaded — colour profiles, user profiles — and all
of those connote settings that change how something behaves, whereas a sprite set defines
*what gets built and where*. "Sprite sheet" would be actively wrong: that means a packed
atlas, and this emits individual PNGs.

Deleting a set takes two clicks. Sets are the only place a curated glyph list lives, there
is no undo for one, and the destructive click sits next to the harmless ones.

### State and migration

Sets persist in the utility's backing file (`utils:settings._emojisprite`) as
`{ version, selectedId, sets[] }`. Version 1 held a single unnamed `{ glyphs, size,
folder }`; it migrates to one set named after the package it wrote into, so an upgraded
workspace lands on a recognisable set rather than "Default".

## Why it exists

Games want the same Noto glyphs on every host, and a font can't deliver them. WebKit
rejects the Noto web font outright. Chromium/WebView2 loads it, but canvas `fillText`
refuses to rasterise a web-loaded colour font and falls through to the platform emoji
font. Either way the game silently shows Apple or Segoe emoji instead of Noto.

Different hosts, same root cause: **canvas `fillText` is not a reliable way to get colour
emoji out of a font.** The fix is to draw from image artwork instead. This utility
produces that artwork on demand, so no game has to hand-vendor files or grow its own
generator — and because each game owns a sprite set, each can be regenerated on its own.

## How it works

1. The emoji field is split into graphemes, so a ZWJ sequence or a flag counts as one
   glyph rather than several code points.
2. Each glyph maps to a Noto filename: code points, lowercase hex, joined with `_`, with
   VS16 (`U+FE0F`) stripped and ZWJ (`U+200D`) kept — `⛏️` becomes `emoji_u26cf`. This
   addresses the artwork upstream; it is not what the sprite is named.
3. The SVG is fetched from `googlefonts/noto-emoji`, given an intrinsic `width`/`height`
   (Noto ships a `viewBox` only, and a sizeless image draws unreliably), rasterised via a
   same-origin blob URL into a canvas at the chosen resolution, and written as a PNG.

## Sprites are named after the emoji

Output is `soccer-ball.png`, not `emoji_u26bd.png`. JavaScript has no emoji-name API, so
the names come from CLDR's annotation data — the same source that gives emoji their
standard names — fetched alongside the artwork and slugified (`flag: Ireland` becomes
`flag-ireland`).

Two details worth knowing:

- **CLDR's `tts` field is a list**, not a string, even though it only ever holds one name.
- **Distinct glyphs can slug to the same stem** — `keycap: #` and `keycap: *` both reduce
  to `keycap`. A collision falls back to appending the code points, since a silent
  overwrite would drop a sprite.

If CLDR is unreachable the run degrades to code-point names rather than failing, so a
network hiccup costs you readable filenames, not the sprites.

## Consumers

`meteoroids` draws every sprite from the 6-glyph set in "project:game/sprites",
generated at 128×128. Its `game/js/assets.js` lists each sprite's filename explicitly: CLDR
names cannot be derived at runtime without CLDR itself.

The game uses the generated readable names (`red-heart.png`). If a code-point naming
scheme (`emoji_u2764.png`) is ever needed, `outputName()` is the seam for it.

## Fetch at generate time, not at runtime

The generator needs the network; the games do not. Sprites are written into the project
as ordinary PNGs, so the packages that consume them stay offline and deterministic — which
is the whole point of the utility. Vendoring the full Noto set into this package to
avoid a generate-time fetch would cost megabytes to save a dependency that only an author
running the generator ever encounters.

The trade-off is that the artwork is unpinned: regenerating later picks up whatever is on
`main` at that moment. If a package needs byte-stable sprites across regenerations, pin the
`NOTO_SVG_BASE` constant in `js/emoji-sprite.js` to a commit SHA instead of `main`.

## Blank-sprite guard

`img.decode()` can resolve on artwork that still paints nothing, so sprites would go blank
rather than fail visibly. Every canvas is
checked for a non-zero alpha pixel before it is written, and a glyph that renders empty is
reported as a failure instead of writing a transparent PNG.

## Licensing

Noto emoji artwork is Apache-2.0 (the `googlefonts/noto-emoji` bitmap and SVG assets).
Redistributing generated PNGs is permitted; carry the license, with a note that the PNGs
were rendered from the SVGs, in whichever package ships them, as Meteoroids does in
`game/sprites/LICENSE-noto-emoji.txt`. This has not been reviewed by the author yet.
