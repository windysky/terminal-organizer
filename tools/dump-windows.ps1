# SPEC-WIN-004 dump tool (REQ-CMP-002; B5 production scope semantics).
# -SelfTest                : exercises the pure components (prefix strip, classification,
#                            merge eligibility) against the pinned values; prints one
#                            PASS/FAIL line per check and exits 0 only when every check
#                            passes.
# -Monitor n               : live mode - enumerates the real WT windows, reads tab titles
#                            via UIA, queries child-process command lines via WMI, composes
#                            the snapshot, and prints per window: titles, stripped titles,
#                            matched sessions, identification and merge eligibility. Output
#                            is scoped by the shared production rule (WindowScope.Filter:
#                            stable monitor key + current desktop only).
# -Monitor n -InventoryPath <file>
#                          : deterministic fixture mode - loads the pinned inventory JSON
#                            instead of enumerating the live desktop and prints the scoped
#                            window rows (tests; never touches a real window).
# -AllMonitors             : explicit no-monitor-filter scope (the desktop safety filter
#                            ALWAYS still applies; -AllMonitors never implies cross-desktop).
# There is NO implicit default monitor: exactly one of -SelfTest / -Monitor / -AllMonitors.
# Exit codes: 0 success or valid no-windows; 1 invalid args / missing required file /
#             missing requested monitor; 2 acquisition exception/failure. Live output is
#             informational (morning checklist), NOT an acceptance claim about live
#             correctness.
param(
    [Parameter(ParameterSetName='SelfTest', Mandatory=$true)]
    [switch]$SelfTest,

    [Parameter(ParameterSetName='Monitor', Mandatory=$true)]
    [int]$Monitor,

    [Parameter(ParameterSetName='All', Mandatory=$true)]
    [switch]$AllMonitors,

    [string]$InventoryPath
)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here
$dllPath = Join-Path $repoRoot 'bin\TerminalOrganizer.Core.dll'

if (-not (Test-Path $dllPath)) {
    Write-Output ('FAIL: build output missing: ' + $dllPath + ' (run build.ps1 first)')
    exit 1
}
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dllPath))

# Pinned values (acceptance.md Conventions; plan.md J.1/J.2) — never re-derived from code under test.
$CMD_LOCAL = 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA1 20'
$CMD_REMOTE = 'wsl.exe -d Ubuntu --exec /tmp/rdl_attach.sh user@host YODA2'
$CMD_NATIVE = 'powershell.exe -NoProfile -Command "& { $env:LAUNCHER_SESSION=''OPS1''; Get-Date }"'
$CMD_NATIVE_DQ = 'powershell.exe -NoProfile -Command "& { $env:LAUNCHER_SESSION="OPS9"; Get-Date }"'
$CMD_LOCAL_EXTRA = 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA1 EXTRA'
$CMD_LOCAL_INTERVENING = 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach EXTRA YODA1'

$script:FailCount = 0
$script:CheckCount = 0

function Assert-Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $script:CheckCount++
    if ($Ok) {
        Write-Output ('PASS: ' + $Name)
    }
    else {
        $script:FailCount++
        Write-Output ('FAIL: ' + $Name + ' — expected ' + $Detail)
    }
}

function New-ChildProc([string]$Image, [string]$CommandLine) {
    New-Object TerminalOrganizer.Core.Windows.ChildProcess -ArgumentList $Image, $CommandLine
}

function New-Sess([string]$Name, [string]$Kind, [string]$CommandLine) {
    $kindEnum = [TerminalOrganizer.Core.Windows.SessionKind]::Parse([TerminalOrganizer.Core.Windows.SessionKind], $Kind)
    New-Object TerminalOrganizer.Core.Windows.SessionRecord -ArgumentList $Name, $kindEnum, $CommandLine
}

