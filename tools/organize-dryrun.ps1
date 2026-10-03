# SPEC-ZONE-005 dry-run tool (plan.md F file 7; acceptance AC-010).
# -SelfTest     : exercises the pure planning rows (AC-001..AC-006) against the pinned
#                 zone/window values from acceptance.md and plan.md section J; prints one
#                 PASS/FAIL line per check and exits 0 only when every check passes.
# No switch     : live mode - computes and PRINTS the plan for the real screen without
#                 moving any window (the dry-run never applies a plan, ever). Prints the
#                 resolved zones, the persisted manager choice, and one row per window:
#                 target zone/rect, no-move / restore-first / stacked / full-screen-skip
#                 markers, plus the manager pin or skip reason. Live output is
#                 informational (morning checklist), NOT an acceptance claim.
param(
    [switch]$SelfTest,
    [int]$Monitor = 1,
    [switch]$AllMonitors,
    [string]$SettingsPath,
    [string]$AppliedLayoutsPath,
    [string]$CustomLayoutsPath
)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here
$dllPath = Join-Path $repoRoot 'bin\TerminalOrganizer.Core.dll'

if (-not $SettingsPath) {
    $SettingsPath = Join-Path $env:LOCALAPPDATA 'TerminalOrganizer\settings.json'
}
$fancyZonesDir = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys\FancyZones'
if (-not $AppliedLayoutsPath) {
    $AppliedLayoutsPath = Join-Path $fancyZonesDir 'applied-layouts.json'
}
if (-not $CustomLayoutsPath) {
    $CustomLayoutsPath = Join-Path $fancyZonesDir 'custom-layouts.json'
}

if (-not (Test-Path $dllPath)) {
    Write-Output ('FAIL: build output missing: ' + $dllPath + ' (run build.ps1 first)')
    exit 1
}
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dllPath))
# C2: the App assembly carries the snapshot composer and the physical-label derivation
# the cross-monitor preview needs (pure types; no WinForms dependency is touched).
$appDllPath = Join-Path $repoRoot 'bin\TerminalOrganizer.App.exe'
if (-not (Test-Path $appDllPath)) {
    Write-Output ('FAIL: build output missing: ' + $appDllPath + ' (run build.ps1 first)')
    exit 1
}
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($appDllPath))

# Pinned values (acceptance.md Conventions; plan.md J.1/J.2) - never re-derived from code under test.
# Standing zone set: z0 456x1120 @ 16,16; z1 944x1120 @ 488,16 (largest, first in ZoneOrdering order); z2 456x1120 @ 1448,16.
function New-StandingZones {
    @(
        [TerminalOrganizer.Core.Geometry.Zone]::new(0, 16, 16, 456, 1120),
        [TerminalOrganizer.Core.Geometry.Zone]::new(1, 488, 16, 944, 1120),
        [TerminalOrganizer.Core.Geometry.Zone]::new(2, 1448, 16, 456, 1120)
    )
}

$script:FactHandleSeed = 1000

function New-Fact {
    param(
        [string]$Id,
        [int]$Left = 0,
        [int]$Top = 0,
        [int]$Width = 400,
        [int]$Height = 300,
        [switch]$Maximized,
        [switch]$Minimized,
        [switch]$FullScreen
    )
    $script:FactHandleSeed = $script:FactHandleSeed + 1
    [TerminalOrganizer.Core.Assignment.WindowFact]::new(
        $Id, [IntPtr]$script:FactHandleSeed, $Id, $true,
        $Left, $Top, $Width, $Height,
        [bool]$Maximized.IsPresent, [bool]$Minimized.IsPresent, [bool]$FullScreen.IsPresent)
}

function Invoke-Assign($Zones, $Facts, [string]$ManagerName) {
    [TerminalOrganizer.Core.Assignment.ZoneAssigner]::Assign($Zones, $Facts, $ManagerName)
}

function Get-Move($Plan, [string]$WindowId) {
    @($Plan.Moves | Where-Object { $_.WindowId -eq $WindowId })[0]
}

$script:FailCount = 0
$script:CheckCount = 0

function Assert-Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $script:CheckCount++
    if ($Ok) {
        Write-Output ('PASS: ' + $Name)
    }
    else {
        $script:FailCount++
        Write-Output ('FAIL: ' + $Name + ' - expected ' + $Detail)
    }
}

