<#
.SYNOPSIS
    Personal version launcher for iPhoneMirror (Fabian's fork).

.DESCRIPTION
    Lists every branch on GitHub (origin), starts the build of the chosen one
    and updates them: "Update" fetches origin, fast-forwards the dev checkout
    when it is clean, and rebuilds every branch whose GitHub state changed.

    Each branch is built in its own detached git worktree under
    %LOCALAPPDATA%\iPMV, so the dev checkout is never touched. Builds, logs
    and settings live in %LOCALAPPDATA%\iPhoneMirrorVersions.
    A build only replaces the startable copy after it succeeded, so a broken
    branch never takes away the last working version.

    Started from the repo (Start-iPhoneMirror.cmd) the script installs a copy
    of itself into %LOCALAPPDATA%\iPhoneMirrorVersions\launcher and creates a
    desktop shortcut to that copy, so switching branches in the dev checkout
    cannot break the launcher itself.

.PARAMETER Update
    Run "Update" without showing the window (console output), then exit.
#>
param([string]$RepoPath, [switch]$Update)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$StateRoot = Join-Path $env:LOCALAPPDATA 'iPhoneMirrorVersions'
$InstallDir = Join-Path $StateRoot 'launcher'
$ConfigPath = Join-Path $StateRoot 'launcher.json'
# Kept short: deep build paths inside the worktree must stay under MAX_PATH.
$WorktreeRoot = Join-Path $env:LOCALAPPDATA 'iPMV'
$BuildRoot = Join-Path $StateRoot 'builds'
$LogRoot = Join-Path $StateRoot 'logs'
$ShortcutName = 'iPhoneMirror (Version wählen).lnk'

# Same flags as the usual local builds: UxPlay is loaded at runtime (its local
# build needs MSYS2), and the source-text logic tests are skipped.
$BuildArguments = '-Configuration Release -OmitUxPlayRuntime -SkipTests'

# Ignored, locally prepared build inputs that a fresh worktree lacks. They are
# copied from the dev checkout once; build.ps1 refreshes them where needed.
$SeedPaths = @()

# All mutable launcher state lives in one table so event handlers share it.
$State = @{
    Repo = $null
    Config = $null
    Queue = New-Object System.Collections.Queue
    Current = $null
    Busy = $false
    Summary = New-Object System.Collections.Generic.List[string]
    Branches = @()
}

function Show-Message([string]$Text, [string]$Icon = 'Information', [string]$Buttons = 'OK') {
    [System.Windows.Forms.MessageBox]::Show($Text, 'iPhoneMirror Versionen',
        [System.Windows.Forms.MessageBoxButtons]::$Buttons,
        [System.Windows.Forms.MessageBoxIcon]::$Icon)
}

function Read-Json([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try { return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return $null }
}

function Write-Json([string]$Path, $Value) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    $Value | ConvertTo-Json | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Save-Config {
    Write-Json $ConfigPath ([pscustomobject]@{
        RepoPath = $State.Repo
        LastBranch = $State.Config.LastBranch
    })
}

function Invoke-Git([string[]]$GitArgs, [string]$Directory = $State.Repo) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & git -C $Directory @GitArgs 2>&1
        return [pscustomobject]@{ Code = $LASTEXITCODE; Text = (($output | ForEach-Object { "$_" }) -join "`n").Trim() }
    }
    finally { $ErrorActionPreference = $previous }
}

function Get-SafeName([string]$Branch) { $Branch -replace '[^A-Za-z0-9._-]', '-' }
function Get-WorktreePath([string]$Branch) {
    $sha = [Security.Cryptography.SHA1]::Create()
    $hash = -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Branch))[0..3] | ForEach-Object { $_.ToString('x2') })
    Join-Path $WorktreeRoot $hash
}
function Get-BranchDir([string]$Branch) { Join-Path $BuildRoot (Get-SafeName $Branch) }

