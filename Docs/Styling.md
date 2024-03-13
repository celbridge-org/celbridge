# Styling

Changing the color of a control, should be easy right? Oh dear god no.

# Built-in system brushes

When you create a new Uno Platform application with a blank page, the background color is set like this:

````
Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">
````

So obvious question - where is "ApplicationPageBackgroundThemeBrush" defined and what other values can I use here? The answer is... buried in an xml file somewhere in the Windows App SDK, I think?

This is the only list of these resourcess I could find, it's ancient but the ones I tried worked. http://metro.excastle.com/xaml-system-brushes

I think it's better to ignore these built-in colors and brushes and just define custom ones that we at least have
full control over.

# Static and Theme Resources

This documentation explains how to get static and theme resources using C# Markup. https://platform.uno/docs/articles/external/uno.extensions/doc/Reference/Markup/StaticAndThemeResources.html

This works and is absolutely the way to go. Define all custom colors in the Colors.xaml resource dictionary, taking care to define the colors in a `<ResourceDictionary.ThemeDictionaries>` section so that theme switching works automatically.

Define static resource brushes based on those color and access them like this in C# Markup:

````
.Background(x => x.StaticResource("PanelBackgroundBBrush"))
````

Using StaticResource here is fine, the brush automatically changes color when the system theme switched. For resources that are defined inside a `<ResourceDictionary.ThemeDictionaries>` section (e.g. a color resource) you would presumably need to use `x.ThemeResources` instead of `x.StaticResources`.

# Lightweight Styling

This is a system that allows you to customize specific properties in a style using a little bit of XAML or C# Markup.
https://platform.uno/docs/articles/external/uno.themes/doc/lightweight-styling.html

Unfortunately, the documentation seems to assume that you are using the `Uno Material` style, and the constants shown in the docs seem to only work when using the `Material` style.

The [Uno Platform Gallery](https://gallery.platform.uno) has a section on Lightweight Style with some examples using the `Fluent` style, so presumably it's possible to get it working. 

To get this working in XAML, the trick is to remove the `Style="{StaticResource FilledButtonStyle}"` specified in the Uno documentation. Looks like that's only necessary for `Uno Material`, and specifying it when using the `Fluent` style causes an exception. 

The other trick is to specify the correct resource key for the type of control you are styling. e.g. "ButtonBackground" in the example below. I haven't yet figured out where these keys are defined.

````
<Button Content="Overridden Button Style">
  <Button.Resources>
    <SolidColorBrush x:Key="ButtonBackground"
                   Color="LightGreen" />
  </Button.Resources>
</Button>
````

Similarly in C# markup:
````
new Button()
    .Content("Do Stuff")
    .Resources(config => config
        .Add("ButtonBackground", new SolidColorBrush(Colors.DarkRed))
)
````

There appears however to be a limitation with the `.Add` method. It does not appear to be possible to pass a reference to an existing `StaticResource`, it has to be a standalone resource object. I don't fully understand why that restriction applies, but it can be worked around by constructing brushes and color objects manually at the point where they are specified. This does seem to be a point where XAML has an advantage over C# Markup.

The `Add()` method does provide overloads that allow you to specify both a light and dark resource in the same call.

# Theme Switching

I get a crash when changing the system dark/light if I specify the background color of a page using `.Background(Theme.Brushes.Background.Default)`. As far as I can tell, the `Theme.Brushes` approach is only valid when using the `Material` style, and shouldn't be used for `Fluent` style applications.

Anyway, theme switching based on the OS settings seems to work out of the box, even on Skia + Gtk, at least on Windows. For now we're only going to support switching the theme via the OS settings. We can easily add a setting for it later when we have the setting infrastructure in place.



