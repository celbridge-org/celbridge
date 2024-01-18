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