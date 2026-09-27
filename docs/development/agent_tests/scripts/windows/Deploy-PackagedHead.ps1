<#
.SYNOPSIS
Builds the packaged Windows head and copies the build into the layout its development registration runs from.

.DESCRIPTION
A command-line build refreshes the build output but never the registered AppX layout, so without this step
the app launches and runs the previous build with no warning. Visual Studio registers the layout on its
first F5, which this script relies on and does not do itself.

The script builds with the MSBuild that vswhere finds, reads the build's .appxrecipe, copies every packaged
file whose bytes differ into the registered layout, and then checks that each one matches. It also reports
files in the layout that the recipe no longer lists. A loose .xaml with no .xbf beside it is one of these,
and it throws when its type is activated.

.PARAMETER Configuration
The build configuration. The registration must point at this configuration's layout.

.PARAMETER SkipBuild
Copies the existing build output without building first.

.PARAMETER RemoveOrphans
Deletes the files the recipe no longer lists, instead of only reporting them.

.EXAMPLE
.\Deploy-PackagedHead.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipBuild,
    [switch]$RemoveOrphans
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
$projectFile = Join-Path $repoRoot 'Source\Celbridge\Celbridge.Application.csproj'
$targetFramework = 'net10.0-windows10.0.22621'
$outputFolder = Join-Path $repoRoot "Source\Celbridge\bin\$Configuration\$targetFramework\win-x64"
$recipeFile = Join-Path $outputFolder 'Celbridge.Application.build.appxrecipe'

# A running instance holds its DLLs open, so a copy would leave a mixed layout behind.
if (Get-Process -Name 'Celbridge' -ErrorAction SilentlyContinue) {
    throw 'Celbridge is running. Close it before deploying.'
}

$package = Get-AppxPackage -Name 'org.celbridge.Celbridge' | Where-Object IsDevelopmentMode | Select-Object -First 1
if (-not $package) {
    throw 'Celbridge has no development registration. Run the packaged head once from Visual Studio (F5), which registers it, then run this script again.'
}

$layoutFolder = $package.InstallLocation
$expectedLayout = Join-Path $outputFolder 'AppX'
if ($layoutFolder.TrimEnd('\') -ne $expectedLayout.TrimEnd('\')) {
    throw "The development registration runs from '$layoutFolder', not the $Configuration layout '$expectedLayout'. Deploy the $Configuration configuration once from Visual Studio, or pass the configuration the registration uses."
}

if (-not $SkipBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = & $vswhere -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) {
        throw 'MSBuild was not found. Install Visual Studio with the .NET desktop and WinUI workloads.'
    }

    # Passing a Platform would move the output under bin\x64, which the registration does not point at.
    & $msbuild $projectFile -t:Build "-p:Configuration=$Configuration" "-p:TargetFramework=$targetFramework" -m -v:minimal -nologo
    if ($LASTEXITCODE -ne 0) {
        throw "The build failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $recipeFile)) {
    throw "The build recipe '$recipeFile' is missing. Build the $Configuration configuration first."
}

[xml]$recipe = Get-Content -LiteralPath $recipeFile -Raw
$namespaces = New-Object System.Xml.XmlNamespaceManager($recipe.NameTable)
$namespaces.AddNamespace('m', $recipe.DocumentElement.NamespaceURI)

# The manifest is listed apart from the packaged files, and some package paths mix separators.
$entries = @($recipe.SelectNodes('//m:AppxPackagedFile | //m:AppXManifest', $namespaces)) | ForEach-Object {
    [pscustomobject]@{
        Source = $_.GetAttribute('Include')
        PackagePath = ($_.SelectSingleNode('m:PackagePath', $namespaces).InnerText) -replace '/', '\'
    }
}

function Test-SameFile([string]$First, [string]$Second) {
    if (-not (Test-Path -LiteralPath $Second)) {
        return $false
    }
    if ((Get-Item -LiteralPath $First).Length -ne (Get-Item -LiteralPath $Second).Length) {
        return $false
    }
    return (Get-FileHash -LiteralPath $First).Hash -eq (Get-FileHash -LiteralPath $Second).Hash
}

$copied = New-Object System.Collections.Generic.List[string]
$missingSources = New-Object System.Collections.Generic.List[string]
foreach ($entry in $entries) {
    if (-not (Test-Path -LiteralPath $entry.Source)) {
        $missingSources.Add($entry.Source)
        continue
    }

    $destination = Join-Path $layoutFolder $entry.PackagePath
    if (Test-SameFile $entry.Source $destination) {
        continue
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $entry.Source -Destination $destination -Force
    $copied.Add($entry.PackagePath)
}

$mismatched = @($entries | Where-Object {
    (Test-Path -LiteralPath $_.Source) -and -not (Test-SameFile $_.Source (Join-Path $layoutFolder $_.PackagePath))
})

$listed = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $entries) {
    [void]$listed.Add($entry.PackagePath)
}

# Visual Studio keeps its own record of what it deployed in the layout, which is not part of the package.
$orphans = @(Get-ChildItem -LiteralPath $layoutFolder -Recurse -File | ForEach-Object {
    $_.FullName.Substring($layoutFolder.TrimEnd('\').Length + 1)
} | Where-Object { -not $listed.Contains($_) -and $_ -ne 'vs.appxrecipe' })

$looseXaml = @($orphans | Where-Object {
    $_ -like '*.xaml' -and -not (Test-Path -LiteralPath (Join-Path $layoutFolder ($_ -replace '\.xaml$', '.xbf')))
})

if ($RemoveOrphans) {
    foreach ($orphan in $orphans) {
        Remove-Item -LiteralPath (Join-Path $layoutFolder $orphan) -Force
    }
}

Write-Host "Layout: $layoutFolder"
Write-Host "Recipe entries: $($entries.Count). Copied: $($copied.Count). Missing sources: $($missingSources.Count)."
foreach ($path in $copied) {
    Write-Host "  copied $path"
}

if ($orphans.Count -gt 0) {
    if ($RemoveOrphans) {
        $verb = 'Removed'
    } else {
        $verb = 'Not in the recipe'
    }
    Write-Host "$verb`: $($orphans.Count) file(s)."
    foreach ($orphan in $orphans) {
        Write-Host "  $orphan"
    }
    if ($looseXaml.Count -gt 0 -and -not $RemoveOrphans) {
        Write-Warning "$($looseXaml.Count) loose .xaml file(s) have no .xbf beside them and throw when their type is activated. Run again with -RemoveOrphans."
    }
}

if ($mismatched.Count -gt 0) {
    throw "$($mismatched.Count) file(s) in the layout still differ from the build: $(($mismatched | Select-Object -First 5 | ForEach-Object PackagePath) -join ', ')"
}

Write-Host 'The layout matches the build.'