if ($SelfTest) {
    # --- AC-001 prefix rows ---
    $prefixRows = @(
        @('OC_YODA1', 'YODA1'),
        @('WD_AB_X', 'AB_X'),
        @('Plain Title', 'Plain Title'),
        @('oc_yoda1', 'oc_yoda1'),
        @('XY_YODA1', 'XY_YODA1')
    )
    foreach ($row in $prefixRows) {
        $actual = [TerminalOrganizer.Core.Windows.TitleNormalizer]::StripPrefix($row[0])
        Assert-Check ('prefix/' + $row[0]) ($actual -ceq $row[1]) ($row[1] + ' got [' + $actual + ']')
    }

    # --- AC-002 classification rows ---
    $rows = @(
        @('wsl.exe', $CMD_LOCAL, 'Local', 'YODA1', 'local'),
        @('wsl.exe', $CMD_REMOTE, 'Remote', 'YODA2', 'remote'),
        @('powershell.exe', $CMD_NATIVE, 'WindowsNative', 'OPS1', 'native'),
        @('powershell.exe', $CMD_NATIVE_DQ, 'WindowsNative', 'OPS9', 'native-double-quote'),
        @('wsl.exe', $CMD_LOCAL_EXTRA, 'Local', 'YODA1', 'tokens-after-tolerated'),
        @('wsl.exe', $CMD_LOCAL_INTERVENING, 'Local', 'EXTRA', 'token-immediately-after-attach')
    )
    foreach ($row in $rows) {
        $record = [TerminalOrganizer.Core.Windows.CommandLineClassifier]::Classify($row[0], $row[1])
        $ok = ($null -ne $record) -and ($record.Kind.ToString() -ceq $row[2]) -and ($record.Name -ceq $row[3])
        Assert-Check ('classify/' + $row[4]) $ok ($row[2] + '/' + $row[3] + ' got ' + $(if ($null -eq $record) { 'null' } else { $record.Kind.ToString() + '/' + $record.Name }))
    }

    $nullCmd = [TerminalOrganizer.Core.Windows.CommandLineClassifier]::Classify('cmd.exe', 'cmd.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach NOPE')
    Assert-Check 'classify/wrong-image-null' ($null -eq $nullCmd) 'null got ' + $(if ($null -eq $nullCmd) { 'null' } else { 'record' })
    $listCmd = [TerminalOrganizer.Core.Windows.CommandLineClassifier]::Classify('wsl.exe', 'wsl.exe --list')
    Assert-Check 'classify/wsl-list-null' ($null -eq $listCmd) 'null got ' + $(if ($null -eq $listCmd) { 'null' } else { 'record' })

    # --- AC-006 merge-eligibility rows (standing set) ---
    $sessions = @(
        (New-Sess 'YODA1' 'Local' $CMD_LOCAL),
        (New-Sess 'YODA2' 'Remote' $CMD_REMOTE),
        (New-Sess 'OPS1' 'WindowsNative' $CMD_NATIVE)
    )
    $eligRows = @(
        @(, @('OC_YODA1'), $true,  'single-local-identified'),
        @(, @('NC_OPS1'), $false, 'single-windows-native'),
        @(, @('WD_AB_X'), $false, 'single-unidentified'),
        @(, @('OC_YODA1', 'HC_YODA2'), $false, 'two-tab-identified')
    )
    foreach ($row in $eligRows) {
        $result = [TerminalOrganizer.Core.Windows.SessionMatcher]::Match([string[]]$row[0], $sessions)
        Assert-Check ('eligibility/' + $row[2]) ($result.Mergeable -eq $row[1]) ($row[1].ToString() + ' got ' + $result.Mergeable.ToString())
    }

    Write-Output ('SelfTest summary: ' + $script:CheckCount + ' checks, ' + $script:FailCount + ' failed')
    if ($script:FailCount -gt 0) { exit 1 }
    exit 0
}

# --- Scope modes (-Monitor n or -AllMonitors; B5) ---
# Writes ERROR to stderr and exits with the given nonzero code.
function Fail-Dump([string]$Message, [int]$Code) {
    [Console]::Error.WriteLine('ERROR: ' + $Message)
    exit $Code
}

