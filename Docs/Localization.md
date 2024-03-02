# Localization

Accessibility is a core principal of Celbridge, and one of the main ways we can make the software accessible to more people is by providing high quality localization of all user facing text in the user's own language.

I've written several localization systems for various commercial games and game engines. I have to say that while Uno Platform does provide a system for localizing text and XAML files, it is quite restrictive and hard to work with, especially for the modular architecture we use in Celbridge.

## Issues with XAML and .resw localization

https://platform.uno/docs/articles/features/working-with-strings.html

These are the main problems I've encountered so far using XAML and .resw files for localization.

* Localized strings may only be defined in the main application project. (It may be possible to define .resw files in another project but I couldn't get it to work).
  * Defining all the strings in a central location works against having a modular architecture.
* The built-in resource editor for .resw files in Visual Studio is extremely basic.
  * No support for text search for keys or content
  * Data entry is awkward
  * No support for import/export to spreadsheets for easier editing
* The x:Uid referencing system is verbose 
  * You can only use the shortened version of referencing resources in XAML files for strings that are defined in the main project (and we don't have any).
  * It creates a dependency on the name of the main project.

## Localization in Celbridge

On the whole, it seems more robust and scalable to just ignore the `x:Uid` system entirely and instead bind all user facing text to public properties on the view class. These properties perform a lookup of the localized text using the `IStringLocalizer` service acquired via Dependency Injection. This ensures that localization used in a consistent way across every project and extension.

This still doesn't resolve the issue of .`resw` files needing to be defined centrally in the main application. That's clearly not going to work later on when we come to support localized user extensions.

I think the most pragmatic approach for now is to use `.resw` files initially and perform all localization via `ÌStringLocalizer`. 

At a later point we can replace the implementation of `IStringLocalizer` with a custom system that allows us to define localized strings anywhere we want, rather than in a centralized `.resw` files.

> I'm investigating using [C# Markup](https://platform.uno/c-markup/) rather than XAML for defining UI. An initial investigation suggests that C# Markup does not support the `x:Uid` approach, which is another reason to avoid it.



# Referencing a localized string in XAML

> As noted above, do not use the `x:Uid` shown here for localization. Use the `IStringLocalizer` approach instead.

This is an example of using an `x:Uid` in a `XAML` file to reference a string defined in the main `.resw` file.

```
<Button x:Uid="/Celbridge.MainApplication/Resources/StringKey"  />
```