function Resolve-Repo {
    $candidates = @($RepoPath)
    if ($PSScriptRoot) { $candidates += (Join-Path $PSScriptRoot '..\..') }
    $saved = Read-Json $ConfigPath
    if ($saved -and $saved.RepoPath) { $candidates += $saved.RepoPath }
    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        $full = [IO.Path]::GetFullPath($candidate)
        if ((Test-Path -LiteralPath (Join-Path $full '.git')) -and
            (Test-Path -LiteralPath (Join-Path $full 'build.ps1') -PathType Leaf)) { return $full.TrimEnd('\') }
    }
    return $null
}

function Install-Launcher {
    # Only a run from the repo installs; the installed copy never rewrites itself.
    if (-not $PSCommandPath -or $PSCommandPath.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase)) { return }
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    $script = Join-Path $InstallDir 'iPhoneMirrorLauncher.ps1'
    Copy-Item -LiteralPath $PSCommandPath -Destination $script -Force
    $icon = Join-Path $State.Repo 'src\App\Assets\iPhoneMirror.ico'
    $installedIcon = Join-Path $InstallDir 'iPhoneMirror.ico'
    if (Test-Path -LiteralPath $icon) { Copy-Item -LiteralPath $icon -Destination $installedIcon -Force }
    try {
        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) $ShortcutName))
        $link.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $link.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`""
        $link.WorkingDirectory = $InstallDir
        $link.Description = 'iPhoneMirror-Version auswählen, starten und aktualisieren'
        if (Test-Path -LiteralPath $installedIcon) { $link.IconLocation = "$installedIcon,0" }
        $link.Save()
    }
    catch { }
}

function Get-RemoteBranches {
    $format = '--format=%(refname:short)%09%(objectname)%09%(committerdate:format:%d.%m.%Y %H:%M)%09%(subject)'
    $result = Invoke-Git @('for-each-ref', $format, 'refs/remotes/origin')
    $branches = @()
    foreach ($line in ($result.Text -split "`n")) {
        $parts = $line -split "`t", 4
        if ($parts.Count -lt 4 -or -not $parts[0].StartsWith('origin/')) { continue }
        $name = $parts[0].Substring(7)
        if ($name -eq 'HEAD') { continue }
        $branches += [pscustomobject]@{ Name = $name; Sha = $parts[1]; Date = $parts[2]; Subject = $parts[3] }
    }
    # main first, the rest alphabetically.
    return @($branches | Sort-Object @{ Expression = { $_.Name -ne 'main' } }, Name)
}

function Get-BuildInfo([string]$Branch) { Read-Json (Join-Path (Get-BranchDir $Branch) 'build.json') }

function Get-CurrentExe([string]$Branch) { Join-Path (Get-BranchDir $Branch) 'current\iPhoneMirror.exe' }

function Get-ProcessesUnder([string]$Directory) {
    $prefix = $Directory.TrimEnd('\') + '\'
    @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    })
}

function Complete-Staging([string]$Branch) {
    # Swap a finished build in. Returns $false while the old copy is running.
    $dir = Get-BranchDir $Branch
    $staging = Join-Path $dir 'staging'
    $stagingInfo = Join-Path $dir 'staging.json'
    if (-not (Test-Path -LiteralPath $stagingInfo -PathType Leaf)) { return $true }
    $current = Join-Path $dir 'current'
    if ((Get-ProcessesUnder $current).Count -gt 0) { return $false }
    $previous = Join-Path $dir 'previous'
    if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
    if (Test-Path -LiteralPath $current) { Rename-Item -LiteralPath $current -NewName 'previous' }
    Rename-Item -LiteralPath $staging -NewName 'current'
    Move-Item -LiteralPath $stagingInfo -Destination (Join-Path $dir 'build.json') -Force
    return $true
}

