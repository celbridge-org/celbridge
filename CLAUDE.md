# Celbridge - Claude Code Instructions

## Project Overview

Celbridge is a cross-platform desktop application built with Uno Platform and WinUI. The solution is at `Celbridge.slnx` in the repo root.

## Building

We recommend building with the latest Visual Studio 2026. This is an Uno Platform project with XAML files targeting WinUI/WinAppSDK. The WinUI projects require MSBuild (not `dotnet build`) because Uno SDK raises error UNOB0008 when XAML files are present.

Use the MSBuild that ships with your Visual Studio installation:

```
msbuild Celbridge.slnx -t:Build -p:Configuration=Debug -verbosity:minimal -nologo
```

If `msbuild` is not on your PATH (e.g. outside of a Developer PowerShell), it is typically located at:

```
C:/Program Files/Microsoft Visual Studio/<version>/<edition>/MSBuild/Current/Bin/MSBuild.exe
```

For example, with VS 2026 Community: `C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe`.

## Running Tests

The test project does not contain XAML and can be built and run with `dotnet`:

```
dotnet test Source/Tests/Celbridge.Tests.csproj
```

Run JS tests from the `Source/` folder:

```
cd Source && npm test
```

## Code Conventions

- Use full descriptive variable names, never abbreviate
- Do not add section marker comments like `// -- Initialization --`
- Never use `#region` / `#endregion`
- Order interface methods by lifecycle stage; match that order in implementations
- Use "folder" not "directory" in naming (exception: external APIs)
- Use CRLF line endings (Windows project)
- Follow the patterns in `ProjectConfigParser.cs` as a reference for coding style
