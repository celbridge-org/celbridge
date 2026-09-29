# Helpers for running agent test plans against the packaged Windows head.
#
# Each shell call an agent makes starts a fresh PowerShell, so every call imports this module and names its
# run folder first. The module keeps everything a later call needs in that folder: the settings backup, the
# running app's port and log, and the MCP session.
#
#   Import-Module <repo>\docs\development\agent_tests\scripts\windows\CelbridgeAgentTest.psm1 -Force
#   Use-CelbridgeRun 'C:\Temp\celbridge_agent_tests\html_editor_2026-09-27'

$ErrorActionPreference = 'Stop'

$script:RunFolder = $null

# A type cannot be redefined in a session, so a session that loaded an older copy of this module keeps its
# older native type. Check for the newest member, and fail now rather than at the first call that needs it.
$nativeType = 'CelbridgeAgentTestNative' -as [type]
if ($nativeType -and -not $nativeType.GetMethod('IsIconic')) {
    throw 'This PowerShell session holds an older copy of this module. Start a new session and import it again.'
}

if (-not $nativeType) {
    Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Runtime.InteropServices;
public static class CelbridgeAgentTestNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
'@
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# Per-monitor DPI awareness, so window rectangles, element bounds and screen captures are all physical pixels.
[void][CelbridgeAgentTestNative]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))

function Get-RunFile([string]$Name) {
    if (-not $script:RunFolder) {
        throw 'No run folder. Call Use-CelbridgeRun first.'
    }
    return Join-Path $script:RunFolder $Name
}

<#
.SYNOPSIS
Names the folder this run keeps its state and evidence in, creating it if needed. Call it at the start of
every shell call.
#>
function Use-CelbridgeRun([Parameter(Mandatory)][string]$Folder) {
    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    $script:RunFolder = (Resolve-Path $Folder).Path
}

<#
.SYNOPSIS
Appends a timestamped line to the run's timeline and echoes it.
#>
function Write-RunLog([Parameter(Mandatory)][string]$Text) {
    $line = "$(Get-Date -Format 'HH:mm:ss.fff') $Text"
    Add-Content -LiteralPath (Get-RunFile 'timeline.txt') -Value $line -Encoding UTF8
    return $line
}

<#
.SYNOPSIS
The development registration Visual Studio made, which the deploy script keeps up to date.
#>
function Get-CelbridgePackage {
    $package = Get-AppxPackage -Name 'org.celbridge.Celbridge' | Where-Object IsDevelopmentMode | Select-Object -First 1
    if (-not $package) {
        throw 'Celbridge has no development registration. Run the packaged head once from Visual Studio (F5).'
    }
    return $package
}

function Get-CelbridgeLocalState {
    $package = Get-CelbridgePackage
    return Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalState"
}

function Get-CelbridgeSettingsPath {
    return Join-Path (Get-CelbridgeLocalState) 'settings.json'
}

function Get-CelbridgeProcess {
    return @(Get-Process -Name 'Celbridge' -ErrorAction SilentlyContinue)
}