function Get-StatusText($Branch) {
    $info = Get-BuildInfo $Branch.Name
    $built = Test-Path -LiteralPath (Get-CurrentExe $Branch.Name) -PathType Leaf
    $failed = $info -and $info.FailedSha -eq $Branch.Sha
    if (Test-Path -LiteralPath (Join-Path (Get-BranchDir $Branch.Name) 'staging.json')) {
        return 'Neuer Build bereit, wird beim nächsten Start übernommen'
    }
    if (-not $built) {
        if ($failed) { return 'Build fehlgeschlagen, noch keine startbare Version' }
        return 'Noch nicht gebaut, bitte Update'
    }
    if ($info.Sha -eq $Branch.Sha) { return 'Aktuell' }
    if ($failed) { return 'Neuer Stand: Build fehlgeschlagen, alte Version bleibt' }
    return 'Neuer Stand auf GitHub, bitte Update'
}

# ---------------------------------------------------------------- UI

$form = New-Object System.Windows.Forms.Form
$form.Text = 'iPhoneMirror: Version wählen'
$form.Size = New-Object System.Drawing.Size(860, 460)
$form.MinimumSize = New-Object System.Drawing.Size(640, 360)
$form.StartPosition = 'CenterScreen'
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)
$iconFile = @((Join-Path $InstallDir 'iPhoneMirror.ico'), (Join-Path $PSScriptRoot '..\..\src\App\Assets\iPhoneMirror.ico')) |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($iconFile) { try { $form.Icon = New-Object System.Drawing.Icon($iconFile) } catch { } }

$header = New-Object System.Windows.Forms.Label
$header.Dock = 'Top'
$header.Height = 28
$header.Padding = New-Object System.Windows.Forms.Padding(8, 8, 8, 0)
$form.Controls.Add($header)

$list = New-Object System.Windows.Forms.ListView
$list.View = 'Details'
$list.FullRowSelect = $true
$list.MultiSelect = $false
$list.HideSelection = $false
$list.Dock = 'Fill'
[void]$list.Columns.Add('Branch', 160)
[void]$list.Columns.Add('Gebauter Stand', 380)
[void]$list.Columns.Add('Status', 290)

$bottom = New-Object System.Windows.Forms.Panel
$bottom.Dock = 'Bottom'
# Final width before children are placed, so right anchors keep their margin.
$bottom.Size = New-Object System.Drawing.Size($form.ClientSize.Width, 92)

$status = New-Object System.Windows.Forms.Label
$status.SetBounds(10, 6, $bottom.Width - 20, 20)
$status.Anchor = 'Top, Left, Right'
$status.AutoEllipsis = $true
$bottom.Controls.Add($status)

$progress = New-Object System.Windows.Forms.ProgressBar
$progress.SetBounds(10, 28, $bottom.Width - 20, 8)
$progress.Anchor = 'Top, Left, Right'
$progress.Style = 'Marquee'
$progress.Visible = $false
$bottom.Controls.Add($progress)

function New-Button([string]$Text, [int]$X, [string]$Anchor = 'Bottom, Left') {
    $button = New-Object System.Windows.Forms.Button
    $button.Text = $Text
    $button.SetBounds($X, 46, 120, 32)
    $button.Anchor = $Anchor
    $bottom.Controls.Add($button)
    return $button
}
$startButton = New-Button 'Starten' 10
$updateButton = New-Button 'Update' 140
$rebuildButton = New-Button 'Neu bauen' 270
$logButton = New-Button 'Log öffnen' 400
$closeButton = New-Button 'Schliessen' ($bottom.Width - 130) 'Bottom, Right'
$form.AcceptButton = $startButton
$form.CancelButton = $closeButton

$form.Controls.Add($list)
$form.Controls.Add($bottom)
$list.BringToFront()

function Get-SelectedBranch {
    if ($list.SelectedItems.Count -eq 0) { return $null }
    return $list.SelectedItems[0].Tag
}

