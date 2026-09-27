# SPEC-OVERFLOW-006 acceptance suite (Pester 3.4.0, Windows PowerShell 5.1).
# Run through test.ps1, which builds first and starts a fresh powershell.exe.
# Expected values come from acceptance.md and plan.md section J, never from the code under test.

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here
$dllPath = Join-Path $repoRoot 'bin\TerminalOrganizer.Core.dll'

# Byte-load keeps the DLL file unlocked so build.ps1 can rebuild while this session runs.
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dllPath))

# Pinned command lines (plan.md J.2 + SPEC-WIN-004 J.1 recorded shapes).
$CMD_LOCAL_YODA3 = 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA3 20'
$CMD_LOCAL_YODA4 = 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA4 20'
$CMD_REMOTE = 'wsl.exe -d Ubuntu --exec /tmp/rdl_attach.sh user@host YODA2'

# Standing zone set (SPEC-ZONE-005 plan.md J.1): Zone(id, left, top, width, height).
# ZoneOrdering order: z1 (largest) first, then z0, then z2; fill order after the manager: z0, then z2.
function New-StandingZones {
    @(
        [TerminalOrganizer.Core.Geometry.Zone]::new(0, 16, 16, 456, 1120),   # z0: 456x1120 @ 16,16
        [TerminalOrganizer.Core.Geometry.Zone]::new(1, 488, 16, 944, 1120),  # z1: 944x1120 @ 488,16 (largest)
        [TerminalOrganizer.Core.Geometry.Zone]::new(2, 1448, 16, 456, 1120)  # z2: 456x1120 @ 1448,16
    )
}

$script:OverflowHandleSeed = 5000
$script:OverflowHandles = @{}

# One WindowFact; rect input is the plan.md J.1 form (top, left); name defaults to the id.
function New-OFact {
    param(
        [string]$Id,
        [int]$Top = 0,
        [int]$Left = 0,
        [bool]$Identified = $true
    )
    $script:OverflowHandleSeed = $script:OverflowHandleSeed + 1
    $script:OverflowHandles[$Id] = [IntPtr]$script:OverflowHandleSeed
    [TerminalOrganizer.Core.Assignment.WindowFact]::new(
        $Id, [IntPtr]$script:OverflowHandleSeed, $Id, $Identified,
        $Left, $Top, 400, 300, $false, $false, $false)
}

function New-OSession([string]$Name, [string]$Kind, [string]$CommandLine) {
    $kindEnum = [TerminalOrganizer.Core.Windows.SessionKind]::Parse([TerminalOrganizer.Core.Windows.SessionKind], $Kind)
    New-Object TerminalOrganizer.Core.Windows.SessionRecord -ArgumentList $Name, $kindEnum, $CommandLine
}

# One WindowSnapshot joined to the fact by handle: single-tab variants carry their matched
# session; the multi-tab variant carries two; the unidentified variant carries none.
function New-OSnap {
    param(
        [string]$Id,
        [string[]]$TabTitles,
        [TerminalOrganizer.Core.Windows.SessionRecord[]]$TabSessions,
        [bool]$Identified,
        [bool]$Mergeable
    )
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
        ([TerminalOrganizer.Core.Windows.WindowIdentity]::new($script:OverflowHandles[$Id], 42, 12345, 'CASCADIA_HOSTING_WINDOW_CLASS', $TabTitles[0])), $null, $null, $tabs.ToArray(), $Identified, [string[]]$unmatched, $Mergeable, ([TerminalOrganizer.Core.Windows.TabReadQuality]::Trusted)
}

# The plan.md J.1 standing scenario: 8 windows (MGR, A..G) with pinned (top,left) rects.
function New-StandingFacts {
    @(
        (New-OFact 'MGR' -Top 600 -Left 100),
        (New-OFact 'A'   -Top 0   -Left 0),
        (New-OFact 'B'   -Top 0   -Left 500),
        (New-OFact 'C'   -Top 400 -Left 0),
        (New-OFact 'D'   -Top 800 -Left 0 -Identified:$false),
        (New-OFact 'E'   -Top 900 -Left 0),
        (New-OFact 'F'   -Top 1000 -Left 0),
        (New-OFact 'G'   -Top 1100 -Left 0)
    )
}

