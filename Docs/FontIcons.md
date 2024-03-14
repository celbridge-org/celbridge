# Font Icons

SymbolIcon is handy but pretty limited. Font Icon is more awkward to use, but give you more flexibility, such as being able to set the size.

The Symbol enum is listed here. Copy the unicode value on the right hand side of that table for the icon you want to display.
https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-ui-symbol-font

To specify the icon directly in XAML, reference the unicode value like this:

```
<FontIcon FontFamily="{StaticResource SymbolThemeFontFamily}" 
          Glyph="&#xE102;" 
          FontSize="18" />
```

Note the FontSize attribute above for setting the icon size.

To specify the icon directly from C#, reference the unicode value in a string like this:

```
public string Icon => "\uE10B"; // Accept icon
```

List of MDL2 Font Icons:
http://modernicons.io/segoe-mdl2/cheatsheet/