function Update-List {
    $selected = Get-SelectedBranch
    $keep = if ($selected) { $selected.Name } else { $State.Config.LastBranch }
    $State.Branches = Get-RemoteBranches
    $list.BeginUpdate()
    $list.Items.Clear()
    foreach ($branch in $State.Branches) {
        $info = Get-BuildInfo $branch.Name
        $builtText = if ((Test-Path -LiteralPath (Get-CurrentExe $branch.Name)) -and $info) {
            "$($info.Sha.Substring(0, 7))  $($info.Date)  $($info.Subject)"
        } else { '(noch nichts gebaut)' }
        $item = New-Object System.Windows.Forms.ListViewItem($branch.Name)
        [void]$item.SubItems.Add($builtText)
        [void]$item.SubItems.Add((Get-StatusText $branch))
        $item.Tag = $branch
        [void]$list.Items.Add($item)
        if ($branch.Name -eq $keep) { $item.Selected = $true }
    }
    if ($list.SelectedItems.Count -eq 0 -and $list.Items.Count -gt 0) { $list.Items[0].Selected = $true }
    $list.EndUpdate()
    $devBranch = (Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD')).Text
    $header.Text = "Dev-Repo: $($State.Repo)  (ausgecheckt: $devBranch)"
}

function Set-Busy([bool]$Busy) {
    $State.Busy = $Busy
    $progress.Visible = $Busy
    foreach ($button in @($updateButton, $rebuildButton)) { $button.Enabled = -not $Busy }
}

# ---------------------------------------------------------------- step queue
# A step is a hashtable: Title, Run (returns a Process or $null), Done (gets
# the step and exit code). Steps run one after another on the UI thread timer.

function Start-LoggedProcess([string]$CommandLine, [string]$Directory, [string]$Log) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Log) | Out-Null
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $env:SystemRoot 'System32\cmd.exe'
    $info.Arguments = "/d /s /c `"$CommandLine >> `"$Log`" 2>&1`""
    $info.WorkingDirectory = $Directory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
    return [System.Diagnostics.Process]::Start($info)
}

function Add-Step([hashtable]$Step) { $State.Queue.Enqueue($Step) }

function Invoke-NextStep {
    while (-not $State.Current -and $State.Queue.Count -gt 0) {
        $step = $State.Queue.Dequeue()
        $status.Text = $step.Title
        Set-Busy $true
        try {
            $process = & $step.Run $step
            if ($process) {
                $step.Process = $process
                $State.Current = $step
            }
            elseif ($step.Done) { & $step.Done $step 0 }
        }
        catch { $State.Summary.Add("$($step.Title): $($_.Exception.Message)") }
    }
    if (-not $State.Current -and $State.Queue.Count -eq 0 -and $State.Busy) {
        Set-Busy $false
        Update-List
        $status.Text = if ($State.Summary.Count -gt 0) { $State.Summary -join '  |  ' } else { 'Fertig.' }
        $State.Summary.Clear()
    }
}

function Get-LogTail([string]$Log) {
    try {
        $stream = [IO.File]::Open($Log, 'Open', 'Read', 'ReadWrite')
        try {
            $length = [Math]::Min(4096, $stream.Length)
            [void]$stream.Seek(-$length, 'End')
            $buffer = New-Object byte[] $length
            [void]$stream.Read($buffer, 0, $length)
            $lines = [Text.Encoding]::UTF8.GetString($buffer) -split "`r?`n" | Where-Object { $_.Trim() }
            return @($lines)[-1]
        }
        finally { $stream.Dispose() }
    }
    catch { return '' }
}

function Invoke-Tick {
    $step = $State.Current
    if (-not $step) { return }
    if ($step.Log) {
        $tail = Get-LogTail $step.Log
        if ($tail) { $status.Text = "$($step.Title)  ·  $tail" }
    }
    if (-not $step.Process.HasExited) { return }
    $code = $step.Process.ExitCode
    $State.Current = $null
    try { if ($step.Done) { & $step.Done $step $code } }
    catch { $State.Summary.Add("$($step.Title): $($_.Exception.Message)") }
    Invoke-NextStep
}

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 500
$timer.Add_Tick({ Invoke-Tick })