function New-StandingSnaps {
    @(
        (New-OSnap 'MGR' @('OC_MGR')  @((New-OSession 'MGR' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach MGR 20')) $true $true),
        (New-OSnap 'A'   @('OC_YODA1') @((New-OSession 'YODA1' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA1 20')) $true $true),
        (New-OSnap 'B'   @('OC_YODA2') @((New-OSession 'YODA2' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA2 20')) $true $true),
        (New-OSnap 'C'   @('NC_OPS1')  @((New-OSession 'OPS1' 'WindowsNative' 'powershell.exe -NoProfile -Command "& { $env:LAUNCHER_SESSION=''OPS1''; Get-Date }"')) $true $false),
        (New-OSnap 'D'   @('WD_UNK1')  @() $false $false),
        (New-OSnap 'E'   @('OC_YODA3') @((New-OSession 'YODA3' 'Local' $CMD_LOCAL_YODA3)) $true $true),
        (New-OSnap 'F'   @('OC_YODA4') @((New-OSession 'YODA4' 'Local' $CMD_LOCAL_YODA4)) $true $true),
        (New-OSnap 'G'   @('OC_YODA1', 'HC_YODA2') @((New-OSession 'YODA1' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA1 20'), (New-OSession 'YODA2' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA2 20')) $true $false)
    )
}

function Invoke-OverflowPlan($Facts, $Snaps) {
    $zones = New-StandingZones
    $assignment = [TerminalOrganizer.Core.Assignment.ZoneAssigner]::Assign($zones, $Facts, 'MGR')
    [TerminalOrganizer.Core.Overflow.MergePlanner]::Plan($assignment, $Snaps)
}

Describe 'AC-001 Merge planning happy path' {
    It 'the J.1 standing scenario yields target B with sources [E, F] in position order, each carrying its session record and full command line' {
        $facts = New-StandingFacts
        $snaps = New-StandingSnaps
        $plan = Invoke-OverflowPlan $facts $snaps
        $merges = @($plan.Merges)
        $merges.Count | Should Be 2
        $merges[0].SourceWindowId | Should BeExactly 'E'
        $merges[1].SourceWindowId | Should BeExactly 'F'
        $merges[0].TargetWindowId | Should BeExactly 'B'
        $merges[1].TargetWindowId | Should BeExactly 'B'
        $merges[0].Session | Should Not BeNullOrEmpty
        $merges[0].Session.Name | Should BeExactly 'YODA3'
        $merges[1].Session.Name | Should BeExactly 'YODA4'
        $merges[0].CommandLine | Should BeExactly $CMD_LOCAL_YODA3
        $merges[1].CommandLine | Should BeExactly $CMD_LOCAL_YODA4
        $merges[0].SourceHandle.ToInt64() | Should Be $script:OverflowHandles['E'].ToInt64()
        $merges[0].TargetHandle.ToInt64() | Should Be $script:OverflowHandles['B'].ToInt64()
    }
}

Describe 'AC-002 Never-close sources' {
    It 'manager (MGR), multi-tab (G), unidentified (D) and Windows-native (C) are absent from every source list' {
        $facts = New-StandingFacts
        $snaps = New-StandingSnaps
        $plan = Invoke-OverflowPlan $facts $snaps
        $sources = @($plan.Merges | ForEach-Object { $_.SourceWindowId })
        $sources.Count | Should Be 2
        @($sources | Where-Object { $_ -ceq 'MGR' }).Count | Should Be 0
        @($sources | Where-Object { $_ -ceq 'G' }).Count | Should Be 0
        @($sources | Where-Object { $_ -ceq 'D' }).Count | Should Be 0
        @($sources | Where-Object { $_ -ceq 'C' }).Count | Should Be 0
    }
}

Describe 'AC-003 Unidentified base -> stack-only' {
    It 'when the stack base is unidentified, no merges are planned for that zone and the zone is marked stack-only' {
        $facts = @(
            (New-OFact 'MGR' -Top 600 -Left 100),
            (New-OFact 'A'   -Top 0   -Left 0),
            (New-OFact 'B'   -Top 0   -Left 500 -Identified:$false),
            (New-OFact 'C'   -Top 400 -Left 0),
            (New-OFact 'D'   -Top 800 -Left 0)
        )
        $snaps = @(
            (New-OSnap 'MGR' @('OC_MGR')  @((New-OSession 'MGR' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach MGR 20')) $true $true),
            (New-OSnap 'A'   @('OC_YODA1') @((New-OSession 'YODA1' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA1 20')) $true $true),
            (New-OSnap 'B'   @('WD_UNK1')  @() $false $false),
            (New-OSnap 'C'   @('OC_YODA3') @((New-OSession 'YODA3' 'Local' $CMD_LOCAL_YODA3)) $true $true),
            (New-OSnap 'D'   @('OC_YODA4') @((New-OSession 'YODA4' 'Local' $CMD_LOCAL_YODA4)) $true $true)
        )
        $plan = Invoke-OverflowPlan $facts $snaps
        @($plan.Merges).Count | Should Be 0
        @($plan.StackOnlyZoneIds | Where-Object { $_ -eq 2 }).Count | Should Be 1
    }

    It 'with no stacked windows at all, the plan is empty' {
        $facts = @(
            (New-OFact 'MGR' -Top 600 -Left 100),
            (New-OFact 'A'   -Top 0   -Left 0),
            (New-OFact 'B'   -Top 0   -Left 500)
        )
        $snaps = @(
            (New-OSnap 'MGR' @('OC_MGR')  @((New-OSession 'MGR' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach MGR 20')) $true $true),
            (New-OSnap 'A'   @('OC_YODA1') @((New-OSession 'YODA1' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA1 20')) $true $true),
            (New-OSnap 'B'   @('OC_YODA2') @((New-OSession 'YODA2' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA2 20')) $true $true)
        )
        $plan = Invoke-OverflowPlan $facts $snaps
        @($plan.Merges).Count | Should Be 0
        @($plan.StackOnlyZoneIds).Count | Should Be 0
    }
}

Describe 'AC-004 Full command line + escape' {
    It 'the builder produces the J.2 Local line prefixed with new-tab, byte-identical (including 20)' {
        $built = [TerminalOrganizer.Core.Overflow.MergeCommandBuilder]::Build($CMD_LOCAL_YODA3)
        $built | Should BeExactly 'new-tab wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA3 20'
    }

    It 'the J.2 Remote line builds with its image token kept' {
        $built = [TerminalOrganizer.Core.Overflow.MergeCommandBuilder]::Build($CMD_REMOTE)
        $built | Should BeExactly 'new-tab wsl.exe -d Ubuntu --exec /tmp/rdl_attach.sh user@host YODA2'
    }

    It 'the same input yields the same string on every call' {
        $first = [TerminalOrganizer.Core.Overflow.MergeCommandBuilder]::Build($CMD_LOCAL_YODA3)
        $second = [TerminalOrganizer.Core.Overflow.MergeCommandBuilder]::Build($CMD_LOCAL_YODA3)
        $second | Should BeExactly $first
    }

    It 'a command line containing quotes survives unchanged' {
        $built = [TerminalOrganizer.Core.Overflow.MergeCommandBuilder]::Build('wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --title "My X"')
        $built | Should BeExactly 'new-tab wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --title "My X"'
    }

    It 'every semicolon is escaped as \; with nothing else changed (run.sh;extra -> run.sh\;extra)' {
        $built = [TerminalOrganizer.Core.Overflow.MergeCommandBuilder]::Build('wsl.exe run.sh;extra')
        $built | Should BeExactly 'new-tab wsl.exe run.sh\;extra'
    }

    It 'the manager is never a merge target: a one-zone scenario whose only non-manager occupant is unidentified plans no merges' {
        $zones = @([TerminalOrganizer.Core.Geometry.Zone]::new(9, 0, 0, 1920, 1040))
        $facts = @(
            (New-OFact 'MGR' -Top 600 -Left 100),
            (New-OFact 'D'   -Top 800 -Left 0 -Identified:$false)
        )
        $assignment = [TerminalOrganizer.Core.Assignment.ZoneAssigner]::Assign($zones, $facts, 'MGR')
        $snaps = @(
            (New-OSnap 'MGR' @('OC_MGR')  @((New-OSession 'MGR' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach MGR 20')) $true $true),
            (New-OSnap 'D'   @('WD_UNK1') @() $false $false)
        )
        $plan = [TerminalOrganizer.Core.Overflow.MergePlanner]::Plan($assignment, $snaps)
        @($plan.Merges).Count | Should Be 0
    }
}

function New-A1Identity([long]$Handle = 7001, [int]$ProcessId = 42, [string]$Class = 'CASCADIA_HOSTING_WINDOW_CLASS', [string]$Title = 'OC_YODA3', [long]$Ticks = 12345) {
    [TerminalOrganizer.Core.Windows.WindowIdentity]::new([IntPtr]$Handle, $ProcessId, $Ticks, $Class, $Title)
}

function New-A1Tabs([string[]]$Titles, [string[]]$Keys = @()) {
    $items = New-Object 'System.Collections.Generic.List[TerminalOrganizer.Core.Windows.TabEvidence]'
    for ($i = 0; $i -lt $Titles.Count; $i++) {
        $key = $null
        if ($i -lt $Keys.Count) { $key = $Keys[$i] }
        $items.Add([TerminalOrganizer.Core.Windows.TabEvidence]::new($key, $Titles[$i]))
    }
    [TerminalOrganizer.Core.Windows.TabTitleResult]::TrustedResult($items.ToArray())
}

Describe 'A1 merge close evidence' {
    It 'target baseline does not accept a pre-existing matching title or reordering' {
        $baseline = [TerminalOrganizer.Core.Overflow.TargetTabBaseline]::new((New-A1Identity 7002), (New-A1Tabs @('OC_YODA3', 'OTHER') @('1.2', '1.3')).Evidence)
        [TerminalOrganizer.Core.Overflow.MergeEvidence]::HasNewMatchingTab($baseline, (New-A1Tabs @('OC_YODA3', 'OTHER') @('1.2', '1.3')), 'YODA3') | Should Be $false
        [TerminalOrganizer.Core.Overflow.MergeEvidence]::HasNewMatchingTab($baseline, (New-A1Tabs @('OTHER', 'OC_YODA3') @('1.3', '1.2')), 'YODA3') | Should Be $false
    }
    It 'one new matching runtime id confirms target delta' {
        $baseline = [TerminalOrganizer.Core.Overflow.TargetTabBaseline]::new((New-A1Identity 7002), (New-A1Tabs @('OTHER') @('1.2')).Evidence)
        [TerminalOrganizer.Core.Overflow.MergeEvidence]::HasNewMatchingTab($baseline, (New-A1Tabs @('OTHER', 'OC_YODA3') @('1.2', '1.3')), 'YODA3') | Should Be $true
    }
    It 'matching-title multiplicity increase confirms without runtime ids' {
        $baseline = [TerminalOrganizer.Core.Overflow.TargetTabBaseline]::new((New-A1Identity 7002), (New-A1Tabs @('OC_YODA3')).Evidence)
        $baseline.Tabs.Count | Should Be 1
        $post = New-A1Tabs @('OC_YODA3', 'OC_YODA3')
        $post.ObservedTabItemCount | Should Be 2
        [TerminalOrganizer.Core.Overflow.MergeEvidence]::HasNewMatchingTab($baseline, $post, 'YODA3') | Should Be $true
    }
    It 'changed source PID class title session or start time blocks close; exact identity closes once' {
        $merge = [TerminalOrganizer.Core.Overflow.PlannedMerge]::new('E', (New-A1Identity), 'B', (New-A1Identity 7002), (New-OSession 'YODA3' 'Local' $CMD_LOCAL_YODA3), 'OC_YODA3')
        $baseline = [TerminalOrganizer.Core.Overflow.TargetTabBaseline]::new($merge.TargetIdentity, (New-A1Tabs @('OTHER') @('1.2')).Evidence)
        $script:A1Target = [TerminalOrganizer.Core.Windows.WindowInspection]::new($merge.TargetIdentity, (New-A1Tabs @('OTHER', 'OC_YODA3') @('1.2', '1.3')))
        $reader = [TerminalOrganizer.Core.Windows.WindowInspectionRead]{ param($h) if ($h.ToInt64() -eq 7001) { $script:A1Source } else { $script:A1Target } }
        $post = [Func[IntPtr,bool]]{ param($h) $script:A1Closes++; $true }
        $executor = [TerminalOrganizer.Core.Overflow.MergeExecutor]::new($reader, $post)
        foreach ($identity in @((New-A1Identity -ProcessId 43), (New-A1Identity -Class 'OTHER'), (New-A1Identity -Title 'changed'), (New-A1Identity -Ticks 67890), (New-A1Identity -Ticks 0))) {
            $script:A1Closes = 0
            $script:A1Source = [TerminalOrganizer.Core.Windows.WindowInspection]::new($identity, (New-A1Tabs @('OC_YODA3')))
            $executor.VerifyAndClose($merge, $baseline).ClosePosted | Should Be $false
            $script:A1Closes | Should Be 0
        }
        $script:A1Source = [TerminalOrganizer.Core.Windows.WindowInspection]::new($merge.SourceIdentity, (New-A1Tabs @('OC_OTHER')))
        $executor.VerifyAndClose($merge, $baseline).ClosePosted | Should Be $false
        $script:A1Closes | Should Be 0
        $script:A1Source = [TerminalOrganizer.Core.Windows.WindowInspection]::new($merge.SourceIdentity, (New-A1Tabs @('OC_YODA3')))
        $executor.VerifyAndClose($merge, $baseline).ClosePosted | Should Be $true
        $script:A1Closes | Should Be 1
    }
    It 'legacy handle-only plans fail closed before launch' {
        $merge = [TerminalOrganizer.Core.Overflow.PlannedMerge]::new('E', [IntPtr]7001, 'B', [IntPtr]7002, (New-OSession 'YODA3' 'Local' $CMD_LOCAL_YODA3))
        ([TerminalOrganizer.Core.Overflow.MergeExecutor]::new()).RunMerge($merge).Kind.ToString() | Should BeExactly 'SafetyCheckFailed'
    }
    It 'lost target delta and untrusted source or target never post close' {
        $merge = [TerminalOrganizer.Core.Overflow.PlannedMerge]::new('E', (New-A1Identity), 'B', (New-A1Identity 7002), (New-OSession 'YODA3' 'Local' $CMD_LOCAL_YODA3), 'OC_YODA3')
        $baseline = [TerminalOrganizer.Core.Overflow.TargetTabBaseline]::new($merge.TargetIdentity, (New-A1Tabs @('OTHER')).Evidence)
        $script:A1Source = [TerminalOrganizer.Core.Windows.WindowInspection]::new($merge.SourceIdentity, (New-A1Tabs @('OC_YODA3')))
        $reader = [TerminalOrganizer.Core.Windows.WindowInspectionRead]{ param($h) if ($h.ToInt64() -eq 7001) { $script:A1Source } else { $script:A1Target } }
        $script:A1Closes = 0
        $executor = [TerminalOrganizer.Core.Overflow.MergeExecutor]::new($reader, [Func[IntPtr,bool]]{ param($h) $script:A1Closes++; $true })
        foreach ($target in @(
            [TerminalOrganizer.Core.Windows.WindowInspection]::new($merge.TargetIdentity, (New-A1Tabs @('OTHER'))),
            [TerminalOrganizer.Core.Windows.WindowInspection]::new((New-A1Identity 7002 -ProcessId 43), (New-A1Tabs @('OTHER', 'OC_YODA3'))),
            [TerminalOrganizer.Core.Windows.WindowInspection]::new($merge.TargetIdentity, [TerminalOrganizer.Core.Windows.TabTitleResult]::Failure('OC_YODA3', 'failed'))
        )) {
            $script:A1Target = $target
            $executor.VerifyAndClose($merge, $baseline).ClosePosted | Should Be $false
        }
        $script:A1Target = [TerminalOrganizer.Core.Windows.WindowInspection]::new($merge.TargetIdentity, (New-A1Tabs @('OTHER', 'OC_YODA3')))
        $script:A1Source = [TerminalOrganizer.Core.Windows.WindowInspection]::new($merge.SourceIdentity, [TerminalOrganizer.Core.Windows.TabTitleResult]::Failure('OC_YODA3', 'failed'))
        $executor.VerifyAndClose($merge, $baseline).ClosePosted | Should Be $false
        $script:A1Closes | Should Be 0
    }
}

function New-OMerge {
    [TerminalOrganizer.Core.Overflow.PlannedMerge]::new('E', (New-A1Identity), 'B', (New-A1Identity 7002), (New-OSession 'YODA3' 'Local' $CMD_LOCAL_YODA3), 'OC_YODA3')
}

function Add-OBaseline($step) {
    $baseline = [TerminalOrganizer.Core.Overflow.TargetTabBaseline]::new($step.Merge.TargetIdentity, (New-A1Tabs @('NG_OTHER')).Evidence)
    [TerminalOrganizer.Core.Overflow.MergeSequencer]::Next($step, [TerminalOrganizer.Core.Overflow.MergeObservation]::Baseline($baseline))
}

Describe 'AC-005 Sequencing walk' {
    It 'fresh step -> launch; launched -> poll; no tabs yet with budget remaining -> poll; session-name tab -> verify-source; source single-tab -> close; closed -> merge complete' {
        $step = [TerminalOrganizer.Core.Overflow.MergeSequencer]::Start((New-OMerge))
        $step.PendingAction.ToString() | Should BeExactly 'CaptureBaseline'
        $step = Add-OBaseline $step
        $step.Phase.ToString() | Should BeExactly 'AwaitingLaunch'
        $step.PendingAction.ToString() | Should BeExactly 'Launch'
        $step.IsTerminal | Should Be $false

        $step = [TerminalOrganizer.Core.Overflow.MergeSequencer]::Next($step, [TerminalOrganizer.Core.Overflow.MergeObservation]::Launched($true, $null))
        $step.Phase.ToString() | Should BeExactly 'Polling'
        $step.PendingAction.ToString() | Should BeExactly 'Poll'

        $step = [TerminalOrganizer.Core.Overflow.MergeSequencer]::Next($step, [TerminalOrganizer.Core.Overflow.MergeObservation]::Tabs($step.Merge.TargetIdentity, (New-A1Tabs @('NG_OTHER')), 9750))
        $step.PendingAction.ToString() | Should BeExactly 'Poll'
        $step.PollCount | Should Be 1
        $step.IsTerminal | Should Be $false

        $step = [TerminalOrganizer.Core.Overflow.MergeSequencer]::Next($step, [TerminalOrganizer.Core.Overflow.MergeObservation]::Tabs($step.Merge.TargetIdentity, (New-A1Tabs @('NG_OTHER', 'OC_YODA3')), 9500))
        $step.Phase.ToString() | Should BeExactly 'ConfirmingSource'
        $step.PendingAction.ToString() | Should BeExactly 'VerifyAndClose'

        $step = [TerminalOrganizer.Core.Overflow.MergeSequencer]::Next($step, [TerminalOrganizer.Core.Overflow.MergeObservation]::CloseGate($true, $null))
        $step.Phase.ToString() | Should BeExactly 'AwaitingClose'
        $step.PendingAction.ToString() | Should BeExactly 'ObserveClose'

        $step = [TerminalOrganizer.Core.Overflow.MergeSequencer]::Next($step, [TerminalOrganizer.Core.Overflow.MergeObservation]::Close($true))
        $step.IsTerminal | Should Be $true
        $step.Phase.ToString() | Should BeExactly 'Merged'
        $step.PendingAction.ToString() | Should BeExactly 'None'
        $step.Outcome | Should Not BeNullOrEmpty
        $step.Outcome.Kind.ToString() | Should BeExactly 'Merged'
        $step.Outcome.SourceWindowId | Should BeExactly 'E'
    }

    It 'Start pins the defaults: poll interval 250 ms, confirmation budget 10000 ms (plan.md B.3)' {
        $step = [TerminalOrganizer.Core.Overflow.MergeSequencer]::Start((New-OMerge))
        $step.PollIntervalMs | Should Be 250
        $step.BudgetMs | Should Be 10000
    }

    It 'the sequencer exposes no batch entry point (single-flight only: Start and Next)' {
        $methods = @([TerminalOrganizer.Core.Overflow.MergeSequencer].GetMethods([System.Reflection.BindingFlags]'Public,Static') |
            Where-Object { $_.DeclaringType -eq [TerminalOrganizer.Core.Overflow.MergeSequencer] } |
            ForEach-Object { $_.Name } | Sort-Object -Unique)
        ($methods -join ',') | Should BeExactly 'Next,Start'
    }
}

Describe 'AC-006 Abort paths' {
    It 'budget exhausted without a title match -> abort "confirm-timeout", no close action, source stays open' {
        $step = Add-OBaseline ([TerminalOrganizer.Core.Overflow.MergeSequencer]::Start((New-OMerge)))
        $step = [TerminalOrganizer.Core.Overflow.MergeSequencer]::Next($step, [TerminalOrganizer.Core.Overflow.MergeObservation]::Launched($true, $null))
        $step = [TerminalOrganizer.Core.Overflow.MergeSequencer]::Next($step, [TerminalOrganizer.Core.Overflow.MergeObservation]::Tabs($step.Merge.TargetIdentity, (New-A1Tabs @('NG_OTHER')), 0))
        $step.IsTerminal | Should Be $true
        $step.Phase.ToString() | Should BeExactly 'Aborted'
        $step.PendingAction.ToString() | Should BeExactly 'None'
        $step.Outcome.Kind.ToString() | Should BeExactly 'ConfirmTimeout'
        $step.Outcome.Detail | Should BeExactly 'confirm-timeout'
    }

    It 'close-gate failure (source now multi-tab) -> outcome SafetyCheckFailed, source stays open' {
        $step = Add-OBaseline ([TerminalOrganizer.Core.Overflow.MergeSequencer]::Start((New-OMerge)))
        $step = [TerminalOrganizer.Core.Overflow.MergeSequencer]::Next($step, [TerminalOrganizer.Core.Overflow.MergeObservation]::Launched($true, $null))
        $step = [TerminalOrganizer.Core.Overflow.MergeSequencer]::Next($step, [TerminalOrganizer.Core.Overflow.MergeObservation]::Tabs($step.Merge.TargetIdentity, (New-A1Tabs @('NG_OTHER', 'OC_YODA3')), 9000))
        $step.PendingAction.ToString() | Should BeExactly 'VerifyAndClose'
        $step = [TerminalOrganizer.Core.Overflow.MergeSequencer]::Next($step, [TerminalOrganizer.Core.Overflow.MergeObservation]::CloseGate($false, 'source now multi-tab'))
        $step.IsTerminal | Should Be $true
        $step.Phase.ToString() | Should BeExactly 'Aborted'
        $step.PendingAction.ToString() | Should BeExactly 'None'
        $step.Outcome.Kind.ToString() | Should BeExactly 'SafetyCheckFailed'
        $step.Outcome.Detail | Should BeExactly 'source now multi-tab'
    }
}

Describe 'AC-007 Executor surface and degradation' {
    It 'the DLL exposes MergeExecutor with MRU + titled modes, an injectable launcher image (production default wt.exe) and an injectable tab reader' {
        [TerminalOrganizer.Core.Overflow.MergeExecutor]::DefaultLauncherImage | Should BeExactly 'wt.exe'
        $default = New-Object TerminalOrganizer.Core.Overflow.MergeExecutor
        ($default -is [TerminalOrganizer.Core.Overflow.MergeExecutor]) | Should Be $true
        $fakeReader = [TerminalOrganizer.Core.Overflow.TabTitleRead]{ param($h) [TerminalOrganizer.Core.Windows.TabTitleResult]::Ok(@('X')) }
        $mru = New-Object TerminalOrganizer.Core.Overflow.MergeExecutor -ArgumentList 'wt.exe', ([TerminalOrganizer.Core.Overflow.MergeTargetingMode]::Mru), $null, $fakeReader, 250, 10000
        $titled = New-Object TerminalOrganizer.Core.Overflow.MergeExecutor -ArgumentList 'wt.exe', ([TerminalOrganizer.Core.Overflow.MergeTargetingMode]::Titled), 'ORG', $fakeReader, 250, 10000
        ($mru -is [TerminalOrganizer.Core.Overflow.MergeExecutor]) | Should Be $true
        ($titled -is [TerminalOrganizer.Core.Overflow.MergeExecutor]) | Should Be $true
    }

    It 'a nonexistent live window fails the baseline before launch, never an exception' {
        $fakeReader = [TerminalOrganizer.Core.Overflow.TabTitleRead]{ param($h) [TerminalOrganizer.Core.Windows.TabTitleResult]::Ok(@('X')) }
        $exec = New-Object TerminalOrganizer.Core.Overflow.MergeExecutor -ArgumentList 'definitely-not-a-real-image-zz.exe', ([TerminalOrganizer.Core.Overflow.MergeTargetingMode]::Mru), $null, $fakeReader, 250, 10000
        $caught = $null
        $outcome = $null
        try { $outcome = $exec.RunMerge((New-OMerge)) } catch { $caught = $_ }
        $caught | Should BeNullOrEmpty
        $outcome | Should Not BeNullOrEmpty
        $outcome.Kind.ToString() | Should BeExactly 'SafetyCheckFailed'
        $outcome.Detail | Should Not BeNullOrEmpty
    }

    It 'inspection rows: the executor uses SetForegroundWindow (MRU), PostMessage WM_CLOSE, Process.Start without a shell, and the WIN-004 UIA reader' {
        $src = [IO.File]::ReadAllText((Join-Path $repoRoot 'src\TerminalOrganizer.Core\Overflow\MergeExecutor.cs'))
        $src.Contains('SetForegroundWindow') | Should Be $true
        $src.Contains('PostMessage') | Should Be $true
        $src.Contains('WM_CLOSE') | Should Be $true
        $src.Contains('UseShellExecute = false') | Should Be $true
        $src.Contains('WindowInspector') | Should Be $true
    }
}

Describe 'AC-008 Overflow runner composition' {
    It 'A2 a full-screen skipped move is neither a target nor a source' {
        $facts = New-StandingFacts
        $snaps = New-StandingSnaps
        $assignment = [TerminalOrganizer.Core.Assignment.ZoneAssigner]::Assign((New-StandingZones), $facts, 'MGR')
        $moves = @($assignment.Moves | ForEach-Object {
            if ($_.WindowId -eq 'E') {
                [TerminalOrganizer.Core.Assignment.PlannedMove]::new($_.WindowId, $_.Handle, -1, 0, 0, 1920, 1080, $false, $false, $false, $true)
            } else { $_ }
        })
        $plan = [TerminalOrganizer.Core.Assignment.AssignmentPlan]::new($moves, $true, 'MGR', $null)
        $mergePlan = [TerminalOrganizer.Core.Overflow.MergePlanner]::Plan($plan, $snaps)
        @($mergePlan.Merges | Where-Object { $_.SourceWindowId -eq 'E' -or $_.TargetWindowId -eq 'E' }).Count | Should Be 0
        $mergePlan.Merges.Count | Should Be 1
    }
    It 'one successful + one aborted merge: both outcomes reported, survivors recomputed (failed-merge window present), exactly ONE placer pass over the survivors' {
        $facts = New-StandingFacts
        $snaps = New-StandingSnaps
        $zones = New-StandingZones
        $assignment = [TerminalOrganizer.Core.Assignment.ZoneAssigner]::Assign($zones, $facts, 'MGR')

        $script:RunnerLog = New-Object System.Collections.ArrayList
        $script:RecomputeWindows = $null
        $script:PlacerPlans = New-Object System.Collections.ArrayList

        $mergeExec = [TerminalOrganizer.Core.Overflow.MergeExecution]{ param($m)
            [void]$script:RunnerLog.Add('merge:' + $m.SourceWindowId)
            if ($m.SourceWindowId -ceq 'E') { return [TerminalOrganizer.Core.Overflow.MergeOutcome]::Merged($m.SourceWindowId) }
            return [TerminalOrganizer.Core.Overflow.MergeOutcome]::ConfirmTimeout($m.SourceWindowId) }

        $assigner = [TerminalOrganizer.Core.Overflow.AssignmentComputation]{ param($z, $w, $mgr)
            $script:RecomputeWindows = $w
            return [TerminalOrganizer.Core.Assignment.ZoneAssigner]::Assign($z, $w, $mgr) }

        $placer = [TerminalOrganizer.Core.Overflow.PlacementPass]{ param($p)
            [void]$script:PlacerPlans.Add($p)
            return @() }

        $runner = New-Object TerminalOrganizer.Core.Overflow.OverflowRunner -ArgumentList $mergeExec, $assigner, $placer
        $caught = $null
        $result = $null
        try { $result = $runner.Run($zones, $facts, 'MGR', $assignment, $snaps) } catch { $caught = $_ }
        $caught | Should BeNullOrEmpty

        @($result.Outcomes).Count | Should Be 2
        $result.Outcomes[0].Kind.ToString() | Should BeExactly 'Merged'
        $result.Outcomes[0].SourceWindowId | Should BeExactly 'E'
        $result.Outcomes[1].Kind.ToString() | Should BeExactly 'ConfirmTimeout'
        $result.Outcomes[1].SourceWindowId | Should BeExactly 'F'

        ($script:RunnerLog -join ',') | Should BeExactly 'merge:E,merge:F'

        @($script:RecomputeWindows).Count | Should Be 7
        @($script:RecomputeWindows | Where-Object { $_.Id -ceq 'F' }).Count | Should Be 1
        @($script:RecomputeWindows | Where-Object { $_.Id -ceq 'E' }).Count | Should Be 0

        @($script:PlacerPlans).Count | Should Be 1
        ([object]::ReferenceEquals($result.RecomputedPlan, $script:PlacerPlans[0])) | Should Be $true
    }
}

Describe 'AC-009 Merge test tool' {
    It 'tools/merge-test.ps1 -SelfTest under PS 5.1 exercises AC-001, AC-004, AC-005 and AC-006 rows, one PASS/FAIL line each, exit 0 only on all-pass' {
        $ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $tool = Join-Path $repoRoot 'tools\merge-test.ps1'
        $output = & $ps51 -NoProfile -ExecutionPolicy Bypass -File $tool -SelfTest
        $code = $LASTEXITCODE
        $lines = @($output | Where-Object { $_ -cmatch '^(PASS|FAIL):' })
        $lines.Count | Should BeGreaterThan 0
        @($lines | Where-Object { $_ -cmatch '^FAIL:' }).Count | Should Be 0
        $code | Should Be 0
    }

    It 'live mode is a supervised surface: takes -TargetHwnd and -CommandLine, is labelled manual verification, and the suite never runs it' {
        $tool = Join-Path $repoRoot 'tools\merge-test.ps1'
        $src = [IO.File]::ReadAllText($tool)
        $src.Contains('[long]$TargetHwnd') | Should Be $true
        $src.Contains('[string]$CommandLine') | Should Be $true
        $src.Contains('MANUAL VERIFICATION') | Should Be $true
        @(Select-String -LiteralPath $tool -Pattern 'MergeExecutor').Count | Should BeGreaterThan 0
    }
}

# --- B5: merge-test drill argument rules and the attach-only executor path
# (night-design-2026-09-25 unit B5, tests 7-9). The live drills themselves are
# morning-checklist manual surfaces; these rows pin argument validation and the
# attach-only executor contract with fakes (no real window is ever touched).

Describe 'B5 merge-test tool drills' {
    $ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $tool = Join-Path $repoRoot 'tools\merge-test.ps1'
    $cmdLine = 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA3 20'

    It 'B5 merge close drill rejects missing SourceHwnd' {
        $output = & $ps51 -NoProfile -ExecutionPolicy Bypass -File $tool -TargetHwnd 1 -CommandLine $cmdLine -SessionName YODA3 2>&1
        $code = $LASTEXITCODE
        $text = $output | Out-String
        $code | Should Be 1
        $text.Contains('CLOSE-DRILL requires -SourceHwnd') | Should Be $true
    }

    It 'B5 AttachOnly rejects SourceHwnd' {
        $output = & $ps51 -NoProfile -ExecutionPolicy Bypass -File $tool -AttachOnly -TargetHwnd 1 -SourceHwnd 2 -CommandLine $cmdLine -SessionName YODA3 2>&1
        $code = $LASTEXITCODE
        $text = $output | Out-String
        $code | Should Be 1
        $text.Contains('ATTACH-ONLY rejects -SourceHwnd') | Should Be $true
    }

    It 'B5 AttachOnly never calls close seam' {
        # Fake ports: the inspect seam returns the pinned target identity with a
        # stateful tab read (baseline/launch pre-check see one tab; the poll sees the
        # delta), the launch seam starts nothing, the close seam only records.
        $script:B5Target = [TerminalOrganizer.Core.Windows.WindowIdentity]::new(
            [IntPtr]7002, 42, 12345, 'CASCADIA_HOSTING_WINDOW_CLASS', 'OC_YODA3')
        $merge = [TerminalOrganizer.Core.Overflow.PlannedMerge]::new(
            'b5-attach', $null, 'b5-target', $script:B5Target, (New-OSession 'YODA3' 'Local' $CMD_LOCAL_YODA3), $null)
        $script:B5ReadCount = 0
        $script:B5Closes = 0
        $inspect = [TerminalOrganizer.Core.Windows.WindowInspectionRead] {
            param($h)
            $script:B5ReadCount = $script:B5ReadCount + 1
            if ($script:B5ReadCount -le 2) {
                [TerminalOrganizer.Core.Windows.WindowInspection]::new($script:B5Target, (New-A1Tabs @('NG_OTHER')))
            }
            else {
                [TerminalOrganizer.Core.Windows.WindowInspection]::new($script:B5Target, (New-A1Tabs @('NG_OTHER', 'OC_YODA3')))
            }
        }
        $postClose = [Func[IntPtr,bool]] { param($h) $script:B5Closes = $script:B5Closes + 1; $true }
        $launch = [Func[System.Diagnostics.ProcessStartInfo,System.Diagnostics.Process]] { param($info) $null }
        $executor = [TerminalOrganizer.Core.Overflow.MergeExecutor]::new($inspect, $postClose, $launch, 250, 2000)
        $outcome = $executor.RunAttachOnly($merge)
        $outcome.Kind.ToString() | Should BeExactly 'Attached'
        $outcome.SourceWindowId | Should BeExactly 'b5-attach'
        $script:B5Closes | Should Be 0
        $script:B5ReadCount | Should Be 3
    }
}
