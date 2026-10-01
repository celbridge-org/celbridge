# Celbridge passes this script on PowerShell's command line, not as a file, because an execution policy can block
# an unsigned script. The copy in the project's console folder is only for reference.
#
# The functions below are the start-up's steps, and the end of the script runs them in order. Every name the
# start-up defines begins with Celbridge, so none clashes with the user's own. The first prompt removes them all.

# Saves Celbridge's values and the start-up settings before any profile runs. Then removes the start-up
# variables, so the profiles and the console's command never see them.
function CelbridgeSaveStartup {
    $global:CelbridgeSnapshot = @{}
    foreach ($celbridgeName in ("$env:CELBRIDGE_CONSOLE_RESTORE" -split ' ' | Where-Object { $_ })) {
        $global:CelbridgeSnapshot[$celbridgeName] = [Environment]::GetEnvironmentVariable($celbridgeName)
    }

    $global:CelbridgeStartup = @{
        PathFolders = @("$env:CELBRIDGE_CONSOLE_PATH_FOLDERS" -split ';' | Where-Object { $_ })
        UseShellProfile = [bool]$env:CELBRIDGE_CONSOLE_USE_SHELL_PROFILE
        CompactPrompt = [bool]$env:CELBRIDGE_CONSOLE_COMPACT_PROMPT
        History = $env:CELBRIDGE_CONSOLE_HISTORY
        WorkingFolder = $env:CELBRIDGE_CONSOLE_WORKING_FOLDER
        Command = @(if ($env:CELBRIDGE_CONSOLE_COMMAND) { $env:CELBRIDGE_CONSOLE_COMMAND -split "`n" })
    }

    foreach ($celbridgeVariable in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'CELBRIDGE_CONSOLE_*' })) {
        Remove-Item -LiteralPath "Env:$($celbridgeVariable.Name)"
    }
}

# Whether the restore removes a variable: every uv and Python setting except uv's index and network settings.
# -like ignores case, as Windows does for variable names.
function CelbridgeIsRemoved($celbridgeName) {
    foreach ($celbridgePattern in @({{KEPT_PATTERNS}})) {
        if ($celbridgeName -like $celbridgePattern) {
            return $false
        }
    }

    foreach ($celbridgePattern in @({{REMOVED_PATTERNS}})) {
        if ($celbridgeName -like $celbridgePattern) {
            return $true
        }
    }

    return $false
}