if ($SelfTest) {
    # --- AC-001 manager pin ---
    $zones = New-StandingZones
    $plan = Invoke-Assign $zones @(
        (New-Fact 'MGR' -Left 100 -Top 100),
        (New-Fact 'A'),
        (New-Fact 'B' -Left 500)
    ) 'MGR'
    $ok = $plan.ManagerPinned -and (Get-Move $plan 'MGR').ZoneId -eq 1 -and
        (Get-Move $plan 'A').ZoneId -eq 0 -and (Get-Move $plan 'B').ZoneId -eq 2
    Assert-Check 'ac001/manager-pin' $ok 'MGR->z1, A->z0, B->z2, pinned'

    # --- AC-002 position fill order + tie ---
    $plan = Invoke-Assign (New-StandingZones) @(
        (New-Fact 'A'),
        (New-Fact 'B' -Left 500),
        (New-Fact 'C' -Top 400)
    ) $null
    $ok = (Get-Move $plan 'A').ZoneId -eq 1 -and (Get-Move $plan 'B').ZoneId -eq 0 -and
        (Get-Move $plan 'C').ZoneId -eq 2
    Assert-Check 'ac002/position-order' $ok 'A,B,C -> z1,z0,z2 (z1 first: largest)'

    $plan = Invoke-Assign (New-StandingZones) @((New-Fact 'E'), (New-Fact 'D')) $null
    $ok = (Get-Move $plan 'D').ZoneId -eq 1 -and (Get-Move $plan 'E').ZoneId -eq 0
    Assert-Check 'ac002/tie-id-ordinal' $ok 'D before E at the same top-left'

    # --- AC-003 overflow stacking ---
    $plan = Invoke-Assign (New-StandingZones) @(
        (New-Fact 'MGR' -Left 100 -Top 100),
        (New-Fact 'A'),
        (New-Fact 'B' -Left 500),
        (New-Fact 'C' -Top 400),
        (New-Fact 'D' -Top 800)
    ) 'MGR'
    $stacked = @($plan.Moves | Where-Object { $_.Stacked })
    $ok = (Get-Move $plan 'A').ZoneId -eq 0 -and (Get-Move $plan 'B').ZoneId -eq 2 -and
        $stacked.Count -eq 2 -and $stacked[0].WindowId -ceq 'C' -and $stacked[1].WindowId -ceq 'D' -and
        $stacked[0].ZoneId -eq 2
    Assert-Check 'ac003/overflow-stack' $ok 'C,D stacked in z2, C then D'

    # --- AC-004 manager skipped ---
    $plan = Invoke-Assign (New-StandingZones) @((New-Fact 'A'), (New-Fact 'B' -Left 500)) 'GHOST'
    Assert-Check 'ac004/ghost-reason' ($plan.ManagerSkippedReason -ceq 'window not found') 'window not found'

    $plan = Invoke-Assign (New-StandingZones) @((New-Fact 'A'), (New-Fact 'B' -Left 500)) $null
    Assert-Check 'ac004/no-choice-reason' ($plan.ManagerSkippedReason -ceq 'no choice') 'no choice'

    # --- AC-005 idempotent no-move ---
    $plan = Invoke-Assign (New-StandingZones) @((New-Fact 'A' -Left 488 -Top 16 -Width 944 -Height 1120)) $null
    Assert-Check 'ac005/exact-rect-no-move' (-not (Get-Move $plan 'A').MoveRequired) 'no-move at the exact z1 rect'

    $plan = Invoke-Assign (New-StandingZones) @((New-Fact 'A' -Left 489 -Top 16 -Width 944 -Height 1120)) $null
    $move = Get-Move $plan 'A'
    Assert-Check 'ac005/one-pixel-move' ($move.MoveRequired -and $move.TargetLeft -eq 488) 'move to z1 rect from 1px off'

    # --- AC-006 restore-first and full-screen ---
    $plan = Invoke-Assign (New-StandingZones) @(
        (New-Fact 'MGR' -Left 700 -Top 700),
        (New-Fact 'A' -Width 1920 -Height 1040 -Maximized)
    ) 'MGR'
    $move = Get-Move $plan 'A'
    Assert-Check 'ac006/maximized-restore-first' ($move.ZoneId -eq 0 -and $move.RestoreFirst -and $move.MoveRequired) 'restore-first + target for a maximized window in z0'

    $plan = Invoke-Assign (New-StandingZones) @(
        (New-Fact 'MGR' -Left 700 -Top 700),
        (New-Fact 'A' -Left 16 -Top 16 -Width 456 -Height 1120 -Maximized)
    ) 'MGR'
    $move = Get-Move $plan 'A'
    Assert-Check 'ac006/state-outranks-rect' ($move.RestoreFirst -and $move.MoveRequired) 'restore-first even at the exact target rect'

    $plan = Invoke-Assign (New-StandingZones) @(
        (New-Fact 'MGR' -Left 700 -Top 700),
        (New-Fact 'A' -FullScreen)
    ) 'MGR'
    $move = Get-Move $plan 'A'
    Assert-Check 'ac006/full-screen-skipped' ((-not $move.MoveRequired) -and $move.FullScreenSkipped -and $move.ZoneId -eq -1 -and (-not $move.OccupiesZone)) 'unzoned no-move with FullScreenSkipped'

    # --- B5: scoping parity (a monitor's plan consumes only that monitor's windows,
    # through the shared WindowScope.Filter - never re-implemented in PowerShell) ---
    $b5WorkA = New-Object TerminalOrganizer.Core.Geometry.WorkArea -ArgumentList 0, 0, 1920, 1152, 96
    $b5MonA = New-Object TerminalOrganizer.Core.Monitors.MonitorInfo -ArgumentList `
        '\\?\DISPLAY#DELA0B5#b5inst1', 'DELA0B5', 'b5inst1', 'B5DRY1', 1, 0, 0, 1920, 1200, $b5WorkA
    $b5MonB = New-Object TerminalOrganizer.Core.Monitors.MonitorInfo -ArgumentList `
        '\\?\DISPLAY#DELA0B5#b5inst2', 'DELA0B5', 'b5inst2', 'B5DRY2', 2, 1920, 0, 1920, 1200, $b5WorkA
    $b5Current = [TerminalOrganizer.Core.Monitors.DesktopFlagResult]::Ok($true)
    $b5Rows = @(
        (New-Object TerminalOrganizer.Core.Windows.EnumeratedWindow -ArgumentList ([IntPtr]4201), $b5MonA, $b5Current),
        (New-Object TerminalOrganizer.Core.Windows.EnumeratedWindow -ArgumentList ([IntPtr]4202), $b5MonB, $b5Current),
        (New-Object TerminalOrganizer.Core.Windows.EnumeratedWindow -ArgumentList ([IntPtr]4203), $b5MonB, $b5Current)
    )
    $b5Scoped = [TerminalOrganizer.Core.Windows.WindowScope]::Filter($b5Rows, $b5MonA.StableKey, $false)
    $b5Facts = @()
    foreach ($b5Window in $b5Scoped.Included) { $b5Facts += , (New-Fact ('B5W' + $b5Window.Handle)) }
    $b5Plan = Invoke-Assign (New-StandingZones) $b5Facts $null
    $b5Ok = $b5Scoped.Included.Length -eq 1 -and $b5Scoped.SkippedOtherMonitor -eq 2 -and
        $b5Facts.Length -eq 1 -and $b5Plan.Moves.Count -eq 1 -and $b5Plan.Moves[0].WindowId -ceq 'B5W4201'
    Assert-Check 'b5/no-cross-monitor-facts' $b5Ok 'monitor 1 scopes to window 4201 only and plans exactly one move'

    Write-Output ('SelfTest summary: ' + $script:CheckCount + ' checks, ' + $script:FailCount + ' failed')
    if ($script:FailCount -gt 0) { exit 1 }
    exit 0
}

