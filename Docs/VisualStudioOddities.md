# Visual Studio Oddities

Sometimes (often) things just break in Visual Studio and you have to spend ages figuring out how to fix it.
Hopefully these notes will help if the same issues happend again.

## References to content in nuget packages

A .csproj somehow ended up with a bunch of directives like this to copy an asset from an installed nuget package.
First of all this content isn't needed, and second of all this will break if the solution isn't located at the right
folder depth in the file system. No idea why this happened, but deleting the whole item group fixed the issue.

```
  <ItemGroup>
    <Content Include="..\..\..\..\..\..\.nuget\packages\uno.toolkit.winui\4.2.22\contentFiles\any\net6.0-windows10.0.18362\Assets\BackIcon.scale-100.png" Link="Assets\BackIcon.scale-100.png">
      <Private>False</Private>
      <NuGetPackageVersion>4.2.22</NuGetPackageVersion>
      <NuGetItemType>Content</NuGetItemType>
      <NuGetPackageId>Uno.Toolkit.WinUI</NuGetPackageId>
      <Pack>false</Pack>
    </Content>

    ...

  </ItemGroup>
```

## Intellisense generating invalid errors

This usually happens in code behind files for XAML, Visual Studio starts highlighting non-existent
errors in references to XAML objects.

The fix is easy, albeit very stupid. Just delete the .vs folder in the solution root.
https://weblog.west-wind.com/posts/2018/Aug/07/Fixing-Visual-Studio-Intellisense-Errors

# Refactoring fails with an exception

Occasionally refactoring stops working in Visual Studio. Any attempt to rename an identifier causes
an exception. I've just fixed this so I can't remember what the exception was. I think what fixed
it was unload and reloading a project in the solution that I had added recently. This happened at the
same time as the Intellisense issue mentioned above, so it's quite possible the two are related.
If it happens again, try deleting the .vs folder!