function Add-FetchStep {
    Add-Step @{
        Title = 'Hole neue Stände von GitHub (git fetch) ...'
        Log = Join-Path $LogRoot 'git.log'
        Run = { param($s) Start-LoggedProcess 'git fetch origin --prune' $State.Repo $s.Log }
        Done = { param($s, $code) if ($code -ne 0) { $State.Summary.Add('git fetch fehlgeschlagen (offline?), siehe logs\git.log') } }
    }
}

function Add-DevPullStep {
    Add-Step @{
        Title = 'Aktualisiere Dev-Checkout (git pull) ...'
        Run = {
            param($s)
            $dirty = (Invoke-Git @('status', '--porcelain')).Text
            $upstream = Invoke-Git @('rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{u}')
            if ($dirty) { $State.Summary.Add('Dev-Checkout nicht gepullt: lokale Änderungen'); return $null }
            if ($upstream.Code -ne 0) { $State.Summary.Add('Dev-Checkout nicht gepullt: Branch ohne Upstream'); return $null }
            $merge = Invoke-Git @('merge', '--ff-only', '@{u}')
            if ($merge.Code -ne 0) { $State.Summary.Add('Dev-Checkout nicht gepullt: kein Fast-Forward möglich') }
            elseif ($merge.Text -notmatch 'Already up to date|Bereits aktuell') { $State.Summary.Add('Dev-Checkout gepullt') }
            return $null
        }
    }
}

function Copy-BuildPrerequisites([string]$Worktree) {
    foreach ($relative in $SeedPaths) {
        $source = Join-Path $State.Repo $relative
        $target = Join-Path $Worktree $relative
        if ((Test-Path -LiteralPath $source) -and -not (Test-Path -LiteralPath $target)) {
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
            if (Test-Path -LiteralPath $source -PathType Container) {
                robocopy $source $target /E /NFL /NDL /NJH /NJS /NP | Out-Null
            }
            else { Copy-Item -LiteralPath $source -Destination $target -Force }
        }
    }
}

function Add-BuildSteps($Branch) {
    $log = Join-Path $LogRoot ("{0}-{1}.log" -f (Get-SafeName $Branch.Name), (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $worktree = Get-WorktreePath $Branch.Name
    Add-Step @{
        Title = "[$($Branch.Name)] Worktree vorbereiten ..."
        Log = $log
        Branch = $Branch
        Worktree = $worktree
        Run = {
            param($s)
            if (Test-Path -LiteralPath (Join-Path $s.Worktree '.git')) {
                $command = "git checkout --force --detach $($s.Branch.Sha)"
                return Start-LoggedProcess $command $s.Worktree $s.Log
            }
            [void](Invoke-Git @('worktree', 'prune'))
            New-Item -ItemType Directory -Force -Path $WorktreeRoot | Out-Null
            return Start-LoggedProcess "git worktree add --detach `"$($s.Worktree)`" $($s.Branch.Sha)" $State.Repo $s.Log
        }
        Done = {
            param($s, $code)
            if ($code -ne 0) { throw "git checkout fehlgeschlagen ($code)" }
            Copy-BuildPrerequisites $s.Worktree
            $build = @{
                Title = "[$($s.Branch.Name)] Baue ... (kann einige Minuten dauern)"
                Log = $s.Log
                Branch = $s.Branch
                Worktree = $s.Worktree
                Run = {
                    param($b)
                    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
                    Start-LoggedProcess "`"$powershell`" -NoProfile -ExecutionPolicy Bypass -File `"$($b.Worktree)\build.ps1`" $BuildArguments" $b.Worktree $b.Log
                }
                Done = { param($b, $buildCode) Complete-Build $b $buildCode }
            }
            # Run the build next, ahead of other branches already queued.
            $rest = $State.Queue.ToArray()
            $State.Queue.Clear()
            $State.Queue.Enqueue($build)
            foreach ($item in $rest) { $State.Queue.Enqueue($item) }
        }
    }
}

