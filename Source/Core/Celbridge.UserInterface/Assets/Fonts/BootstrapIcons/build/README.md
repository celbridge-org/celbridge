# Bootstrap Icons

Bootstrap Icons v1.12.1, MIT licensed, from https://github.com/twbs/icons.

The parent folder holds the vendored files the application reads:

- `bootstrap-icons.ttf` — the font, loaded through `ms-appx` and applied by the `Icon` control.
- `icon-glyphs.json` — the name to codepoint map `IconService` resolves `bs-` names through.
- `icon-keywords.json` — the search keywords the icon picker matches alongside the name. Generated,
  never edited by hand.
- `LICENSE` — the upstream licence, reproduced verbatim.

The font and the glyph map are taken from the release's `font/` folder. The keywords are the
`categories` and `tags` each icon declares in the front matter of its documentation page, which is what
the Bootstrap Icons site searches; that data is not part of the npm package, so `vendor.mjs` reads it
from the release tarball. The generated file covers the icons the bundled glyph map carries, so the
documentation moving ahead of the bundled font cannot introduce a name the font is unable to draw.

To refresh the keywords:

```
cd build && npm run vendor
```

The upstream release is pinned as `upstreamTag` in `vendor.mjs`. Bump it when the font is upgraded, and
bump `BundledBootstrapIconsVersion` in `IconCatalogCoverageTests` to match.
