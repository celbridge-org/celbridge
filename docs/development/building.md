# Building and Testing

## Building on Windows

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

## Building on macOS

### Prerequisites

- The .NET SDK version pinned in `global.json` at the repo root (currently `10.0.100`, rolling forward to the latest feature band). The same file pins the Uno SDK version the projects build against.
- The Uno Platform prerequisites, which `uno-check` installs. Follow the [Uno Platform setup instructions](https://platform.uno/docs/articles/get-started.html); that page covers macOS and links to the Rider and VS Code specific setup.
- Visual Studio does not exist on macOS, so use JetBrains Rider or VS Code. The repo carries a Rider run configuration for the app bundle publish, described under Publishing below.

### Building

The projects target three frameworks: `net10.0` (plain library), `net10.0-windows10.0.22621` (the packaged WinAppSDK head) and `net10.0-desktop` (the Skia head). Only the Skia head ships on macOS, and the packaged Windows target framework cannot be built there at all, so build that framework explicitly rather than the whole project:

```
dotnet build Source/Celbridge/Celbridge.Application.csproj -f net10.0-desktop
```

The MSBuild requirement in the Windows section is specific to the WinUI/WinAppSDK head, which is not in play on macOS.

### Running

```
dotnet run --project Source/Celbridge/Celbridge.Application.csproj -f net10.0-desktop
```

A Debug build on macOS also runs correctly in place from its output folder. A published bundle keeps app content under `Contents/Resources`, which is where `ms-appx` asset lookups go, whereas a plain build lays that content out beside the executable. The `StageBundledAssetsForLocalRun` target in `Celbridge.Application.csproj` mirrors the published shape with a folder of symlinks so both behave the same. Without it the built binary resolves no bundled asset, and every icon renders as the fallback glyph.

### Publishing the app bundle

`Source/Celbridge/Properties/PublishProfiles/macOS-AppBundle.pubxml` holds the canonical bundle settings: Release, `net10.0-desktop`, `osx-arm64`, self-contained, Uno `PackageFormat=app`. It produces a self-contained `Celbridge.app`.

```
dotnet publish Source/Celbridge/Celbridge.Application.csproj -f net10.0-desktop -p:PublishProfile=macOS-AppBundle
```

`-f` is required: a multi-targeted project cannot infer the framework from the profile alone.

In Rider, use the bundled "Publish macOS app bundle" run configuration in `.run/`, which runs that command. Rider's GUI Publish dialog does not read the profile and cannot pass `PackageFormat`, so it will not produce the bundle. Use the run configuration or the CLI.

Signing and notarization are added with `-p:CodesignKey` and `-p:UnoMacOSNotarizeKeychainProfile`. The bundle enables the hardened runtime (required for notarization) and grants the CoreCLR JIT its writable-then-executable memory through `Platforms/Desktop/Entitlements.plist`. The two travel together: under the hardened runtime the JIT faults without `allow-jit`. Both are consumed only by the app bundle publish, so a plain `net10.0-desktop` build ignores them.

### Cross-platform cautions

**A macOS build does not compile Windows-only code.** `WindowsWebViewAdapter.cs` and its DI registration in `PlatformServiceConfiguration.cs` sit inside `#if WINDOWS`, which is defined only for the packaged target framework. Changing a shared interface (`IWebViewAdapter` and the other platform seams) on macOS therefore compiles green while the Windows implementation silently no longer satisfies it. Build the packaged Windows head on a Windows machine before merging cross-platform interface changes.

The Windows Skia head (`net10.0-desktop` on Windows) is a convenience for exercising the Skia code path without a Mac. It is not a deployment target; the deployment targets are the packaged Windows head and the macOS Skia head.

## Running Tests

The test project does not contain XAML and can be built and run with `dotnet`. It targets `net10.0` alone, so this command is the same on Windows and macOS:

```
dotnet test Source/Tests/Celbridge.Tests.csproj
```

Pass `--no-restore` in the inner loop. `dotnet` re-walks the restore graph over all 24 referenced projects on every invocation, which costs several seconds and finds nothing new unless a package reference changed:

```
dotnet test Source/Tests/Celbridge.Tests.csproj --no-restore
```

Run JS tests from the `Source/` folder:

```
cd Source && npm test
```

Run Python tests using a virtual environment. Create it at the repo root, never inside `Source/`: the Uno SDK's item globs and the architecture tests both walk every folder under a project, and neither can be told to skip a venv sitting there.

```
python -m venv .venv
.venv\Scripts\activate
cd Source/Workspace/Celbridge.Python
pip install -e "packages/celbridge[dev]"
python run_tests.py
```

The venv is for running the tests only. The wheel build (`build.py`) resolves its own interpreter from PATH and never looks for a venv, so where the venv lives has no bearing on the build.

## Linting

JavaScript is linted with ESLint from the `Source/` folder:

```
cd Source && npm run lint
```

There is no linter configured for C# or Python. See [Coding Conventions](coding_conventions.md).

## Continuous Integration

`.github/workflows/ci.yml` runs three jobs on every push and on pull requests targeting `main`: the .NET tests, the JS tests and lint, and the Python tests.
