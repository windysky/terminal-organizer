# SPEC-TRAY-007 headless one-shot organize tool (plan.md F file 12; acceptance AC-012).
# -SelfTest : exercises the pure rows (AC-005 hotkey parse + AC-006 pipeline rows) against
#             the pinned values from acceptance.md and plan.md section J; prints one
#             PASS/FAIL line per check and exits 0 only when every check passes. Never
#             touches a window.
# -WhatIf   : plan-only - the SAME controller over the REAL read ports, but BOTH mutation
#             ports (the merge executor AND the placer) are swapped for recording fakes:
#             zero merges executed (no wt launch), zero moves issued (no SetWindowPos).
#             Prints the plan. Read-only on the live screen.
# Live mode : performs ONE real organize (real merges + real placement). MORNING CHECKLIST
#             ONLY - MANUAL VERIFICATION, NOT an acceptance claim.
param(
    [switch]$SelfTest,
    [switch]$WhatIf,
    [int]$Monitor = 1
)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here
$coreDll = Join-Path $repoRoot 'bin\TerminalOrganizer.Core.dll'
$appExe = Join-Path $repoRoot 'bin\TerminalOrganizer.App.exe'

foreach ($bin in @($coreDll, $appExe)) {
    if (-not (Test-Path $bin)) {
        Write-Output ('FAIL: build output missing: ' + $bin + ' (run build.ps1 first)')
        exit 1
    }
}
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($coreDll))
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($appExe))

# The pinned live-mode label (acceptance AC-012; em-dash assembled so the file stays ASCII).
$ManualLabel = 'manual verification step ' + [char]0x2014 + ' see morning checklist'

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

# Standing fixtures for the AC-006 rows (plan.md J.1 zone set + the 7-window scenario).
function New-OZones {
    @(
        [TerminalOrganizer.Core.Geometry.Zone]::new(0, 16, 16, 456, 1120),
        [TerminalOrganizer.Core.Geometry.Zone]::new(1, 488, 16, 944, 1120),
        [TerminalOrganizer.Core.Geometry.Zone]::new(2, 1448, 16, 456, 1120)
    )
}

$script:OHandles = @{}
$script:OSeed = 5000
function New-OFact {
    param([string]$Id, [int]$Top = 0, [int]$Left = 0, [bool]$Identified = $true)
    $script:OSeed = $script:OSeed + 1
    $script:OHandles[$Id] = [IntPtr]$script:OSeed
    [TerminalOrganizer.Core.Assignment.WindowFact]::new(
        $Id, $script:OHandles[$Id], $Id, $Identified,
        $Left, $Top, 400, 300, $false, $false, $false)
}

function New-OSession([string]$Name, [string]$Kind, [string]$CommandLine) {
    $kindEnum = [TerminalOrganizer.Core.Windows.SessionKind]::Parse([TerminalOrganizer.Core.Windows.SessionKind], $Kind)
    New-Object TerminalOrganizer.Core.Windows.SessionRecord -ArgumentList $Name, $kindEnum, $CommandLine
}