# Restores Celbridge's settings. Removes the uv and Python settings the profiles added, puts back the saved
# values, and moves Celbridge's folders to the front of PATH. Also removes any alias or function that hides one
# of Celbridge's commands.
function CelbridgeRestore {
    foreach ($celbridgeVariable in @(Get-ChildItem Env:)) {
        if (CelbridgeIsRemoved $celbridgeVariable.Name) {
            Remove-Item -LiteralPath "Env:$($celbridgeVariable.Name)"
        }
    }

    foreach ($celbridgeEntry in $global:CelbridgeSnapshot.GetEnumerator()) {
        Set-Item -LiteralPath "Env:$($celbridgeEntry.Key)" -Value $celbridgeEntry.Value
    }

    $celbridgeFolders = $global:CelbridgeStartup.PathFolders
    $celbridgeRest = @("$env:Path" -split ';' | Where-Object { $_ -and $celbridgeFolders -notcontains $_ })
    $env:Path = ($celbridgeFolders + $celbridgeRest) -join ';'

    foreach ($celbridgeCommand in @({{COMMAND_NAMES}})) {
        foreach ($celbridgeDrive in @('Alias', 'Function')) {
            # A command can be defined in several scopes. Each removal deletes only the nearest one.
            for ($celbridgeAttempt = 0; $celbridgeAttempt -lt 3; $celbridgeAttempt++) {
                if (-not (Test-Path -LiteralPath "$($celbridgeDrive):$celbridgeCommand")) {
                    break
                }
                Remove-Item -LiteralPath "$($celbridgeDrive):$celbridgeCommand" -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

# Returns the prompt for every prompt after the first. The compact prompt shows only the folder's name, not the
# full path. When it is off, the prompt the profiles set is kept.
function CelbridgeChoosePrompt {
    if (-not $global:CelbridgeStartup.CompactPrompt) {
        return $function:prompt
    }

    return {
        $celbridgePath = $executionContext.SessionState.Path.CurrentLocation.Path
        $celbridgeFolder = if ($celbridgePath -eq $HOME) { '~' } else { Split-Path -Leaf $celbridgePath }
        if (-not $celbridgeFolder) {
            $celbridgeFolder = $celbridgePath
        }
        "PS $celbridgeFolder$('>' * ($nestedPromptLevel + 1)) "
    }
}

# Runs as the first prompt. The chosen prompt runs first, because a tool's prompt hook can change the
# environment, and then the settings are restored again. The chosen prompt then takes over, and everything the
# start-up defined is removed. The names are listed one by one, so none of the user's own is removed.
function CelbridgeFirstPrompt {
    $celbridgeText = & $global:CelbridgePrompt
    CelbridgeRestore
    $function:global:prompt = $global:CelbridgePrompt

    $celbridgeFunctions = @(
        'CelbridgeSaveStartup'
        'CelbridgeIsRemoved'
        'CelbridgeRestore'
        'CelbridgeChoosePrompt'
        'CelbridgeFirstPrompt'
        'CelbridgeQuoteArgument'
        'CelbridgeRunCommand'
    )
    foreach ($celbridgeFunction in $celbridgeFunctions) {
        Remove-Item -LiteralPath "Function:$celbridgeFunction" -ErrorAction SilentlyContinue
    }
    $celbridgeVariables = @('CelbridgeSnapshot', 'CelbridgeStartup', 'CelbridgePrompt', 'celbridgeProfile')
    Remove-Variable -Name $celbridgeVariables -Scope Global -ErrorAction SilentlyContinue

    $celbridgeText
}

# Quotes an argument the way Windows splits a command line. An argument with no space or quote is left as it is.
# Inside quotes, backslashes are doubled where they come before a quote, including the closing one.
function CelbridgeQuoteArgument($celbridgeArgument) {
    if ($celbridgeArgument -ne '' -and $celbridgeArgument -notmatch '[\s"]') {
        return $celbridgeArgument
    }

    $celbridgeEscaped = ($celbridgeArgument -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1'
    return '"' + $celbridgeEscaped + '"'
}

# Shows the console, then runs its command. The console becomes visible at the marker. The command runs last, as
# it would with -NoExit -Command.
function CelbridgeRunCommand {
    $celbridgeCommand = $global:CelbridgeStartup.Command
    if ($celbridgeCommand.Count -eq 0) {
        return
    }

    Clear-Host
    Write-Host -NoNewline ([char]0x2404)

    $celbridgeExecutable = $celbridgeCommand[0]
    $celbridgeArguments = @($celbridgeCommand | Select-Object -Skip 1)
    $celbridgeIsProgram = (Get-Command $celbridgeExecutable -ErrorAction SilentlyContinue).CommandType -eq 'Application'
    if (-not $celbridgeIsProgram -or $celbridgeArguments.Count -eq 0) {
        & $celbridgeExecutable @celbridgeArguments
        return
    }

    # Windows PowerShell does not escape the quotes in a program's arguments, and drops empty ones. So the
    # arguments are quoted here as one command line. The stop-parsing token passes that line on unchanged through
    # an environment variable, which the program also sees.
    $env:CELBRIDGE_CONSOLE_ARGUMENTS = @($celbridgeArguments | ForEach-Object { CelbridgeQuoteArgument $_ }) -join ' '
    & $celbridgeExecutable --% %CELBRIDGE_CONSOLE_ARGUMENTS%
    Remove-Item -LiteralPath Env:CELBRIDGE_CONSOLE_ARGUMENTS -ErrorAction SilentlyContinue
}

CelbridgeSaveStartup

# When the shell profile is on, load the profiles in PowerShell's usual order. They load here, at the top level,
# so what they define stays global. If one fails, report the error and keep loading the rest, as PowerShell does.
if ($global:CelbridgeStartup.UseShellProfile) {
    foreach ($celbridgeProfile in @(
            $PROFILE.AllUsersAllHosts
            $PROFILE.AllUsersCurrentHost
            $PROFILE.CurrentUserAllHosts
            $PROFILE.CurrentUserCurrentHost)) {
        if ($celbridgeProfile -and (Test-Path -LiteralPath $celbridgeProfile)) {
            try {
                . $celbridgeProfile
            }
            catch {
                Write-Error $_
            }
        }
    }
}

if ($global:CelbridgeStartup.History -and (Get-Command Set-PSReadLineOption -ErrorAction SilentlyContinue)) {
    Set-PSReadLineOption -HistorySavePath $global:CelbridgeStartup.History
}

# Celbridge sends Ctrl+U ahead of an injected command to clear any partial input. PSReadLine's default Windows
# edit mode has no binding for it, so the key would be typed into the line as a literal ^U. This is set after the
# profiles so a profile cannot unbind it.
if (Get-Command Set-PSReadLineKeyHandler -ErrorAction SilentlyContinue) {
    Set-PSReadLineKeyHandler -Chord Ctrl+u -Function BackwardDeleteLine
}

$global:CelbridgePrompt = CelbridgeChoosePrompt
$function:global:prompt = $function:CelbridgeFirstPrompt

# Go back to the working folder, since a profile may have changed it. Do this before the restore, so the restore
# undoes anything a folder change set up.
if ($global:CelbridgeStartup.WorkingFolder) {
    Set-Location -LiteralPath $global:CelbridgeStartup.WorkingFolder
}
CelbridgeRestore
CelbridgeRunCommand