function New-InventoryMonitor($Row) {
    $wa = New-Object TerminalOrganizer.Core.Geometry.WorkArea -ArgumentList `
        $Row.workLeft, $Row.workTop, $Row.workWidth, $Row.workHeight, $Row.dpi
    New-Object TerminalOrganizer.Core.Monitors.MonitorInfo -ArgumentList `
        $Row.interfacePath, $Row.monitorId, $Row.instance, $Row.serial, $Row.number,
        $Row.left, $Row.top, $Row.width, $Row.height, $wa
}

function New-InventoryWindow($Row, $Monitors) {
    $monitor = $null
    if ($null -ne $Row.monitor) {
        foreach ($m in $Monitors) { if ($m.Number -eq [int]$Row.monitor) { $monitor = $m } }
    }
    $status = [TerminalOrganizer.Core.Monitors.DesktopFlagResult]::Ok($true)
    if ($Row.desktop -ceq 'other') { $status = [TerminalOrganizer.Core.Monitors.DesktopFlagResult]::Ok($false) }
    if ($Row.desktop -ceq 'unknown') { $status = [TerminalOrganizer.Core.Monitors.DesktopFlagResult]::Failure('inventory: unknown desktop') }
    New-Object TerminalOrganizer.Core.Windows.EnumeratedWindow -ArgumentList ([IntPtr]$Row.handle), $monitor, $status
}

# Acquisition: the pinned inventory fixture (deterministic; never touches the live
# desktop) or the real providers. Any failure is exit 2.
$AcquiredMonitors = $null
$AcquiredWindows = $null
$LiveSessions = $null
$titleReader = $null
try {
    if (-not [string]::IsNullOrEmpty($InventoryPath)) {
        if (-not (Test-Path -LiteralPath $InventoryPath)) {
            Fail-Dump ('required inventory file not found: ' + $InventoryPath) 1
        }
        $resolvedInventory = (Resolve-Path -LiteralPath $InventoryPath).ProviderPath
        $doc = ConvertFrom-Json ([IO.File]::ReadAllText($resolvedInventory))
        if ($doc.failAcquisition) { throw ('inventory pins an injected acquisition failure: ' + $resolvedInventory) }
        $AcquiredMonitors = @(foreach ($row in @($doc.monitors)) { New-InventoryMonitor $row })
        $AcquiredWindows = @(foreach ($row in @($doc.windows)) { New-InventoryWindow $row $AcquiredMonitors })
    }
    else {
        $provider = New-Object TerminalOrganizer.Core.Monitors.Win32MonitorProvider
        $desktopReader = New-Object TerminalOrganizer.Core.Monitors.VirtualDesktopReader
        $titleReader = New-Object TerminalOrganizer.Core.Windows.UiaTabTitleReader
        $enumerator = New-Object TerminalOrganizer.Core.Windows.Win32WindowEnumerator
        $query = New-Object TerminalOrganizer.Core.Windows.ProcessSessionQuery

        $AcquiredMonitors = $provider.GetMonitors()
        $wtProcs = @([System.Diagnostics.Process]::GetProcessesByName('WindowsTerminal'))
        $wtPids = @($wtProcs | ForEach-Object { $_.Id })
        Write-Output ('live dump: ' + $wtPids.Length + ' WindowsTerminal process(es)')
        $children = $query.QueryChildren([int[]]$wtPids)
        $LiveSessions = [TerminalOrganizer.Core.Windows.CommandLineClassifier]::ClassifyAll($children)
        Write-Output ('live dump: ' + $children.Length + ' wsl/powershell child process(es); ' + $LiveSessions.Length + ' open session(s)')
        foreach ($s in $LiveSessions) {
            Write-Output ('  session ' + $s.Name + ' kind=' + $s.Kind.ToString() + ' tmux-backed=' + $s.TmuxBacked)
            Write-Output ('    cmd=' + $s.CommandLine)
        }
        $AcquiredWindows = $enumerator.Enumerate([int[]]$wtPids, $desktopReader, $AcquiredMonitors)
    }
}
catch {
    Fail-Dump ('acquisition failed: ' + $_.Exception.Message) 2
}