function Complete-Build($Step, [int]$Code) {
    $branch = $Step.Branch
    $dir = Get-BranchDir $branch.Name
    $output = Join-Path $Step.Worktree 'outputs\iPhoneMirror'
    $info = Get-BuildInfo $branch.Name
    if ($Code -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $output 'iPhoneMirror.exe') -PathType Leaf)) {
        $failed = if ($info) { $info } else { [pscustomobject]@{} }
        foreach ($name in 'FailedSha', 'FailedAt', 'FailedLog') {
            if (-not ($failed.PSObject.Properties.Name -contains $name)) { $failed | Add-Member -NotePropertyName $name -NotePropertyValue $null }
        }
        $failed.FailedSha = $branch.Sha
        $failed.FailedAt = (Get-Date).ToString('s')
        $failed.FailedLog = $Step.Log
        Write-Json (Join-Path $dir 'build.json') $failed
        $State.Summary.Add("$($branch.Name): Build fehlgeschlagen, alte Version bleibt (Log öffnen)")
        return
    }
    $staging = Join-Path $dir 'staging'
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    robocopy $output $staging /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Kopieren der Build-Ausgabe fehlgeschlagen ($LASTEXITCODE)" }
    Write-Json (Join-Path $dir 'staging.json') ([pscustomobject]@{
        Branch = $branch.Name; Sha = $branch.Sha; Date = $branch.Date; Subject = $branch.Subject
        BuiltAt = (Get-Date).ToString('s'); Log = $Step.Log
        FailedSha = $null; FailedAt = $null; FailedLog = $null
    })
    if (Complete-Staging $branch.Name) { $State.Summary.Add("$($branch.Name): neu gebaut") }
    else { $State.Summary.Add("$($branch.Name): neu gebaut, wird nach dem Beenden der laufenden App übernommen") }
}

function Start-Update([bool]$Force) {
    if ($State.Current -or $State.Queue.Count -gt 0) { return }
    $selected = Get-SelectedBranch
    Add-FetchStep
    if (-not $Force) { Add-DevPullStep }
    Add-Step @{
        Title = 'Prüfe, welche Versionen neu gebaut werden müssen ...'
        Force = $Force
        Selected = if ($selected) { $selected.Name } else { $null }
        Run = {
            param($s)
            $State.Branches = Get-RemoteBranches
            $todo = @()
            foreach ($branch in $State.Branches) {
                if ($s.Force) { if ($branch.Name -eq $s.Selected) { $todo += $branch }; continue }
                $info = Get-BuildInfo $branch.Name
                $built = (Test-Path -LiteralPath (Get-CurrentExe $branch.Name)) -and $info -and $info.Sha -eq $branch.Sha
                $knownBroken = $info -and $info.FailedSha -eq $branch.Sha
                $pending = Read-Json (Join-Path (Get-BranchDir $branch.Name) 'staging.json')
                $waiting = $pending -and $pending.Sha -eq $branch.Sha
                if (-not $built -and -not $knownBroken -and -not $waiting) { $todo += $branch }
            }
            if ($todo.Count -eq 0) { $State.Summary.Add('Alle Versionen sind aktuell') }
            foreach ($branch in $todo) { Add-BuildSteps $branch }
            return $null
        }
    }
    Invoke-NextStep
}

function Start-SelectedVersion {
    $branch = Get-SelectedBranch
    if (-not $branch) { return }
    [void](Complete-Staging $branch.Name)
    $exe = Get-CurrentExe $branch.Name
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        [void](Show-Message "Für '$($branch.Name)' gibt es noch keine gebaute Version. Bitte zuerst Update ausführen." 'Warning')
        return
    }
    $running = @(Get-Process -Name 'iPhoneMirror' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        $where = ($running | ForEach-Object { try { $_.Path } catch { '?' } }) -join "`n"
        $answer = Show-Message "iPhoneMirror läuft bereits (nur eine Instanz möglich):`n$where`n`nLaufende Instanz beenden und '$($branch.Name)' starten?" 'Question' 'YesNo'
        if ($answer -ne 'Yes') { return }
        foreach ($process in $running) { [void]$process.CloseMainWindow() }
        foreach ($process in $running) { [void]$process.WaitForExit(10000) }
        $still = @($running | Where-Object { -not $_.HasExited })
        if ($still.Count -gt 0) {
            $answer = Show-Message 'Die laufende Instanz hat sich nicht beendet (evtl. ausgeblendet oder im Tray). Hart beenden?' 'Warning' 'YesNo'
            if ($answer -ne 'Yes') { return }
            $still | Stop-Process -Force
            Start-Sleep -Milliseconds 800
        }
        [void](Complete-Staging $branch.Name)
    }
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
    $State.Config.LastBranch = $branch.Name
    Save-Config
    $form.Close()
}