function New-OSnap {
    param([string]$Id, [string[]]$TabTitles, $TabSessions, [bool]$Identified, [bool]$Mergeable)
    $tabs = New-Object 'System.Collections.Generic.List[TerminalOrganizer.Core.Windows.TabSnapshot]'
    for ($i = 0; $i -lt $TabTitles.Count; $i++) {
        $raw = $TabTitles[$i]
        $stripped = [TerminalOrganizer.Core.Windows.TitleNormalizer]::StripPrefix($raw)
        $session = $null
        if ($i -lt $TabSessions.Count) { $session = $TabSessions[$i] }
        $tabs.Add((New-Object TerminalOrganizer.Core.Windows.TabSnapshot -ArgumentList $raw, $stripped, $session))
    }
    $unmatched = @()
    if (-not $Identified) { $unmatched = @($tabs | ForEach-Object { $_.StrippedTitle }) }
    New-Object TerminalOrganizer.Core.Windows.WindowSnapshot -ArgumentList `
        ([TerminalOrganizer.Core.Windows.WindowIdentity]::new($script:OHandles[$Id], 42, 12345, 'CASCADIA_HOSTING_WINDOW_CLASS', $TabTitles[0])), $null, $null, $tabs.ToArray(), $Identified, [string[]]$unmatched, $Mergeable, ([TerminalOrganizer.Core.Windows.TabReadQuality]::Trusted)
}

function New-OScenario {
    $yoda3Cmd = 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA3 20'
    $remoteCmd = 'wsl.exe -d Ubuntu --exec /tmp/rdl_attach.sh user@host YODA2'
    $script:OFacts = @(
        (New-OFact 'MGR' -Top 600 -Left 100),
        (New-OFact 'A'   -Top 0 -Left 0),
        (New-OFact 'B'   -Top 0 -Left 500),
        (New-OFact 'C'   -Top 400 -Left 0),
        (New-OFact 'D'   -Top 800 -Left 0 -Identified:$false),
        (New-OFact 'E'   -Top 900 -Left 0),
        (New-OFact 'F'   -Top 1000 -Left 0)
    )
    $script:OSnaps = @(
        (New-OSnap 'MGR' @('OC_MGR')   @((New-OSession 'MGR' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach MGR 20')) $true $true),
        (New-OSnap 'A'   @('OC_YODA1') @((New-OSession 'YODA1' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA1 20')) $true $true),
        (New-OSnap 'B'   @('OC_YODA2B') @((New-OSession 'YODA2B' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA2B 20')) $true $true),
        (New-OSnap 'C'   @('NC_OPS1')  @((New-OSession 'OPS1' 'WindowsNative' 'powershell.exe -NoProfile -Command "echo hi"')) $true $false),
        (New-OSnap 'D'   @('WD_UNK1')  @() $false $false),
        (New-OSnap 'E'   @('OC_YODA3') @((New-OSession 'YODA3' 'Local' $yoda3Cmd)) $true $true),
        (New-OSnap 'F'   @('OC_YODA2') @((New-OSession 'YODA2' 'Remote' $remoteCmd)) $true $true)
    )
}

if ($SelfTest) {
    # --- AC-005 rows (plan.md J.2) ---
    $r = [TerminalOrganizer.App.HotkeyParser]::Parse('Ctrl+Alt+O')
    Assert-Check 'ac005/ctrl-alt-o' ($r.Success -and $r.ModifierFlags -eq 3 -and $r.VirtualKey -eq 0x4F) 'success, mods 3 (CONTROL|ALT), vk 0x4F'
    $r = [TerminalOrganizer.App.HotkeyParser]::Parse('Ctrl+Shift+F5')
    Assert-Check 'ac005/ctrl-shift-f5' ($r.Success -and $r.ModifierFlags -eq 6 -and $r.VirtualKey -eq 0x74) 'success, mods 6 (CONTROL|SHIFT), vk 0x74'
    $r = [TerminalOrganizer.App.HotkeyParser]::Parse('ctrl+alt+o')
    Assert-Check 'ac005/case-insensitive' ($r.Success -and $r.ModifierFlags -eq 3 -and $r.VirtualKey -eq 0x4F) 'success, mods 3, vk 0x4F'
    $badResults = @('Hotkey+O', 'Ctrl+Alt', '') | ForEach-Object { [TerminalOrganizer.App.HotkeyParser]::Parse($_) }
    Assert-Check 'ac005/failure-rows' (@($badResults | Where-Object { -not $_.Success }).Count -eq 3) 'all three parse to failure results'

    # --- AC-006 rows (controller under fake ports; never touches a window) ---
    function New-OController {
        param([string]$LayoutMode, [string]$ProbeMode)
        New-OScenario
        $script:OSeq = New-Object 'System.Collections.Generic.List[string]'
        $script:ONotices = New-Object 'System.Collections.Generic.List[string]'
        $script:OLayoutMode = $LayoutMode
        $script:OProbeMode = $ProbeMode
        $script:OZones = New-OZones

        $desktopSrc = [TerminalOrganizer.App.CurrentDesktopSource] { $script:OSeq.Add('desktop'); '{00000000-0000-4000-8000-000000000007}' }
        $layoutRes = [TerminalOrganizer.App.LayoutResolution] {
            param($monitor, $desktop)
            $script:OSeq.Add('layout')
            if ($script:OLayoutMode -eq 'unsupported') {
                [TerminalOrganizer.Core.Layouts.LayoutResult]::Unsupported([TerminalOrganizer.Core.Layouts.LayoutReason]::Focus, 'focus layout', 'focus', $null)
            }
            else {
                [TerminalOrganizer.Core.Layouts.LayoutResult]::Supported('grid', $null, 0, $script:OZones, @())
            }
        }
        $windowDisc = [TerminalOrganizer.App.WindowDiscovery] {
            param($monitor, $desktop)
            $script:OSeq.Add('windows')
            New-Object TerminalOrganizer.App.DiscoveredWindows -ArgumentList $script:OFacts, $script:OSnaps
        }
        $assigner = [TerminalOrganizer.Core.Overflow.AssignmentComputation] {
            param($zones, $facts, $manager)
            $script:OSeq.Add('assign')
            [TerminalOrganizer.Core.Assignment.ZoneAssigner]::Assign($zones, $facts, $manager)
        }
        $planner = [TerminalOrganizer.App.MergePlanning] {
            param($plan, $snaps)
            $script:OSeq.Add('mergeplan')
            [TerminalOrganizer.Core.Overflow.MergePlanner]::Plan($plan, $snaps)
        }
        $probe = [TerminalOrganizer.App.HelperExistsProbe] {
            param($merge)
            $script:OSeq.Add('probe')
            if ($script:OProbeMode -eq 'false') { $false }
            elseif ($script:OProbeMode -eq 'throw') { throw 'probe infrastructure failed' }
            else { $true }
        }
        $executor = [TerminalOrganizer.Core.Overflow.MergeExecution] {
            param($merge)
            $script:OSeq.Add('merge:' + $merge.SourceWindowId)
            [TerminalOrganizer.Core.Overflow.MergeOutcome]::Merged($merge.SourceWindowId)
        }
        $placer = [TerminalOrganizer.Core.Overflow.PlacementPass] {
            param($plan)
            $script:OSeq.Add('place')
            @()
        }
        $notify = [TerminalOrganizer.App.NoticeSink] { param($text) $script:ONotices.Add($text) }
        New-Object TerminalOrganizer.App.OrganizeController -ArgumentList `
            $desktopSrc, $layoutRes, $windowDisc, $assigner, $planner, $probe, $executor, $placer, $notify, (New-Object TerminalOrganizer.App.LabelRegistry)
    }

    $c = New-OController 'unsupported' 'true'
    $null = $c.Run($null, 'MGR')
    Assert-Check 'ac006/unsupported-stop' `
        (($script:OSeq -join ',') -ceq 'desktop,layout' -and $script:ONotices.Count -eq 1 -and $script:ONotices[0] -ceq "FancyZones layout 'focus' is not supported on this monitor; nothing was changed.") `
        'notice verbatim, zero downstream calls'

    $c = New-OController 'supported' 'false'
    $null = $c.Run($null, 'MGR', $true)
    Assert-Check 'ac006/sequence' `
        (($script:OSeq -join ',') -ceq 'desktop,layout,windows,assign,mergeplan,probe,merge:E,assign,place') `
        'the exact pipeline order; HelperMissing skips only the Remote merge'

    $c = New-OController 'supported' 'throw'
    $null = $c.Run($null, 'MGR', $true)
    Assert-Check 'ac006/fail-open' `
        (($script:OSeq -join ',') -ceq 'desktop,layout,windows,assign,mergeplan,probe,merge:E,merge:F,assign,place') `
        'a failed probe does not skip the merge (fail-open)'

    # --- AC-012 label row ---
    Assert-Check 'ac012/live-label' `
        ($ManualLabel.Length -eq 48 -and $ManualLabel.StartsWith('manual verification step') -and $ManualLabel.EndsWith('see morning checklist')) `
        'the pinned live-mode label'

    Write-Output ('SelfTest summary: ' + $script:CheckCount + ' checks, ' + $script:FailCount + ' failed')
    if ($script:FailCount -gt 0) { exit 1 }
    exit 0
}