<#
.SYNOPSIS
Copies the app's settings.json and records its hash, so the run can put it back byte for byte. An existing
backup is kept, so a run that is resumed still restores the settings from before it started.
#>
function Backup-CelbridgeSettings {
    $backup = Get-RunFile 'settings.json.backup'
    if (Test-Path -LiteralPath $backup) {
        return Write-RunLog "Settings backup kept from earlier: $backup"
    }

    $settings = Get-CelbridgeSettingsPath
    Copy-Item -LiteralPath $settings -Destination $backup
    (Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash | Set-Content -LiteralPath (Get-RunFile 'settings.json.sha256')
    return Write-RunLog "Settings backed up: $((Get-Content -LiteralPath (Get-RunFile 'settings.json.sha256')).Trim())"
}

<#
.SYNOPSIS
Puts the app's settings.json back from the run's backup and checks the hash. The app must be closed first,
since it writes its settings as it quits.
#>
function Restore-CelbridgeSettings {
    if ((Get-CelbridgeProcess).Count -gt 0) {
        throw 'Celbridge is still running. Stop it before restoring its settings.'
    }

    $settings = Get-CelbridgeSettingsPath
    Copy-Item -LiteralPath (Get-RunFile 'settings.json.backup') -Destination $settings -Force

    $expected = (Get-Content -LiteralPath (Get-RunFile 'settings.json.sha256')).Trim()
    $actual = (Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash
    if ($actual -ne $expected) {
        throw "The restored settings.json does not match the backup: $actual, expected $expected"
    }
    return Write-RunLog "Settings restored: $actual"
}

<#
.SYNOPSIS
Sets the project the app opens at launch. The setting's value is JSON held inside a JSON string, so a path
is escaped twice.
#>
function Set-CelbridgePreviousProject([Parameter(Mandatory)][string]$ProjectFile) {
    # Built at runtime, since file tools can decode a literal escape sequence in a script's text.
    $quote = [string][char]92 + 'u0022'
    $inner = $ProjectFile.Replace('\', '\\')
    $value = $quote + $inner.Replace('\', '\\') + $quote

    $settings = Get-CelbridgeSettingsPath
    $text = [IO.File]::ReadAllText($settings)
    $pattern = '("Project\.PreviousProject":\s*")([^"]*)(")'
    if ($text -notmatch $pattern) {
        throw 'Project.PreviousProject was not found in settings.json. Open any project in the app once, then try again.'
    }

    $replaced = [regex]::Replace($text, $pattern, { param($match) $match.Groups[1].Value + $value + $match.Groups[3].Value })
    [IO.File]::WriteAllText($settings, $replaced, (New-Object System.Text.UTF8Encoding $false))
}

<#
.SYNOPSIS
Launches the packaged head on a project and waits for its workspace to load. Refuses when Celbridge is
already running, which may be the user's own instance.
#>
function Start-Celbridge([Parameter(Mandatory)][string]$ProjectFile, [int]$TimeoutSeconds = 90) {
    if ((Get-CelbridgeProcess).Count -gt 0) {
        throw 'Celbridge is already running. Ask the user to close it, and never close an instance this run did not start.'
    }

    Set-CelbridgePreviousProject $ProjectFile

    $logFolder = Join-Path (Get-CelbridgeLocalState) 'Logs'
    $before = @(Get-ChildItem -LiteralPath $logFolder -Filter 'celbridge_*.log' -ErrorAction SilentlyContinue | ForEach-Object FullName)

    # Launched through the shell by its application ID, which is how a packaged app gets its identity.
    $package = Get-CelbridgePackage
    Start-Process explorer.exe "shell:AppsFolder\$($package.PackageFamilyName)!App"

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $log = $null
    while (-not $log -and (Get-Date) -lt $deadline) {
        $log = Get-ChildItem -LiteralPath $logFolder -Filter 'celbridge_*.log' -ErrorAction SilentlyContinue |
            Where-Object { $before -notcontains $_.FullName } |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $log) { Start-Sleep -Milliseconds 250 }
    }
    if (-not $log) {
        throw 'No new log appeared, so the app did not start.'
    }

    $port = $null
    $loaded = $false
    while ((Get-Date) -lt $deadline -and (-not $port -or -not $loaded)) {
        $text = Get-Content -LiteralPath $log.FullName -Raw -ErrorAction SilentlyContinue
        if ($text -match 'Server started on port (\d+)') { $port = [int]$Matches[1] }
        if ($text -match 'Workspace loaded in') { $loaded = $true }
        if (-not $port -or -not $loaded) { Start-Sleep -Milliseconds 250 }
    }
    if (-not $port) { throw "The log never named the server port: $($log.FullName)" }
    if (-not $loaded) { throw "The workspace did not finish loading: $($log.FullName)" }

    $process = Get-CelbridgeProcess | Select-Object -First 1
    $state = [pscustomobject]@{ Pid = $process.Id; Port = $port; Log = $log.FullName }
    $state | ConvertTo-Json | Set-Content -LiteralPath (Get-RunFile 'app.json') -Encoding UTF8
    Remove-Item -LiteralPath (Get-RunFile 'mcp_session.json') -ErrorAction SilentlyContinue

    [void](Write-RunLog "Launched pid $($state.Pid) on port $port, log $($log.Name)")
    return $state
}

<#
.SYNOPSIS
Closes the app the way a user does, which lets it save its workspace, and waits for it to exit.
#>
function Stop-Celbridge([int]$TimeoutSeconds = 30) {
    foreach ($process in Get-CelbridgeProcess) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            throw "Celbridge pid $($process.Id) did not exit."
        }
        [void](Write-RunLog "Quit pid $($process.Id)")
    }
}

<#
.SYNOPSIS
The running app's pid, port and log, as Start-Celbridge recorded them.
#>
function Get-CelbridgeApp {
    $file = Get-RunFile 'app.json'
    if (-not (Test-Path -LiteralPath $file)) {
        throw 'No app recorded for this run. Call Start-Celbridge first.'
    }
    return Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
}

<#
.SYNOPSIS
The lines the app has logged, optionally from a line onwards and matching a pattern. Pass the count from an
earlier call as -FromLine to read only what came after it.
#>
function Get-CelbridgeLogLines([int]$FromLine = 0, [string]$Pattern = '') {
    $lines = @(Get-Content -LiteralPath (Get-CelbridgeApp).Log)
    $selected = $lines | Select-Object -Skip $FromLine
    if ($Pattern) {
        $selected = $selected | Where-Object { $_ -match $Pattern }
    }
    return $selected
}

function Get-CelbridgeLogLineCount {
    return @(Get-Content -LiteralPath (Get-CelbridgeApp).Log).Count
}

function Read-McpBody($Response) {
    $content = $Response.Content
    if ($content -is [byte[]]) {
        $content = [Text.Encoding]::UTF8.GetString($content)
    }
    if ("$($Response.Headers['Content-Type'])" -like 'text/event-stream*') {
        $data = $content -split "`n" | Where-Object { $_ -like 'data:*' } | ForEach-Object { $_.Substring(5).Trim() } | Where-Object { $_ }
        return ($data | Select-Object -Last 1 | ConvertFrom-Json)
    }
    if ([string]::IsNullOrWhiteSpace($content)) {
        return $null
    }
    return ($content | ConvertFrom-Json)
}

function Send-McpMessage([hashtable]$Body, [hashtable]$Headers, [int]$TimeoutSeconds) {
    $port = (Get-CelbridgeApp).Port
    $allHeaders = @{ 'Accept' = 'application/json, text/event-stream' }
    foreach ($key in $Headers.Keys) {
        $allHeaders[$key] = $Headers[$key]
    }
    $json = $Body | ConvertTo-Json -Depth 20 -Compress
    return Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$port/mcp" -Method Post -ContentType 'application/json' `
        -Headers $allHeaders -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec $TimeoutSeconds
}

function Get-McpSession {
    $file = Get-RunFile 'mcp_session.json'
    if (Test-Path -LiteralPath $file) {
        return Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    }

    $initialize = @{
        jsonrpc = '2.0'; id = 1; method = 'initialize'
        params = @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'agent-test'; version = '1.0' } }
    }
    $response = Send-McpMessage $initialize @{} 60
    $sessionId = "$($response.Headers['Mcp-Session-Id'])"
    $version = (Read-McpBody $response).result.protocolVersion
    $headers = @{ 'Mcp-Session-Id' = $sessionId; 'MCP-Protocol-Version' = $version }
    [void](Send-McpMessage @{ jsonrpc = '2.0'; method = 'notifications/initialized' } $headers 60)

    $session = [pscustomobject]@{ SessionId = $sessionId; ProtocolVersion = $version; NextId = 2 }
    $session | ConvertTo-Json | Set-Content -LiteralPath $file -Encoding UTF8
    return $session
}

