# Uno Platform Notes

A collection of gotchas and tips that we've figured out as we've been working with Uno Platform.

## Windows Community Toolkit

The [Windows Community Toolkit](https://github.com/CommunityToolkit/Windows) is an open source library that 
extends WinUI with a bunch of useful UI controls.
We also use the similarly named [.NET Community Toolkit](https://github.com/CommunityToolkit/dotnet) open 
source library. It provides support for things like MVVM, diagnostics, etc.

Most of our Nuget package dependencies work fine with Windows and other platforms. However, Windows Community Toolkit 
uses different Nuget packages on Windows vs other platforms. This requires manual editing of the .csproj file to add a condtional package reference, like this:

```
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows'">
  <PackageReference Include="CommunityToolkit.WinUI.UI.Controls" />
</ItemGroup>
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) != 'windows'">
  <PackageReference Include="Uno.CommunityToolkit.WinUI.UI.Controls" />
</ItemGroup>
```

Additional information:

* [Referencing the Windows Community Toolkit from a Cross-Targeted Library](https://platform.uno/docs/articles/uno-community-toolkit.html?tabs=tabid-winui#referencing-the-windows-community-toolkit-from-a-cross-targeted-library)
* [Github Discussion](https://github.com/unoplatform/uno/discussions/11363)