# --- Real read ports (shared by -WhatIf and live mode) ---
$desktopReader = New-Object TerminalOrganizer.Core.Monitors.VirtualDesktopReader
$monitorProvider = New-Object TerminalOrganizer.Core.Monitors.Win32MonitorProvider
$titleReader = New-Object TerminalOrganizer.Core.Windows.UiaTabTitleReader
$enumerator = New-Object TerminalOrganizer.Core.Windows.Win32WindowEnumerator
$sessionQuery = New-Object TerminalOrganizer.Core.Windows.ProcessSessionQuery
$stateReader = New-Object TerminalOrganizer.Core.Assignment.WindowStateReader
$fancyZonesDir = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys\FancyZones'
$appliedPath = Join-Path $fancyZonesDir 'applied-layouts.json'
$customPath = Join-Path $fancyZonesDir 'custom-layouts.json'

$desktopSrc = [TerminalOrganizer.App.CurrentDesktopSource] {
    $win10 = $desktopReader.TryReadWin10SessionValue()
    if ($win10.Status.ToString() -eq 'Present') { return $win10.Value }
    $win11 = $desktopReader.TryReadWin11Value()
    if ($win11.Status.ToString() -eq 'Present') { return $win11.Value }
    $null
}

$layoutRes = [TerminalOrganizer.App.LayoutResolution] {
    param($monitor, $desktop)
    if (-not (Test-Path -LiteralPath $appliedPath)) {
        return [TerminalOrganizer.Core.Layouts.LayoutResult]::Unsupported([TerminalOrganizer.Core.Layouts.LayoutReason]::NoAppliedLayout, $null, $null, $null)
    }
    $appliedJson = [IO.File]::ReadAllText($appliedPath)
    $customJson = ''
    if (Test-Path -LiteralPath $customPath) { $customJson = [IO.File]::ReadAllText($customPath) }
    $document = [TerminalOrganizer.Core.Layouts.FancyZonesJsonReader]::ParseAppliedLayouts($appliedJson)
    if (-not $document.IsValid) {
        return [TerminalOrganizer.Core.Layouts.LayoutResult]::Invalid([TerminalOrganizer.Core.Layouts.LayoutReason]::MalformedJson, $document.Detail, $null)
    }
    $entry = [TerminalOrganizer.Core.Monitors.AppliedEntrySelector]::Select($document, $monitor.ToIdentity(), $desktop)
    if ($null -eq $entry) {
        return [TerminalOrganizer.Core.Layouts.LayoutResult]::Unsupported([TerminalOrganizer.Core.Layouts.LayoutReason]::NoAppliedLayout, $null, $null, $null)
    }
    $context = [TerminalOrganizer.Core.Monitors.OrganizerContext]::Build($entry, $monitor)
    [TerminalOrganizer.Core.Layouts.LayoutResolver]::ResolveByDeviceKey($appliedJson, $customJson, $context.Key, $context.WorkArea)
}