<#
.SYNOPSIS
Calls one of the app's MCP tools and returns its result. The response opens with a session snapshot and
guides, so the tool's own answer is the last text block, returned as Result. An argument the tool does not
take fails with only the tool's name, and the reason is in the app's log.
#>
function Invoke-CelbridgeTool([Parameter(Mandatory)][string]$Name, [hashtable]$Arguments = @{}, [int]$TimeoutSeconds = 120) {
    $session = Get-McpSession
    $id = [int]$session.NextId
    $headers = @{ 'Mcp-Session-Id' = "$($session.SessionId)"; 'MCP-Protocol-Version' = "$($session.ProtocolVersion)" }
    $request = @{ jsonrpc = '2.0'; id = $id; method = 'tools/call'; params = @{ name = $Name; arguments = $Arguments } }

    $response = Send-McpMessage $request $headers $TimeoutSeconds

    $session.NextId = $id + 1
    $session | ConvertTo-Json | Set-Content -LiteralPath (Get-RunFile 'mcp_session.json') -Encoding UTF8

    $body = Read-McpBody $response
    if ($body.error) {
        throw "MCP error from $Name`: $($body.error | ConvertTo-Json -Compress -Depth 10)"
    }

    $blocks = @($body.result.content | Where-Object { $_.type -eq 'text' } | ForEach-Object { $_.text })
    $result = $null
    if ($blocks.Count -gt 0) {
        $result = $blocks[-1]
    }
    return [pscustomobject]@{ IsError = [bool]$body.result.isError; Result = $result; Blocks = $blocks }
}