# Monitor resolution: -Monitor n must name a real monitor (exit 1 when absent).
# The scoping itself is the shared production rule - never re-implemented here.
$ScopeKey = $null
if (-not $AllMonitors) {
    if ($Monitor -le 0) {
        Fail-Dump ('invalid -Monitor value ' + $Monitor + ' (a positive monitor number or -AllMonitors is required)') 1
    }
    $chosen = @($AcquiredMonitors | Where-Object { $_.Number -eq $Monitor }) | Select-Object -First 1
    if ($null -eq $chosen) {
        Fail-Dump ('requested monitor ' + $Monitor + ' not found among ' + $AcquiredMonitors.Length + ' monitor(s)') 1
    }
    $ScopeKey = $chosen.StableKey
}

$scope = [TerminalOrganizer.Core.Windows.WindowScope]::Filter($AcquiredWindows, $ScopeKey, [bool]$AllMonitors.IsPresent)
$scopeLabel = 'all monitors'
if (-not $AllMonitors) { $scopeLabel = 'monitor ' + $Monitor }
Write-Output ('scope (' + $scopeLabel + '): included=' + $scope.Included.Length +
    ' other-monitor=' + $scope.SkippedOtherMonitor + ' other-desktop=' + $scope.SkippedOtherDesktop +
    ' unknown-desktop=' + $scope.SkippedUnknownDesktop + ' unattributed=' + $scope.SkippedUnattributed)

if ($scope.Included.Length -eq 0) {
    Write-Output 'dump: no windows in scope (valid empty result; acquisition succeeded)'
    Write-Output 'dump is informational (morning checklist); it is NOT an acceptance claim'
    exit 0
}

if ($null -ne $LiveSessions) {
    # Live mode: compose snapshots for the scoped windows and print the full report.
    $acquired = @()
    foreach ($w in $scope.Included) {
        $titles = $titleReader.ReadTabs($w.Handle)
        if (-not $titles.Success) {
            Write-Output ('note: UIA read failed for handle ' + $w.Handle + ': ' + $titles.Error + ' (fallback title in use)')
        }
        $acquired += , (New-Object TerminalOrganizer.Core.Windows.AcquiredWindow -ArgumentList $w.Handle, $titles, $w.Monitor, $w.DesktopStatus)
    }
    $snapshots = [TerminalOrganizer.Core.Windows.WindowSnapshotBuilder]::Compose($acquired, $LiveSessions)
    foreach ($snap in $snapshots) {
        $monitorLabel = '<unattributed>'
        if ($null -ne $snap.Monitor) { $monitorLabel = '#' + $snap.Monitor.Number }
        $desktopLabel = 'unknown'
        if ($snap.DesktopStatus.Success) { $desktopLabel = $snap.DesktopStatus.Value.ToString() }
        Write-Output ('window ' + $snap.Handle + ': monitor=' + $monitorLabel + ' current-desktop=' + $desktopLabel +
            ' identified=' + $snap.Identified + ' mergeable=' + $snap.Mergeable)
        for ($i = 0; $i -lt $snap.Tabs.Length; $i++) {
            $tab = $snap.Tabs[$i]
            $sessionLabel = 'UNMATCHED'
            if ($tab.Matched) { $sessionLabel = $tab.Session.Name + ' (' + $tab.Session.Kind.ToString() + ')' }
            Write-Output ('  tab ' + $i + ': [' + $tab.Title + '] stripped=[' + $tab.StrippedTitle + '] session=' + $sessionLabel)
        }
        if (-not $snap.Identified -and $snap.UnmatchedTitles.Length -gt 0) {
            Write-Output ('  unmatched: ' + ($snap.UnmatchedTitles -join ' | '))
        }
    }
}
else {
    # Inventory mode: the pinned fixture rows (deterministic; no UIA/WMI).
    foreach ($w in $scope.Included) {
        Write-Output ('window ' + $w.Handle + ': monitor=#' + $w.Monitor.Number + ' current-desktop=' + $w.DesktopStatus.Value)
    }
}
Write-Output 'dump is informational (morning checklist); it is NOT an acceptance claim'
exit 0
