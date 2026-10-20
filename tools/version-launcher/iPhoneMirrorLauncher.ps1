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
        Pinned = @($State.Config.Pinned)
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

# ---------------------------------------------------------------- theme
# The launcher follows the app: same light/dark choice, colour preset and
# accent as iPhoneMirror's settings (settings.fork.json wins, like in the app).

function ConvertTo-LauncherColor([string]$Hex) {
    $value = $Hex.Trim().TrimStart('#')
    return [System.Drawing.Color]::FromArgb([Convert]::ToInt32($value.Substring(0, 2), 16),
        [Convert]::ToInt32($value.Substring(2, 2), 16), [Convert]::ToInt32($value.Substring(4, 2), 16))
}

function Get-EnumSetting($Value, [string[]]$Names, [string]$Default) {
    if ($null -eq $Value) { return $Default }
    if ($Value -is [int] -or $Value -is [long]) {
        if ($Value -ge 0 -and $Value -lt $Names.Count) { return $Names[$Value] }
        return $Default
    }
    foreach ($name in $Names) { if ([string]::Equals("$Value", $name, 'OrdinalIgnoreCase')) { return $name } }
    return $Default
}

function Get-LauncherTheme {
    $appData = Join-Path $env:LOCALAPPDATA 'iPhoneMirror'
    $settings = Read-Json (Join-Path $appData 'settings.json')
    $fork = Read-Json (Join-Path $appData 'settings.fork.json')
    $mode = Get-EnumSetting $(if ($settings) { $settings.Theme }) @('System', 'Dark', 'Light') 'System'
    $preset = Get-EnumSetting $(if ($fork -and $null -ne $fork.ColorTheme) { $fork.ColorTheme } elseif ($settings) { $settings.ColorTheme }) `
        @('Default', 'Midnight', 'Nord', 'Sunset', 'Forest') 'Default'
    $accent = if ($fork -and $null -ne $fork.AccentColor) { "$($fork.AccentColor)" } elseif ($settings) { "$($settings.AccentColor)" } else { '' }
    $dark = $true
    if ($mode -eq 'Light') { $dark = $false }
    elseif ($mode -eq 'System') {
        try {
            $light = Get-ItemPropertyValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'AppsUseLightTheme'
            $dark = $light -eq 0
        } catch { }
    }
    # Same palettes as ThemePalette.cs; presets are dark-only, like in the app.
    $palettes = @{
        DefaultDark = @('1F1F1F', '2A2A2A', '333333', '1A1A1A', '3A3A3A', 'FFFFFF', 'A8A8A8', '69B1F8', '5FD39A', 'F2C661', 'FF8A8A')
        DefaultLight = @('F3F3F5', 'FFFFFF', 'EDEDF1', 'EBEBEE', 'D4D4DA', '1A1A1A', '5C5C5C', '0F6CBD', '107C41', '8A5A00', 'C42B1C')
        Midnight = @('0B1020', '121A2E', '19233C', '0D1426', '24304D', 'E8EDF9', '9AA6C4', '5B8CFF', '5FD39A', 'FFC66B', 'FF7A8A')
        Nord = @('2E3440', '3B4252', '434C5E', '2A303B', '4C566A', 'ECEFF4', 'B0B8C7', '88C0D0', 'A3BE8C', 'EBCB8B', 'D08770')
        Sunset = @('1D1418', '281B20', '33232A', '181014', '45313A', 'FBEEE8', 'C9AAA3', 'FF8A5C', '7FD1A1', 'FFD166', 'FF6B81')
        Forest = @('111A15', '18241E', '1F2D26', '0E1612', '2D3D34', 'E6F2EA', '9FBAA9', '5FD3A5', '8BDC7A', 'E9C46A', 'F28482')
    }
    $key = if (-not $dark) { 'DefaultLight' } elseif ($preset -ne 'Default') { $preset } else { 'DefaultDark' }
    $p = $palettes[$key]
    $theme = @{
        Name = $(if ($key -like 'Default*') { 'Standard' } else { $key })
        Bg = ConvertTo-LauncherColor $p[0]; Surface = ConvertTo-LauncherColor $p[1]; Raised = ConvertTo-LauncherColor $p[2]
        Sidebar = ConvertTo-LauncherColor $p[3]; Border = ConvertTo-LauncherColor $p[4]; Text = ConvertTo-LauncherColor $p[5]
        Muted = ConvertTo-LauncherColor $p[6]; Accent = ConvertTo-LauncherColor $p[7]; Ok = ConvertTo-LauncherColor $p[8]
        Warn = ConvertTo-LauncherColor $p[9]; Error = ConvertTo-LauncherColor $p[10]
    }
    if ($accent -match '^#?[0-9A-Fa-f]{6}$') { $theme.Accent = ConvertTo-LauncherColor $accent }
    elseif ($accent -eq 'windows') {
        try {
            # A DWORD above 0x7FFFFFFF comes back negative.
            $abgr = [int64](Get-ItemPropertyValue 'HKCU:\Software\Microsoft\Windows\DWM' 'AccentColor')
            if ($abgr -lt 0) { $abgr += 4294967296 }
            $theme.Accent = [System.Drawing.Color]::FromArgb([int]($abgr -band 0xFF), [int](($abgr -shr 8) -band 0xFF), [int](($abgr -shr 16) -band 0xFF))
        } catch { }
    }
    $luminance = (0.2126 * $theme.Accent.R + 0.7152 * $theme.Accent.G + 0.0722 * $theme.Accent.B) / 255
    $theme.OnAccent = if ($luminance -gt 0.55) { [System.Drawing.Color]::FromArgb(11, 11, 11) } else { [System.Drawing.Color]::White }
    return $theme
}

$Theme = Get-LauncherTheme

function New-RoundedPath([System.Drawing.RectangleF]$Rect, [float]$Radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($Radius * 2, [Math]::Min($Rect.Width, $Rect.Height))
    $path.AddArc($Rect.X, $Rect.Y, $d, $d, 180, 90)
    $path.AddArc($Rect.Right - $d, $Rect.Y, $d, $d, 270, 90)
    $path.AddArc($Rect.Right - $d, $Rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($Rect.X, $Rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Get-Pinned {
    if ($State.Config -and $State.Config.Pinned) { return @($State.Config.Pinned) }
    return @('main')
}

function Switch-Pin([string]$Branch) {
    $pinned = @(Get-Pinned)
    if ($pinned -contains $Branch) { $pinned = @($pinned | Where-Object { $_ -ne $Branch }) }
    else { $pinned += $Branch }
    $State.Config.Pinned = $pinned
    Save-Config
    Update-List
}

function Get-StatusKind($Branch) {
    $current = Join-Path (Get-BranchDir $Branch.Name) 'current'
    if ((Test-Path -LiteralPath $current) -and (Get-ProcessesUnder $current).Count -gt 0) { return 'Running' }
    $text = Get-StatusText $Branch
    if ($text -eq 'Aktuell') { return 'Built' }
    if ($text -like '*fehlgeschlagen*') { return 'Failed' }
    if ($text -like 'Noch nicht gebaut*') { return 'None' }
    return 'Stale'
}

# ---------------------------------------------------------------- UI

$form = New-Object System.Windows.Forms.Form
$form.Text = 'iPhoneMirror: Version wählen'
$form.Size = New-Object System.Drawing.Size(900, 640)
$form.MinimumSize = New-Object System.Drawing.Size(680, 420)
$form.StartPosition = 'CenterScreen'
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9.5)
$form.BackColor = $Theme.Bg
$form.ForeColor = $Theme.Text
$iconFile = @((Join-Path $InstallDir 'iPhoneMirror.ico'), (Join-Path $PSScriptRoot '..\..\src\App\Assets\iPhoneMirror.ico')) |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($iconFile) { try { $form.Icon = New-Object System.Drawing.Icon($iconFile) } catch { } }

$headerPanel = New-Object System.Windows.Forms.Panel
$headerPanel.Dock = 'Top'
$headerPanel.Height = 66
$headerPanel.BackColor = $Theme.Sidebar
$headerTitle = New-Object System.Windows.Forms.Label
$headerTitle.Text = 'iPhoneMirror Versionen'
$headerTitle.Font = New-Object System.Drawing.Font('Segoe UI Semibold', 14)
$headerTitle.ForeColor = $Theme.Text
$headerTitle.AutoSize = $true
$headerTitle.Location = New-Object System.Drawing.Point(20, 10)
$headerPanel.Controls.Add($headerTitle)
$header = New-Object System.Windows.Forms.Label
$header.ForeColor = $Theme.Muted
$header.AutoSize = $false
$header.AutoEllipsis = $true
$header.SetBounds(21, 40, 840, 20)
$header.Anchor = 'Top, Left, Right'
$headerPanel.Controls.Add($header)

# One owner-drawn list: group headings and a card per branch.
$list = New-Object System.Windows.Forms.ListBox
$list.Dock = 'Fill'
$list.BorderStyle = 'None'
$list.BackColor = $Theme.Bg
$list.ForeColor = $Theme.Text
$list.DrawMode = 'OwnerDrawVariable'
$list.IntegralHeight = $false
$listHost = New-Object System.Windows.Forms.Panel
$listHost.Dock = 'Fill'
$listHost.Padding = New-Object System.Windows.Forms.Padding(16, 10, 16, 6)
$listHost.BackColor = $Theme.Bg
$listHost.Controls.Add($list)

$titleFont = New-Object System.Drawing.Font('Segoe UI Semibold', 11)
$smallFont = New-Object System.Drawing.Font('Segoe UI', 8.5)
$headingFont = New-Object System.Drawing.Font('Segoe UI Semibold', 8.5)
$monoFont = New-Object System.Drawing.Font('Consolas', 9)

$list.Add_MeasureItem({
    param($sender, $e)
    if ($e.Index -lt 0 -or $e.Index -ge $list.Items.Count) { return }
    $e.ItemHeight = if ($list.Items[$e.Index].Kind -eq 'Heading') { 30 } else { 84 }
})

$list.Add_DrawItem({
    param($sender, $e)
    if ($e.Index -lt 0 -or $e.Index -ge $list.Items.Count) { return }
    $g = $e.Graphics
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'ClearTypeGridFit'
    $bgBrush = New-Object System.Drawing.SolidBrush($Theme.Bg)
    $g.FillRectangle($bgBrush, $e.Bounds)
    $item = $list.Items[$e.Index]
    if ($item.Kind -eq 'Heading') {
        $mutedBrush = New-Object System.Drawing.SolidBrush($Theme.Muted)
        $g.DrawString($item.Text.ToUpperInvariant(), $headingFont, $mutedBrush, $e.Bounds.X + 2, $e.Bounds.Y + 10)
        return
    }
    $branch = $item.Branch
    $selected = ($e.State -band [System.Windows.Forms.DrawItemState]::Selected) -ne 0
    $card = New-Object System.Drawing.RectangleF(($e.Bounds.X + 1), ($e.Bounds.Y + 3), ($e.Bounds.Width - 3), ($e.Bounds.Height - 7))
    $path = New-RoundedPath $card 10
    $fill = if ($selected) { $Theme.Raised } else { $Theme.Surface }
    $g.FillPath((New-Object System.Drawing.SolidBrush($fill)), $path)
    $pen = if ($selected) { New-Object System.Drawing.Pen($Theme.Accent, 2) } else { New-Object System.Drawing.Pen($Theme.Border, 1) }
    $g.DrawPath($pen, $path)

    # Pin star on the left; a click on it toggles the pin.
    $starColor = if ($item.Pinned) { $Theme.Warn } else { $Theme.Muted }
    $g.DrawString($(if ($item.Pinned) { [string][char]0x2605 } else { [string][char]0x2606 }),
        (New-Object System.Drawing.Font('Segoe UI Symbol', 14)), (New-Object System.Drawing.SolidBrush($starColor)),
        $card.X + 12, $card.Y + 24)

    $x = $card.X + 46
    $textBrush = New-Object System.Drawing.SolidBrush($Theme.Text)
    $g.DrawString($branch.Name, $titleFont, $textBrush, $x, $card.Y + 9)
    $nameWidth = $g.MeasureString($branch.Name, $titleFont).Width

    $kinds = @{
        Running = @('Läuft', $Theme.Ok); Built = @('Gebaut, aktuell', $Theme.Accent)
        Stale = @('Veraltet', $Theme.Warn); Failed = @('Build fehlgeschlagen', $Theme.Error); None = @('Nicht gebaut', $Theme.Muted)
    }
    $badge = $kinds[$item.StatusKind]
    $badgeWidth = $g.MeasureString($badge[0], $smallFont).Width + 24
    $badgeRect = New-Object System.Drawing.RectangleF(($x + $nameWidth + 8), ($card.Y + 11), $badgeWidth, 20)
    $badgeFill = [System.Drawing.Color]::FromArgb(40, $badge[1].R, $badge[1].G, $badge[1].B)
    $g.FillPath((New-Object System.Drawing.SolidBrush($badgeFill)), (New-RoundedPath $badgeRect 10))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush($badge[1])), $badgeRect.X + 9, $badgeRect.Y + 7, 6, 6)
    $g.DrawString($badge[0], $smallFont, (New-Object System.Drawing.SolidBrush($badge[1])), $badgeRect.X + 17, $badgeRect.Y + 3)

    $info = $item.Info
    $hash = $branch.Sha.Substring(0, 7)
    $g.DrawString($hash, $monoFont, (New-Object System.Drawing.SolidBrush($Theme.Accent)), $x, $card.Y + 36)
    $subjectX = $x + $g.MeasureString($hash, $monoFont).Width + 6
    $subjectRect = New-Object System.Drawing.RectangleF($subjectX, ($card.Y + 35), ($card.Right - $subjectX - 12), 20)
    $format = New-Object System.Drawing.StringFormat
    $format.Trimming = 'EllipsisCharacter'
    $format.FormatFlags = 'NoWrap'
    $g.DrawString($branch.Subject, $form.Font, $textBrush, $subjectRect, $format)

    $meta = "Stand GitHub: $($branch.Date)"
    if ($info -and $info.Sha) {
        $meta += "   ·   gebaut: $($info.Sha.Substring(0, 7))"
        if ($info.PSObject.Properties.Name -contains 'BuiltAt' -and $info.BuiltAt) {
            try { $meta += ' am ' + ([datetime]$info.BuiltAt).ToString('dd.MM.yyyy HH:mm') } catch { }
        }
    }
    $g.DrawString($meta, $smallFont, (New-Object System.Drawing.SolidBrush($Theme.Muted)), $x, $card.Y + 58)
})

$bottom = New-Object System.Windows.Forms.Panel
$bottom.Dock = 'Bottom'
$bottom.BackColor = $Theme.Sidebar
# Final width before children are placed, so right anchors keep their margin.
$bottom.Size = New-Object System.Drawing.Size($form.ClientSize.Width, 96)

$status = New-Object System.Windows.Forms.Label
$status.SetBounds(16, 8, $bottom.Width - 32, 20)
$status.Anchor = 'Top, Left, Right'
$status.AutoEllipsis = $true
$status.ForeColor = $Theme.Muted
$bottom.Controls.Add($status)

$progress = New-Object System.Windows.Forms.ProgressBar
$progress.SetBounds(16, 30, $bottom.Width - 32, 6)
$progress.Anchor = 'Top, Left, Right'
$progress.Style = 'Marquee'
$progress.Visible = $false
$bottom.Controls.Add($progress)

function New-Button([string]$Text, [int]$X, [string]$Anchor = 'Bottom, Left', [bool]$Primary = $false) {
    $button = New-Object System.Windows.Forms.Button
    $button.Text = $Text
    $button.SetBounds($X, 48, 124, 34)
    $button.Anchor = $Anchor
    $button.FlatStyle = 'Flat'
    $button.Cursor = [System.Windows.Forms.Cursors]::Hand
    if ($Primary) {
        $button.BackColor = $Theme.Accent
        $button.ForeColor = $Theme.OnAccent
        $button.FlatAppearance.BorderSize = 0
        $button.Font = New-Object System.Drawing.Font('Segoe UI Semibold', 9.5)
    }
    else {
        $button.BackColor = $Theme.Surface
        $button.ForeColor = $Theme.Text
        $button.FlatAppearance.BorderColor = $Theme.Border
    }
    $bottom.Controls.Add($button)
    return $button
}
$startButton = New-Button 'Starten' 16 'Bottom, Left' $true
$updateButton = New-Button 'Update' 148
$rebuildButton = New-Button 'Neu bauen' 280
$logButton = New-Button 'Log öffnen' 412
$closeButton = New-Button 'Schliessen' ($bottom.Width - 140) 'Bottom, Right'
$form.AcceptButton = $startButton
$form.CancelButton = $closeButton

$form.Controls.Add($listHost)
$form.Controls.Add($bottom)
$form.Controls.Add($headerPanel)
$listHost.BringToFront()

function Get-SelectedBranch {
    $item = $list.SelectedItem
    if (-not $item -or $item.Kind -ne 'Branch') { return $null }
    return $item.Branch
}

function Update-List {
    $selected = Get-SelectedBranch
    $keep = if ($selected) { $selected.Name } else { $State.Config.LastBranch }
    $State.Branches = Get-RemoteBranches
    $pinned = @(Get-Pinned)
    $cards = foreach ($branch in $State.Branches) {
        [pscustomobject]@{
            Kind = 'Branch'; Branch = $branch; Pinned = ($pinned -contains $branch.Name)
            Info = (Get-BuildInfo $branch.Name); StatusKind = (Get-StatusKind $branch); Text = $branch.Name
        }
    }
    $list.BeginUpdate()
    $list.Items.Clear()
    foreach ($group in @(@('Angeheftet', $true), @('Weitere Branches', $false))) {
        $members = @($cards | Where-Object { $_.Pinned -eq $group[1] })
        if ($members.Count -eq 0) { continue }
        [void]$list.Items.Add([pscustomobject]@{ Kind = 'Heading'; Text = $group[0] })
        foreach ($card in $members) { [void]$list.Items.Add($card) }
    }
    $list.SelectedIndex = -1
    for ($i = 0; $i -lt $list.Items.Count; $i++) {
        if ($list.Items[$i].Kind -eq 'Branch' -and $list.Items[$i].Branch.Name -eq $keep) { $list.SelectedIndex = $i; break }
    }
    if ($list.SelectedIndex -lt 0) {
        for ($i = 0; $i -lt $list.Items.Count; $i++) { if ($list.Items[$i].Kind -eq 'Branch') { $list.SelectedIndex = $i; break } }
    }
    $list.EndUpdate()
    $devBranch = (Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD')).Text
    $header.Text = "Dev-Repo: $($State.Repo)   ·   ausgecheckt: $devBranch   ·   Theme wie die App ($($Theme.Name))"
}

$list.Add_Resize({ $list.Invalidate() })

# Headings are not selectable; the star toggles the pin.
$list.Add_SelectedIndexChanged({
    $item = $list.SelectedItem
    if ($item -and $item.Kind -eq 'Heading' -and $list.SelectedIndex + 1 -lt $list.Items.Count) { $list.SelectedIndex++ }
    $list.Invalidate()
})
$list.Add_MouseDown({
    param($sender, $e)
    $index = $list.IndexFromPoint($e.Location)
    if ($index -lt 0) { return }
    $item = $list.Items[$index]
    if ($item.Kind -eq 'Branch' -and $e.X -lt 46) { Switch-Pin $item.Branch.Name }
})

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
$State.Config = [pscustomobject]@{
    LastBranch = $(if ($saved) { $saved.LastBranch } else { $null })
    Pinned = $(if ($saved -and $saved.Pinned) { @($saved.Pinned) } else { @('main') })
}
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

# Started from a hidden PowerShell, the window would otherwise open behind others.
$form.Add_Shown({ $form.TopMost = $true; $form.Activate(); $form.TopMost = $false })
$timer.Start()
[void]$form.ShowDialog()
$timer.Stop()