$windowDisc = [TerminalOrganizer.App.WindowDiscovery] {
    param($monitor, $desktop)
    # Note: $wtPids (not $pids) - PID is a read-only automatic variable in PowerShell.
    # B5: the scope is the shared production rule (WindowScope.Filter with the stable
    # key resolved once below) - never a monitor-number match re-implemented here.
    $wtPids = [int[]]@([System.Diagnostics.Process]::GetProcessesByName('WindowsTerminal') | ForEach-Object { $_.Id })
    $sessions = [TerminalOrganizer.Core.Windows.CommandLineClassifier]::ClassifyAll($sessionQuery.QueryChildren($wtPids))
    $monitors = $monitorProvider.GetMonitors()
    $enumerated = $enumerator.Enumerate($wtPids, $desktopReader, $monitors)
    $scope = [TerminalOrganizer.Core.Windows.WindowScope]::Filter($enumerated, $stableKey, $false)
    $acquired = @()
    foreach ($w in $scope.Included) {
        $titles = $titleReader.ReadTabs($w.Handle)
        $acquired += , (New-Object TerminalOrganizer.Core.Windows.AcquiredWindow -ArgumentList $w.Handle, $titles, $w.Monitor, $w.DesktopStatus)
    }
    $snaps = [TerminalOrganizer.Core.Windows.WindowSnapshotBuilder]::Compose($acquired, $sessions)
    $facts = @()
    foreach ($snap in $snaps) {
        $state = $stateReader.Read($snap.Handle)
        $name = [TerminalOrganizer.Core.Windows.UiaTabTitleReader]::GetWindowTextTitle($snap.Handle)
        if ([string]::IsNullOrEmpty($name) -and $snap.Tabs.Length -gt 0) { $name = $snap.Tabs[0].Title }
        $facts += , [TerminalOrganizer.Core.Assignment.WindowFact]::new(
            ('w' + $snap.Handle), $snap.Handle, $name, $snap.Identified,
            $state.Left, $state.Top, $state.Width, $state.Height,
            $state.Maximized, $state.Minimized, $state.FullScreen)
    }
    New-Object TerminalOrganizer.App.DiscoveredWindows -ArgumentList $facts, $snaps
}