<#
.SYNOPSIS
Evaluates JavaScript in a document's WebView and returns its value as JSON text. The frame defaults to the
document's content frame, such as an HTML preview. Pass 'top' for the editor page around it.
#>
function Invoke-CelbridgeEval([Parameter(Mandatory)][string]$Resource, [Parameter(Mandatory)][string]$Expression, [string]$Frame = '') {
    $arguments = @{ resource = $Resource; expression = $Expression }
    if ($Frame) {
        $arguments.frame = $Frame
    }

    $answer = Invoke-CelbridgeTool 'webview_eval' $arguments
    if ($answer.IsError) {
        throw "webview_eval failed: $($answer.Result)"
    }

    # The value comes second to last, before a block naming the frame it ran in.
    return $answer.Blocks[-2]
}

function Get-CelbridgeWindowHandle {
    $process = Get-CelbridgeProcess | Select-Object -First 1
    if (-not $process) {
        throw 'Celbridge is not running.'
    }
    return $process.MainWindowHandle
}

<#
.SYNOPSIS
Brings the Celbridge window to the front. Windows refuses a foreground change from a background process
unless a key press came first, so an Alt press and release goes ahead of it. A window already in front is
left alone, since the Alt press moves the keyboard out of WebView2's own find bar. Returns whether it worked.
#>
function Set-CelbridgeForeground {
    $handle = Get-CelbridgeWindowHandle
    if ([CelbridgeAgentTestNative]::GetForegroundWindow() -eq $handle) {
        return $true
    }
    [CelbridgeAgentTestNative]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [CelbridgeAgentTestNative]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    [void][CelbridgeAgentTestNative]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 200
    return ([CelbridgeAgentTestNative]::GetForegroundWindow() -eq $handle)
}

<#
.SYNOPSIS
Moves and sizes the Celbridge window in physical pixels, restoring it first if it is maximized or minimized.
The app can come up minimized, and computer use cannot grant the WebView2 runtime while the window is hidden.
#>
function Set-CelbridgeWindowBounds([int]$X, [int]$Y, [int]$Width, [int]$Height) {
    $handle = Get-CelbridgeWindowHandle
    if ([CelbridgeAgentTestNative]::IsZoomed($handle) -or [CelbridgeAgentTestNative]::IsIconic($handle)) {
        [void][CelbridgeAgentTestNative]::ShowWindow($handle, 9)
    }
    [void][CelbridgeAgentTestNative]::SetWindowPos($handle, [IntPtr]::Zero, $X, $Y, $Width, $Height, 0x0044)
}

<#
.SYNOPSIS
Presses a key chord at the operating system level, as a person does, for the chords app_simulate_input
refuses on Windows. Pass virtual key codes in press order, such as 0x11, 0x57 for Ctrl+W. The keys go to the
foreground window, so bring Celbridge forward first. Escape is refused: computer use takes any system-level
Escape as its own stop key, so send Escape with app_simulate_input.
#>
function Send-KeyChord([Parameter(Mandatory)][byte[]]$VirtualKeys) {
    if ($VirtualKeys -contains 0x1B) {
        throw 'Escape at the system level stops computer use. Send it with the app_simulate_input tool instead.'
    }
    foreach ($key in $VirtualKeys) {
        [CelbridgeAgentTestNative]::keybd_event($key, 0, 0, [UIntPtr]::Zero)
    }
    [array]::Reverse($VirtualKeys)
    foreach ($key in $VirtualKeys) {
        [CelbridgeAgentTestNative]::keybd_event($key, 0, 2, [UIntPtr]::Zero)
    }
}

