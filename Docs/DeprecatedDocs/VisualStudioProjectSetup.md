# Visual Studio Project Setup

Some notes on how to setup a new class library project in Visual Studio. The Uno Templates generate a usable Visual Studio project, but there are no configuration options so the project has several settings which are unnecessary or incorrect for Celbridge.

# Globally Defined Properties

These properties are defined globally in `Directory.Build.props`, so these declarations in the project are redundant and should be removed.

````
<ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable>
````

# Target Frameworks

The default generated project is setup to target iOS, Android and macCatalyst, which we don't support. Change the <TargetFrameworks> property to the following.

````
<TargetFrameworks>$(TargetFrameworks);$(DotNetVersion);</TargetFrameworks>
````

# Community Toolkit

Older versions of Community Toolkit required a conditional package reference to both `CommunityToolkit.WinUI.UI.Controls` and `Uno.CommunityToolkit.WinUI.UI.Controls`. 

With the v8.x release this approach is no longer required, use `CommunityToolkit.WinUI.Controls` instead on all platforms.