$assigner = [TerminalOrganizer.Core.Overflow.AssignmentComputation] {
    param($zones, $facts, $manager)
    [TerminalOrganizer.Core.Assignment.ZoneAssigner]::Assign($zones, $facts, $manager)
}
$planner = [TerminalOrganizer.App.MergePlanning] {
    param($plan, $snaps)
    [TerminalOrganizer.Core.Overflow.MergePlanner]::Plan($plan, $snaps)
}
$notify = [TerminalOrganizer.App.NoticeSink] { param($text) Write-Output ('notice: ' + $text) }

$settings = New-Object TerminalOrganizer.App.SettingsStore -ArgumentList ([TerminalOrganizer.App.SettingsStore]::DefaultPath())
$appSettings = $settings.Load()
$managerName = $appSettings.ManagerWindowName
$cmOverrides = [TerminalOrganizer.App.PriorityOverride[]]@($appSettings.PriorityOverrides)
$emptyLabels = New-Object TerminalOrganizer.App.LabelRegistry

$monitors = $monitorProvider.GetMonitors()
$chosen = @($monitors | Where-Object { $_.Number -eq $Monitor }) | Select-Object -First 1
if ($null -eq $chosen) {
    # B5: a missing requested monitor is exit 1 (invalid arguments), not a silent exit.
    [Console]::Error.WriteLine('ERROR: requested monitor ' + $Monitor + ' not found among ' + $monitors.Length + ' monitor(s)')
    exit 1
}
# B5: the stable key is resolved exactly ONCE; every later scope reuses it (A4 parity:
# monitor NUMBER is a write-time snapshot, never a live identity).
$stableKey = $chosen.StableKey