# --- Live mode (morning checklist; informational only; no window is ever moved) ---
# Exit codes (B5): 0 success or valid no-windows; 1 invalid args / missing required
# file / missing requested monitor; 2 acquisition exception; 3 invalid/unsupported
# layout when a plan was explicitly requested. -AllMonitors only PRINTS per-monitor
# plans (a monitor without a usable layout prints a note and the sweep continues);
# every per-monitor scope is WindowScope.Filter with that monitor's stable key.
function Fail-Dry([string]$Message, [int]$Code) {
    [Console]::Error.WriteLine('ERROR: ' + $Message)
    exit $Code
}

try {
    $provider = New-Object TerminalOrganizer.Core.Monitors.Win32MonitorProvider
    $desktopReader = New-Object TerminalOrganizer.Core.Monitors.VirtualDesktopReader
    $titleReader = New-Object TerminalOrganizer.Core.Windows.UiaTabTitleReader
    $enumerator = New-Object TerminalOrganizer.Core.Windows.Win32WindowEnumerator
    $sessionQuery = New-Object TerminalOrganizer.Core.Windows.ProcessSessionQuery
    $stateReader = New-Object TerminalOrganizer.Core.Assignment.WindowStateReader

    $desktopText = $null
    $win10 = $desktopReader.TryReadWin10SessionValue()
    if ($win10.Status.ToString() -eq 'Present') {
        $desktopText = $win10.Value
    }
    else {
        $win11 = $desktopReader.TryReadWin11Value()
        if ($win11.Status.ToString() -eq 'Present') { $desktopText = $win11.Value }
    }

    $monitors = $provider.GetMonitors()
    if ($AllMonitors) {
        $plannedMonitors = @($monitors)
    }
    else {
        $chosen = @($monitors | Where-Object { $_.Number -eq $Monitor }) | Select-Object -First 1
        if ($null -eq $chosen) {
            Fail-Dry ('requested monitor ' + $Monitor + ' not found among ' + $monitors.Length + ' monitor(s)') 1
        }
        $plannedMonitors = @($chosen)
    }

    if (-not (Test-Path -LiteralPath $AppliedLayoutsPath) -or -not (Test-Path -LiteralPath $CustomLayoutsPath)) {
        Fail-Dry ('required FancyZones documents not found; expected ' + $AppliedLayoutsPath + ' and ' + $CustomLayoutsPath) 1
    }
    $appliedJson = [IO.File]::ReadAllText($AppliedLayoutsPath)
    $customJson = [IO.File]::ReadAllText($CustomLayoutsPath)
    $appliedDoc = [TerminalOrganizer.Core.Layouts.FancyZonesJsonReader]::ParseAppliedLayouts($appliedJson)
    if (-not $appliedDoc.IsValid) {
        Fail-Dry ('applied-layouts document invalid: ' + $appliedDoc.Detail) 3
    }

    $store = New-Object TerminalOrganizer.Core.Assignment.ManagerStore -ArgumentList $SettingsPath
    $managerName = $store.Load()
    Write-Output ('dry run: manager choice from ' + $SettingsPath + ': ' + $(if ($null -ne $managerName) { $managerName } else { '<no choice>' }))

    $wtProcs = @([System.Diagnostics.Process]::GetProcessesByName('WindowsTerminal'))
    $wtPids = @($wtProcs | ForEach-Object { $_.Id })
    $children = $sessionQuery.QueryChildren([int[]]$wtPids)
    $sessions = [TerminalOrganizer.Core.Windows.CommandLineClassifier]::ClassifyAll($children)
    $windows = $enumerator.Enumerate([int[]]$wtPids, $desktopReader, $monitors)
    Write-Output ('dry run: ' + $windows.Length + ' WT window(s), ' + $sessions.Length + ' open session(s)')

    # C2 accumulators: every planned monitor's layout/facts/plan feed the cross-monitor
    # snapshot once the per-monitor sweep completes.
    $cmMonitors = New-Object 'System.Collections.Generic.List[object]'
    $cmZones = New-Object 'System.Collections.Generic.List[object]'
    $cmLabels = New-Object 'System.Collections.Generic.List[string]'
    $cmPlans = New-Object 'System.Collections.Generic.List[object]'
    $cmFacts = New-Object 'System.Collections.Generic.List[object]'
    $cmSnaps = New-Object 'System.Collections.Generic.List[object]'

    foreach ($target in $plannedMonitors) {
        $selected = [TerminalOrganizer.Core.Monitors.AppliedEntrySelector]::Select($appliedDoc, $target.ToIdentity(), $desktopText)
        if ($null -eq $selected) {
            $note = 'no applied-layouts entry matches monitor #' + $target.Number + '; nothing to plan'
            if ($AllMonitors) { Write-Output ('note: ' + $note); continue }
            Fail-Dry $note 3
        }
        $context = [TerminalOrganizer.Core.Monitors.OrganizerContext]::Build($selected, $target)
        $layout = [TerminalOrganizer.Core.Layouts.LayoutResolver]::ResolveByDeviceKey($appliedJson, $customJson, $context.Key, $context.WorkArea)
        if ($layout.Kind.ToString() -ne 'Supported') {
            $note = 'layout not resolved for monitor #' + $target.Number + ': kind=' + $layout.Kind.ToString() + ' reason=' + $layout.Reason.ToString() + ' detail=' + $layout.Detail
            if ($AllMonitors) { Write-Output ('note: ' + $note); continue }
            Fail-Dry $note 3
        }
        $zones = $layout.Zones
        Write-Output ('dry run: monitor #' + $target.Number + ', layout type=' + $layout.LayoutType + ' name=' + $layout.LayoutName + ', ' + $zones.Length + ' zone(s)')
        foreach ($z in $zones) {
            Write-Output ('  ' + $z.ToString())
        }

        # B5: THIS monitor's stable key through the shared production scope rule.
        $scope = [TerminalOrganizer.Core.Windows.WindowScope]::Filter($windows, $target.StableKey, $false)
        Write-Output ('  scope: included=' + $scope.Included.Length + ' other-monitor=' + $scope.SkippedOtherMonitor +
            ' other-desktop=' + $scope.SkippedOtherDesktop + ' unknown-desktop=' + $scope.SkippedUnknownDesktop +
            ' unattributed=' + $scope.SkippedUnattributed)

        $acquired = @()
        foreach ($w in $scope.Included) {
            $titles = $titleReader.ReadTabs($w.Handle)
            $acquired += , (New-Object TerminalOrganizer.Core.Windows.AcquiredWindow -ArgumentList $w.Handle, $titles, $w.Monitor, $w.DesktopStatus)
        }
        $snapshots = [TerminalOrganizer.Core.Windows.WindowSnapshotBuilder]::Compose($acquired, $sessions)

        $facts = @()
        foreach ($snap in $snapshots) {
            $state = $stateReader.Read($snap.Handle)
            $name = [TerminalOrganizer.Core.Windows.UiaTabTitleReader]::GetWindowTextTitle($snap.Handle)
            if ([string]::IsNullOrEmpty($name) -and $snap.Tabs.Length -gt 0) { $name = $snap.Tabs[0].Title }
            $facts += , [TerminalOrganizer.Core.Assignment.WindowFact]::new(
                ('w' + $snap.Handle), $snap.Handle, $name, $snap.Identified,
                $state.Left, $state.Top, $state.Width, $state.Height,
                $state.Maximized, $state.Minimized, $state.FullScreen)
        }

        $plan = Invoke-Assign $zones $facts $managerName
        if ($plan.ManagerPinned) {
            Write-Output ('  plan: manager pinned: ' + $plan.ManagerWindowId)
        }
        else {
            Write-Output ('  plan: manager skipped: ' + $plan.ManagerSkippedReason)
        }
        foreach ($move in $plan.Moves) {
            Write-Output ('  ' + $move.ToString())
        }
        if ($facts.Length -eq 0) {
            Write-Output ('  dry run: no windows in scope on monitor #' + $target.Number + ' (valid empty result)')
        }

        $cmMonitors.Add($target)
        $cmZones.Add($zones)
        $cmLabels.Add([TerminalOrganizer.App.TrayMenuBuilder]::DerivePhysicalLabel($target, $monitors))
        $cmPlans.Add($plan)
        foreach ($cmFact in $facts) { $cmFacts.Add($cmFact) }
        foreach ($cmSnap in $snapshots) { $cmSnaps.Add($cmSnap) }
    }

    # C2: the pure cross-monitor redistribution preview over the planned monitors. The
    # dry run never mutates anything - this plan is computed, printed, and never applied.
    if ($cmMonitors.Count -gt 0) {
        $cmOverrides = @()
        try {
            $cmStore = New-Object TerminalOrganizer.App.SettingsStore -ArgumentList $SettingsPath
            $cmOverrides = $cmStore.Load().PriorityOverrides
        }
        catch { $cmOverrides = @() }
        # Sources are only the organized monitor's own overflow; -AllMonitors organizes
        # every monitor, so the null key keeps every monitor's overflow as a source.
        $cmOrganizedKey = $null
        if (-not $AllMonitors) { $cmOrganizedKey = $chosen.StableKey }
        $cmSnapshot = [TerminalOrganizer.App.CrossMonitorSnapshotComposer]::Compose(
            $desktopText, $cmMonitors.ToArray(), $cmZones.ToArray(), $cmLabels.ToArray(),
            $cmPlans.ToArray(), $windows, $cmFacts.ToArray(), $cmSnaps.ToArray(),
            [TerminalOrganizer.App.PriorityOverride[]]@($cmOverrides), $cmOrganizedKey)
        $cmPlan = [TerminalOrganizer.Core.Overflow.CrossMonitorPlanner]::Plan($cmSnapshot)
        Write-Output ('dry run: redistribute planned=' + $cmPlan.Moves.Length + ' applied=0 (pure plan; C3 selects policy)')
        foreach ($cmMove in $cmPlan.Moves) {
            Write-Output ('  ' + $cmMove.ToWhatIfText().Replace("`n", "`n  "))
        }
    }
}
catch {
    Fail-Dry ('acquisition failed: ' + $_.Exception.Message) 2
}
Write-Output 'dry run is informational (morning checklist); it is NOT an acceptance claim and never moves a window'
exit 0