function Open-SelectedLog {
    $branch = Get-SelectedBranch
    $log = $null
    if ($State.Current -and $State.Current.Log) { $log = $State.Current.Log }
    elseif ($branch) {
        $info = Get-BuildInfo $branch.Name
        if ($info -and $info.FailedSha -eq $branch.Sha -and $info.FailedLog) { $log = $info.FailedLog }
        elseif ($info -and ($info.PSObject.Properties.Name -contains 'Log') -and $info.Log) { $log = $info.Log }
    }
    if ($log -and (Test-Path -LiteralPath $log)) { Start-Process notepad.exe -ArgumentList "`"$log`"" }
    elseif (Test-Path -LiteralPath $LogRoot) { Start-Process explorer.exe -ArgumentList "`"$LogRoot`"" }
}

$startButton.Add_Click({ Start-SelectedVersion })
$list.Add_DoubleClick({ Start-SelectedVersion })
$updateButton.Add_Click({ Start-Update $false })
$rebuildButton.Add_Click({ Start-Update $true })
$logButton.Add_Click({ Open-SelectedLog })
$closeButton.Add_Click({ $form.Close() })
$form.Add_FormClosing({
    param($formSender, $closing)
    if (-not $State.Current) { return }
    $answer = Show-Message 'Es läuft gerade ein Update/Build. Abbrechen und schliessen?' 'Question' 'YesNo'
    if ($answer -ne 'Yes') { $closing.Cancel = $true; return }
    & taskkill.exe /T /F /PID $State.Current.Process.Id | Out-Null
})

# ---------------------------------------------------------------- start

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    [void](Show-Message 'git wurde nicht gefunden.' 'Error')
    return
}
$State.Repo = Resolve-Repo
if (-not $State.Repo) {
    [void](Show-Message 'Das iPhoneMirror-Repo wurde nicht gefunden. Bitte einmal Start-iPhoneMirror.cmd im Repo ausführen.' 'Error')
    return
}
$saved = Read-Json $ConfigPath
$State.Config = [pscustomobject]@{ LastBranch = $(if ($saved) { $saved.LastBranch } else { $null }) }
New-Item -ItemType Directory -Force -Path $StateRoot | Out-Null
Install-Launcher
Save-Config

if ($Update) {
    Update-List
    Start-Update $false
    $lastTitle = $null
    while ($State.Current -or $State.Queue.Count -gt 0) {
        if ($State.Current -and $State.Current.Title -ne $lastTitle) {
            $lastTitle = $State.Current.Title
            Write-Host $lastTitle
        }
        Start-Sleep -Milliseconds 500
        Invoke-Tick
    }
    Write-Host $status.Text
    return
}

Update-List
if ($State.Branches.Count -eq 0) { $status.Text = 'Keine Branches auf origin gefunden: Update holt sie von GitHub.' }
elseif (@($State.Branches | Where-Object { Test-Path -LiteralPath (Get-CurrentExe $_.Name) }).Count -eq 0) {
    $status.Text = 'Noch keine Version gebaut: Update baut alle Branches (dauert beim ersten Mal länger).'
}
else { $status.Text = 'Version wählen und Starten. Update holt neue Stände von GitHub und baut sie.' }

$timer.Start()
[void]$form.ShowDialog()
$timer.Stop()