if ($WhatIf) {
    Write-Output 'whatif: plan-only mode - BOTH mutation ports swapped (merge executor + placer are recording fakes)'
    $script:PlannedMerges = New-Object 'System.Collections.Generic.List[string]'
    $script:PlannedMoves = New-Object 'System.Collections.Generic.List[string]'
    $recordingExecutor = [TerminalOrganizer.Core.Overflow.MergeExecution] {
        param($merge)
        $script:PlannedMerges.Add($merge.ToString())
        [TerminalOrganizer.Core.Overflow.MergeOutcome]::Merged($merge.SourceWindowId)
    }
    $recordingPlacer = [TerminalOrganizer.Core.Overflow.PlacementPass] {
        param($plan)
        foreach ($move in $plan.Moves) { $script:PlannedMoves.Add($move.ToString()) }
        @()
    }
    $planOnlyProbe = [TerminalOrganizer.App.HelperExistsProbe] { param($m) $true }

    $controller = New-Object TerminalOrganizer.App.OrganizeController -ArgumentList `
        $desktopSrc, $layoutRes, $windowDisc, $assigner, $planner, $planOnlyProbe, $recordingExecutor, $recordingPlacer, $notify, $emptyLabels
    try { $result = $controller.Run($chosen, $managerName) }
    catch {
        [Console]::Error.WriteLine('ERROR: acquisition failed: ' + $_.Exception.Message)
        exit 2
    }

    Write-Output ('whatif: monitor #' + $chosen.Number + ' manager=' + $(if ($null -ne $managerName) { $managerName } else { '<no choice>' }))
    Write-Output ('whatif: merges planned=' + $script:PlannedMerges.Count + ' executed=0 (recording fake; no wt launch)')
    foreach ($m in $script:PlannedMerges) { Write-Output ('  planned merge: ' + $m) }
    Write-Output ('whatif: moves planned=' + $script:PlannedMoves.Count + ' issued=0 (recording fake; no SetWindowPos)')
    foreach ($m in $script:PlannedMoves) { Write-Output ('  planned move: ' + $m) }

    # C2: the pure cross-monitor redistribution preview across ALL monitors (read-only
    # acquisition; the plan is computed and printed, never applied - C3 selects policy).
    $cmMonitors = New-Object 'System.Collections.Generic.List[object]'
    $cmZones = New-Object 'System.Collections.Generic.List[object]'
    $cmLabels = New-Object 'System.Collections.Generic.List[string]'
    $cmPlans = New-Object 'System.Collections.Generic.List[object]'
    $cmFacts = New-Object 'System.Collections.Generic.List[object]'
    $cmSnaps = New-Object 'System.Collections.Generic.List[object]'
    $cmDesktop = $desktopSrc.Invoke()
    $cmPids = [int[]]@([System.Diagnostics.Process]::GetProcessesByName('WindowsTerminal') | ForEach-Object { $_.Id })
    $cmWindows = $enumerator.Enumerate($cmPids, $desktopReader, $monitors)
    $cmStateRead = [Func[IntPtr, TerminalOrganizer.Core.Assignment.WindowState]] { param($h) $stateReader.Read($h) }
    $cmTabRead = [TerminalOrganizer.Core.Overflow.TabTitleRead] { param($h) $titleReader.ReadTabs($h) }
    $cmSessionsRead = [TerminalOrganizer.App.SessionRecordsRead] {
        # Note: $processIds (not $pids) - PID is a read-only automatic variable in
        # PowerShell and variable names are case-insensitive.
        param($processIds)
        [TerminalOrganizer.Core.Windows.CommandLineClassifier]::ClassifyAll($sessionQuery.QueryChildren([int[]]$processIds))
    }
    foreach ($cmMon in $monitors) {
        $cmLayout = $layoutRes.Invoke($cmMon, $cmDesktop)
        if ($null -eq $cmLayout -or $cmLayout.Kind.ToString() -ne 'Supported') { continue }
        $cmDiscovered = [TerminalOrganizer.App.DiscoveryPolicy]::Acquire(
            $cmWindows, $cmMon, $cmStateRead, $cmTabRead, $cmSessionsRead, $null)
        $cmLocalPlan = [TerminalOrganizer.Core.Assignment.ZoneAssigner]::Assign(
            $cmLayout.Zones, $cmDiscovered.Facts, $managerName)
        $cmMonitors.Add($cmMon)
        $cmZones.Add($cmLayout.Zones)
        $cmLabels.Add([TerminalOrganizer.App.TrayMenuBuilder]::DerivePhysicalLabel($cmMon, $monitors))
        $cmPlans.Add($cmLocalPlan)
        foreach ($cmFact in $cmDiscovered.Facts) { $cmFacts.Add($cmFact) }
        foreach ($cmSnap in $cmDiscovered.Snapshots) { $cmSnaps.Add($cmSnap) }
    }
    if ($cmMonitors.Count -gt 0) {
        $cmSnapshot = [TerminalOrganizer.App.CrossMonitorSnapshotComposer]::Compose(
            $cmDesktop, $cmMonitors.ToArray(), $cmZones.ToArray(), $cmLabels.ToArray(),
            $cmPlans.ToArray(), $cmWindows, $cmFacts.ToArray(), $cmSnaps.ToArray(), $cmOverrides)
        $cmRedistribution = [TerminalOrganizer.Core.Overflow.CrossMonitorPlanner]::Plan($cmSnapshot)
        Write-Output ('whatif: redistribute planned=' + $cmRedistribution.Moves.Length + ' applied=0 (pure plan; C3 selects policy)')
        foreach ($cmMove in $cmRedistribution.Moves) {
            Write-Output ('  ' + $cmMove.ToWhatIfText().Replace("`n", "`n  "))
        }
    }
    if (-not $result.Completed) {
        Write-Output 'whatif: the run stopped at the layout gate (see the notice above); nothing was changed'
        [Console]::Error.WriteLine('ERROR: the requested plan could not be built (invalid/unsupported layout); nothing was changed')
        exit 3
    }
    Write-Output ('whatif: this is the plan only - nothing was changed; the live run is a ' + $ManualLabel)
    exit 0
}

# --- Live mode: ONE real organize. MORNING CHECKLIST ONLY - MANUAL VERIFICATION. ---
Write-Output ('live: ONE real organize on monitor #' + $chosen.Number + '; ' + $ManualLabel)

$realProbe = [TerminalOrganizer.App.HelperExistsProbe] {
    param($merge)
    # Bounded wsl test -f probe (REQ-PIPE-005, fail-open); the probe process is never killed.
    try {
        if ($null -eq $merge -or $null -eq $merge.Session -or [string]::IsNullOrEmpty($merge.Session.CommandLine)) { return $null }
        $tokens = $merge.Session.CommandLine -split ' '
        $distro = $null
        $scriptPath = $null
        for ($i = 0; $i -lt $tokens.Count; $i++) {
            if ($tokens[$i] -ceq '-d' -and $i + 1 -lt $tokens.Count) { $distro = $tokens[$i + 1] }
            elseif ($tokens[$i] -ceq '--exec' -and $i + 1 -lt $tokens.Count) { $scriptPath = $tokens[$i + 1]; break }
        }
        if ([string]::IsNullOrEmpty($distro) -or [string]::IsNullOrEmpty($scriptPath)) { return $null }
        $probeProc = Start-Process -FilePath 'wsl.exe' -ArgumentList @('-d', $distro, '--exec', '/usr/bin/test', '-f', $scriptPath) -PassThru -WindowStyle Hidden
        if ($probeProc.WaitForExit(2000)) { return ($probeProc.ExitCode -eq 0) }
        return $null
    }
    catch {
        return $null
    }
}

$realMergeExecutor = New-Object TerminalOrganizer.Core.Overflow.MergeExecutor
$realExecutor = [TerminalOrganizer.Core.Overflow.MergeExecution] { param($merge) $realMergeExecutor.RunMerge($merge) }
$realPlacerInstance = New-Object TerminalOrganizer.Core.Assignment.WindowPlacer
$realPlacer = [TerminalOrganizer.Core.Overflow.PlacementPass] { param($plan) $realPlacerInstance.Apply($plan) }

$controller = New-Object TerminalOrganizer.App.OrganizeController -ArgumentList `
    $desktopSrc, $layoutRes, $windowDisc, $assigner, $planner, $realProbe, $realExecutor, $realPlacer, $notify, $emptyLabels
try { $result = $controller.Run($chosen, $managerName) }
catch {
    [Console]::Error.WriteLine('ERROR: acquisition failed: ' + $_.Exception.Message)
    exit 2
}
Write-Output ('live: completed=' + $result.Completed + ' outcomes=' + $result.Outcomes.Length + ' helper-skips=' + $result.HelperMissingSkips + ' placed=' + $result.PlacementResults.Length)
Write-Output $ManualLabel
if (-not $result.Completed) {
    # B5: a run that stopped at the layout gate never built the requested plan.
    [Console]::Error.WriteLine('ERROR: the requested plan could not be built (invalid/unsupported layout); nothing was changed')
    exit 3
}
exit 0
