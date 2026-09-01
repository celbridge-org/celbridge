# Design Tokens

Colours and the shared UI dimensions are held once in
`Source/Core/Celbridge.DesignTokens/DesignTokens.json` and generated at build time into two files, so
the native and web sides cannot disagree about a value:

- `Celbridge.UserInterface/Resources/ColorTokens.xaml` — the theme dictionaries, each holding its own colours and the brushes over them
- `Celbridge.WebHost/Web/celbridge-client/celbridge-tokens.css` — the `--cel-*` custom properties served to WebView content

Both are gitignored. Never edit them: change the token source and rebuild. `Celbridge.UserInterface`
and `Celbridge.WebHost` each reference `Celbridge.DesignTokens` so the generator runs first, and each
declares its generated file as an explicit build item so a clean checkout resolves.

Everything the generator emits is declared once per theme dictionary, never once over the palette. A
brush declared over the dictionaries is a single shared object whose colour resolves against the
application theme, which follows the OS, so it ignores the `ElementTheme` the app applies to the
window root and paints the wrong palette whenever the two disagree.

## Redirecting WinUI control keys

Redirecting a WinUI control key onto the palette is a token declaration, not hand written markup. It
takes one of two forms; which form a key needs is visible in `generic.xaml`, shipped in the
`microsoft.windowsappsdk` package:

- `xamlAliases` emits a brush per theme under the WinUI key. This works when the control's own style references that key
- `xamlColorAliases` emits a `Color` instead, and is what reaches a key WinUI declares as a `StaticResource` alias onto another brush: such an alias binds to the brush object rather than to the key, so overriding the brush it points at changes nothing, while overriding the colour underneath reaches every alias built over it

`Colors.xaml` stays hand written and holds only the overrides that take no palette colour
(`WindowCaption*`).

## The accent ramp

The accent is the application's own, never the one the OS supplies:

- The `accent` token redirects the whole `SystemAccentColor` ramp, which every accent reference in `generic.xaml` resolves through. Nothing outside the generated dictionary may read `SystemAccentColor`; `DesignTokenCoverageTests` fails the build on it
- A WinUI key the chrome reads directly has to be listed in `WinUiKeysReadDirectly` in the same tests, so reaching for a new one is a deliberate edit
- `TextOnAccentFillColor*` is the one accent key not built over the ramp, and WinUI reaches it through a `StaticResource` alias, so the key itself cannot be redirected: the text WinUI draws over its own accent fills holds a fixed pair, black on dark and white on light, while `accent-text` is white on both
- A control is still reachable when its own style resolves a per-control foreground key by `ThemeResource`, because that lookup answers to the generated dictionary: `AccentButtonStyle` resolves `AccentButtonForeground` and its hover and press variants that way, so those are aliased onto `accent-text`. What stays out of reach is a style binding `TextOnAccentFillColorPrimaryBrush` itself. Check which of the two a control does before concluding it cannot be reached

## Token lifecycle

- A token marked `published` is part of the contribution contract that packages outside this repository are written against, so renaming or removing one also means updating the snapshot in `DesignTokenCoverageTests`
- The same tests fail on a token nothing consumes, which is how a token stops outliving the tone it names: give it a consumer, redirect a control key onto it, drop the target that has none, or record the exception in `TokensWithoutHostConsumer` with the reason
