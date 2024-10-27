# Localization

Accessibility is a core principal of Celbridge, and one of the main ways we can make the software accessible to more people is by providing high quality localization of all user facing text in the user's own language.

I've written several localization systems for various commercial games and game engines. I have to say that while Uno Platform does provide a system for localizing text and XAML files, it is quite restrictive and hard to work with, especially for the modular architecture we use in Celbridge.

## Issues with XAML and .resw localization

https://platform.uno/docs/articles/features/working-with-strings.html

The localization system works, but it's terribly designed and full of weird gotchas. I've spent hours trying to make sense of this hot garbage. These are the main problems I've encountered so far using XAML and .resw files for localization.

* Localized strings may only be defined in the main application project. 
  * Defining all the strings in a central location works against having a modular architecture.
  * It may be possible to define .resw files in another project but I couldn't figure it out.
* The built-in resource editor for .resw files in Visual Studio is terrible.
  * No support for text search for keys, content or comments.
  * Data entry is super clunky.
  * No support for import/export to spreadsheets for easier editing.
* The x:Uid referencing system is cumbersome and verbose 
  * You can only use the shortened version for referencing resources in XAML files for strings that are defined in the main project (and we don't have any strings like that).
  * It creates a naming dependency on the name of the main project.
* The localization system throws a CLR exception if a string key contains a `.` character
  * The localized string still resolves correctly, but the lookup generates an exception internally when it encounters a string containing `.`
  * Use `_` instead of `.` as a separator for string keys to avoid this issue.
  * I think it happens because the `x:Uid` system uses a `.` to indicate the property that the resource should be applied to, so using it as a separator confuses things.

> I am seriously tempted to ignore the built-in localization and write a custom Localization Service that uses .json language files. It's basically just a key-value lookup system.

## Localization in Celbridge

On the whole, it seems more robust and scalable to just ignore the `x:Uid` system entirely and instead bind all user facing text to public properties on the view class. These properties perform a lookup of the localized text using the `IStringLocalizer` service acquired via Dependency Injection. This ensures that localization used in a consistent way across every project and extension.

This still doesn't resolve the issue of .`resw` files needing to be defined centrally in the main application. That's clearly not going to work later on when we come to support localized user extensions.

I think the most pragmatic approach for now is to use `.resw` files initially and perform all localization via `ÌStringLocalizer`. 

At a later point we can replace the implementation of `IStringLocalizer` with a custom system that allows us to define localized strings anywhere we want, rather than in a centralized `.resw` files.

> I'm investigating using [C# Markup](https://platform.uno/c-markup/) rather than XAML for defining UI. An initial investigation suggests that C# Markup does not support the `x:Uid` approach, which is another reason to avoid it.

# Referencing a localized string in XAML

As noted above, do not use the `x:Uid` system for localization. Use the `IStringLocalizer` approach instead. Just for reference, this is an example of using an `x:Uid` in a `XAML` file to reference a string defined in the main `.resw` file.

```
<Button x:Uid="/Celbridge.MainApplication/Resources/StringKey"  />
```

# Use LocalizedString instead of string

When retrieving a localized string, use the `LocalizedString` class instead of a raw string for storing the localized text. All properties that accept user-facing text accept both types, but the `LocalizedString` class makes it clear what the intended usage is. This make also make it easier to support dynamic language switching in future. 

# Localization Parameters

The [Microsoft Localization Extensions](https://learn.microsoft.com/en-us/dotnet/core/extensions/localization#use-istringlocalizert-and-istringlocalizerfactory) supports formatting localized strings with parameters as part of the string lookup call.  

# Packaged Build Manifest

I don't know if this is an issue or not, but this doc mentions having to explicitly list the supported langauges in the Package.appxmanifest file.
https://platform.uno/docs/articles/guides/localization.html

It currently looks like this:
````
<Resources>
  <Resource Language="x-generate"/>
</Resources>
````