<#
.SYNOPSIS
Finds elements of the Celbridge window through UI Automation by walking the raw tree. A search with
FindAll or FindFirst on a busy app can miss whole branches without an error, and the walk does not. The walk
stops at web page content, which is a tree of its own.
#>
function Find-CelbridgeElement([string]$AutomationId = '', [string]$ClassName = '', [string]$ControlType = '', [int]$MaxDepth = 40) {
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $root = [System.Windows.Automation.AutomationElement]::FromHandle((Get-CelbridgeWindowHandle))
    $found = New-Object System.Collections.Generic.List[object]
    $stack = New-Object System.Collections.Generic.Stack[object]
    $stack.Push(@($root, 0))

    while ($stack.Count -gt 0) {
        $item = $stack.Pop()
        $element = $item[0]
        $depth = $item[1]
        try {
            $current = $element.Current
            $isMatch = (-not $AutomationId -or $current.AutomationId -eq $AutomationId) -and
                (-not $ClassName -or $current.ClassName -eq $ClassName) -and
                (-not $ControlType -or $current.ControlType.ProgrammaticName -eq "ControlType.$ControlType")
            if ($isMatch -and ($AutomationId -or $ClassName -or $ControlType)) {
                $bounds = $current.BoundingRectangle
                $found.Add([pscustomobject]@{
                    Element = $element; Name = $current.Name; AutomationId = $current.AutomationId
                    ClassName = $current.ClassName; X = [int]$bounds.X; Y = [int]$bounds.Y
                    Width = [int]$bounds.Width; Height = [int]$bounds.Height
                })
            }
            if ($depth -lt $MaxDepth -and $current.ClassName -ne 'Chrome_RenderWidgetHostHWND') {
                $child = $walker.GetFirstChild($element)
                while ($child) {
                    $stack.Push(@($child, $depth + 1))
                    $child = $walker.GetNextSibling($child)
                }
            }
        } catch { }
    }
    return $found
}

<#
.SYNOPSIS
The screen rectangle of the WebView on screen, in physical pixels. Every open document has one, and those
behind other tabs have no size, so the largest is the one showing. A point in the page is the rectangle's
corner plus its CSS position times the page's devicePixelRatio.
#>
function Get-CelbridgeWebViewBounds {
    return Find-CelbridgeElement -ClassName 'Microsoft.UI.Xaml.Controls.WebView2' |
        Where-Object { $_.Width -gt 0 -and $_.Height -gt 0 } |
        Sort-Object { $_.Width * $_.Height } -Descending | Select-Object -First 1
}

<#
.SYNOPSIS
Saves a region of the screen as a PNG, as the screen shows it. Judge colors from this rather than from a
PrintWindow capture, which draws web page content on white whatever the screen shows.
#>
function Save-ScreenRegion([Parameter(Mandatory)][string]$Path, [int]$X, [int]$Y, [int]$Width, [int]$Height) {
    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($X, $Y, 0, 0, (New-Object System.Drawing.Size $Width, $Height))
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
    return $Path
}

<#
.SYNOPSIS
The color of one screen pixel, in physical pixels.
#>
function Get-ScreenPixel([int]$X, [int]$Y) {
    $bitmap = New-Object System.Drawing.Bitmap 1, 1
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($X, $Y, 0, 0, (New-Object System.Drawing.Size 1, 1))
        $color = $bitmap.GetPixel(0, 0)
        return [pscustomobject]@{ R = $color.R; G = $color.G; B = $color.B }
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

Export-ModuleMember -Function Use-CelbridgeRun, Write-RunLog, Get-CelbridgePackage, Get-CelbridgeLocalState,
    Get-CelbridgeSettingsPath, Get-CelbridgeProcess, Backup-CelbridgeSettings, Restore-CelbridgeSettings,
    Set-CelbridgePreviousProject, Start-Celbridge, Stop-Celbridge, Get-CelbridgeApp, Get-CelbridgeLogLines,
    Get-CelbridgeLogLineCount, Invoke-CelbridgeTool, Invoke-CelbridgeEval, Set-CelbridgeForeground,
    Set-CelbridgeWindowBounds, Send-KeyChord, Find-CelbridgeElement, Get-CelbridgeWebViewBounds,
    Save-ScreenRegion, Get-ScreenPixel
