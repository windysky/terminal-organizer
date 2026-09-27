# SPEC-OVERFLOW-006 supervised merge test tool (plan.md F file 7; acceptance AC-009; B5 drills).
# -SelfTest     : exercises the pure rows (AC-001, AC-004, AC-005, AC-006) against the pinned
#                 values from acceptance.md and plan.md section J; prints one PASS/FAIL line
#                 per check and exits 0 only when every check passes. Never touches a window.
# CLOSE-DRILL   : -TargetHwnd <h> -SourceHwnd <h> -CommandLine "<verbatim>" -SessionName <name>
#                 performs ONE supervised merge on real windows with A1's full identity
#                 inspection (both handles REQUIRED; the source closes only after the close
#                 gate passes). MORNING CHECKLIST ONLY - MANUAL VERIFICATION, NOT an
#                 acceptance claim.
# ATTACH-ONLY   : -AttachOnly -TargetHwnd <h> -CommandLine "<verbatim>" -SessionName <name>
#                 launches and confirms the new tab on the target and STOPS - the source
#                 verification and close steps are never entered (a source handle is
#                 REJECTED; there is no source). Also a manual-verification drill.
# Exit codes (B5): 0 expected outcome reached (CLOSE-DRILL: Merged / ATTACH-ONLY: Attached);
#                 1 invalid arguments (missing/rejected handle, missing command line or
#                 session); 4 identity capture failure or the drill did not reach its
#                 expected safe outcome.
param(
    [switch]$SelfTest,
    [switch]$AttachOnly,
    [long]$TargetHwnd = 0,
    [long]$SourceHwnd = 0,
    [string]$CommandLine,
    [string]$SessionName,
    [ValidateSet('Mru', 'Titled')]
    [string]$Mode = 'Mru',
    [string]$WindowTitle,
    [string]$LauncherImage = 'wt.exe',
    [int]$PollIntervalMs = 250,
    [int]$BudgetMs = 10000
)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here
$dllPath = Join-Path $repoRoot 'bin\TerminalOrganizer.Core.dll'

if (-not (Test-Path $dllPath)) {
    Write-Output ('FAIL: build output missing: ' + $dllPath + ' (run build.ps1 first)')
    exit 1
}
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dllPath))

# Pinned values (acceptance.md Conventions; plan.md J.1/J.2) - never re-derived from code under test.
$CMD_LOCAL_YODA3 = 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA3 20'

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

function New-TZone([int]$Id, [int]$Left, [int]$Top, [int]$Width, [int]$Height) {
    [TerminalOrganizer.Core.Geometry.Zone]::new($Id, $Left, $Top, $Width, $Height)
}

function New-TStandingZones {
    @( (New-TZone 0 16 16 456 1120), (New-TZone 1 488 16 944 1120), (New-TZone 2 1448 16 456 1120) )
}

$script:THandles = @{}
function New-TFact([string]$Id, [int]$Top, [int]$Left, [bool]$Identified) {
    $script:THandles[$Id] = [IntPtr]($script:THandles.Count + 9000)
    [TerminalOrganizer.Core.Assignment.WindowFact]::new(
        $Id, $script:THandles[$Id], $Id, $Identified, $Left, $Top, 400, 300, $false, $false, $false)
}

function New-TSession([string]$Name, [string]$Kind, [string]$CommandLine) {
    $kindEnum = [TerminalOrganizer.Core.Windows.SessionKind]::Parse([TerminalOrganizer.Core.Windows.SessionKind], $Kind)
    New-Object TerminalOrganizer.Core.Windows.SessionRecord -ArgumentList $Name, $kindEnum, $CommandLine
}

function New-TSnap([string]$Id, [string[]]$TabTitles, $TabSessions, [bool]$Identified, [bool]$Mergeable) {
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
    New-Object TerminalOrganizer.Core.Windows.WindowSnapshot -ArgumentList (New-TIdentity $script:THandles[$Id]), $null, $null, $tabs.ToArray(), $Identified, [string[]]$unmatched, $Mergeable, ([TerminalOrganizer.Core.Windows.TabReadQuality]::Trusted)
}

function New-TIdentity([IntPtr]$Handle) {
    [TerminalOrganizer.Core.Windows.WindowIdentity]::new($Handle, 42, 12345, 'CASCADIA_HOSTING_WINDOW_CLASS', 'fixture')
}
function New-TMerge {
    [TerminalOrganizer.Core.Overflow.PlannedMerge]::new('E', (New-TIdentity ([IntPtr]7001)), 'B', (New-TIdentity ([IntPtr]7002)), (New-TSession 'YODA3' 'Local' $CMD_LOCAL_YODA3), 'OC_YODA3')
}
function Add-TBaseline($Step) {
    $baseline = [TerminalOrganizer.Core.Overflow.TargetTabBaseline]::new($Step.Merge.TargetIdentity, ([TerminalOrganizer.Core.Windows.TabTitleResult]::Ok(@('NG_OTHER'))).Evidence)
    [TerminalOrganizer.Core.Overflow.MergeSequencer]::Next($Step, [TerminalOrganizer.Core.Overflow.MergeObservation]::Baseline($baseline))
}

if ($SelfTest) {
    # --- AC-001 merge planning row ---
    $facts = @(
        (New-TFact 'MGR' 600 100 $true),
        (New-TFact 'A'   0   0   $true),
        (New-TFact 'B'   0   500 $true),
        (New-TFact 'C'   400 0   $true),
        (New-TFact 'D'   800 0   $false),
        (New-TFact 'E'   900 0   $true),
        (New-TFact 'F'   1000 0  $true),
        (New-TFact 'G'   1100 0  $true)
    )
    $yoda1 = New-TSession 'YODA1' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA1 20'
    $yoda2 = New-TSession 'YODA2' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA2 20'
    $snaps = @(
        (New-TSnap 'MGR' @('OC_MGR')   @((New-TSession 'MGR' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach MGR 20')) $true $true),
        (New-TSnap 'A'   @('OC_YODA1') @($yoda1) $true $true),
        (New-TSnap 'B'   @('OC_YODA2') @($yoda2) $true $true),
        (New-TSnap 'C'   @('NC_OPS1')  @((New-TSession 'OPS1' 'WindowsNative' 'powershell.exe -NoProfile -Command "x"')) $true $false),
        (New-TSnap 'D'   @('WD_UNK1')  @() $false $false),
        (New-TSnap 'E'   @('OC_YODA3') @((New-TSession 'YODA3' 'Local' $CMD_LOCAL_YODA3)) $true $true),
        (New-TSnap 'F'   @('OC_YODA4') @((New-TSession 'YODA4' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA4 20')) $true $true),
        (New-TSnap 'G'   @('OC_YODA1', 'HC_YODA2') @($yoda1, $yoda2) $true $false)
    )
    $assignment = [TerminalOrganizer.Core.Assignment.ZoneAssigner]::Assign((New-TStandingZones), $facts, 'MGR')
    $plan = [TerminalOrganizer.Core.Overflow.MergePlanner]::Plan($assignment, $snaps)
    $ok = $plan.Merges.Count -eq 2 -and $plan.Merges[0].SourceWindowId -ceq 'E' -and
        $plan.Merges[1].SourceWindowId -ceq 'F' -and $plan.Merges[0].TargetWindowId -ceq 'B'
    Assert-Check 'ac001/merge-plan' $ok 'target B with sources [E, F]'

    # --- AC-004 command builder rows ---
    $built = [TerminalOrganizer.Core.Overflow.MergeCommandBuilder]::Build($CMD_LOCAL_YODA3)
    Assert-Check 'ac004/local-full-line' ($built -ceq 'new-tab wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA3 20') 'the full line with image token, byte-identical'

    $built2 = [TerminalOrganizer.Core.Overflow.MergeCommandBuilder]::Build('wsl.exe run.sh;extra')
    Assert-Check 'ac004/escape-row' ($built2 -ceq 'new-tab wsl.exe run.sh\;extra') 'run.sh\;extra (every ; escaped)'

    $builtA = [TerminalOrganizer.Core.Overflow.MergeCommandBuilder]::Build($CMD_LOCAL_YODA3)
    $builtB = [TerminalOrganizer.Core.Overflow.MergeCommandBuilder]::Build($CMD_LOCAL_YODA3)
    Assert-Check 'ac004/deterministic' ($builtA -ceq $builtB) 'the same input yields the same string'

    # --- AC-005 sequencing walk row ---
    $seq = [TerminalOrganizer.Core.Overflow.MergeSequencer]
    $obs = [TerminalOrganizer.Core.Overflow.MergeObservation]
    $step = $seq::Start((New-TMerge))
    $walk = ''
    $walk += $step.PendingAction.ToString() + '>'
    $step = Add-TBaseline $step
    $walk += $step.PendingAction.ToString() + '>'
    $step = $seq::Next($step, $obs::Launched($true, $null))
    $step = $seq::Next($step, $obs::Tabs($step.Merge.TargetIdentity, ([TerminalOrganizer.Core.Windows.TabTitleResult]::Ok(@('NG_OTHER'))), 9750))
    $walk += $step.PendingAction.ToString() + '>'
    $step = $seq::Next($step, $obs::Tabs($step.Merge.TargetIdentity, ([TerminalOrganizer.Core.Windows.TabTitleResult]::Ok(@('NG_OTHER', 'OC_YODA3'))), 9500))
    $walk += $step.PendingAction.ToString() + '>'
    $step = $seq::Next($step, $obs::CloseGate($true, $null))
    $walk += $step.PendingAction.ToString() + '>'
    $step = $seq::Next($step, $obs::Close($true))
    $walk += $step.Phase.ToString()
    $ok = $walk -ceq 'CaptureBaseline>Launch>Poll>VerifyAndClose>ObserveClose>Merged' -and $step.Outcome.Kind.ToString() -ceq 'Merged'
    Assert-Check 'ac005/walk' $ok 'launch > poll > verify-source > close > merged'

    # --- AC-006 abort rows ---
    $step = Add-TBaseline ($seq::Start((New-TMerge)))
    $step = $seq::Next($step, $obs::Launched($true, $null))
    $step = $seq::Next($step, $obs::Tabs($step.Merge.TargetIdentity, ([TerminalOrganizer.Core.Windows.TabTitleResult]::Ok(@('NG_OTHER'))), 0))
    $ok = $step.IsTerminal -and $step.Outcome.Kind.ToString() -ceq 'ConfirmTimeout' -and $step.Outcome.Detail -ceq 'confirm-timeout'
    Assert-Check 'ac006/confirm-timeout' $ok 'abort confirm-timeout, source open'

    $step = Add-TBaseline ($seq::Start((New-TMerge)))
    $step = $seq::Next($step, $obs::Launched($true, $null))
    $step = $seq::Next($step, $obs::Tabs($step.Merge.TargetIdentity, ([TerminalOrganizer.Core.Windows.TabTitleResult]::Ok(@('NG_OTHER', 'OC_YODA3'))), 9000))
    $step = $seq::Next($step, $obs::CloseGate($false, 'source now multi-tab'))
    $ok = $step.IsTerminal -and $step.Outcome.Kind.ToString() -ceq 'SafetyCheckFailed' -and $step.Outcome.Detail -ceq 'source now multi-tab'
    Assert-Check 'ac006/close-gate-failure' $ok 'close-gate failure -> SafetyCheckFailed'

    Write-Output ('SelfTest summary: ' + $script:CheckCount + ' checks, ' + $script:FailCount + ' failed')
    if ($script:FailCount -gt 0) { exit 1 }
    exit 0
}

# --- Live drills: CLOSE-DRILL (one supervised merge, full identity inspection) or
# ATTACH-ONLY (launch + confirm, source path never entered). Morning checklist. ---
$Drill = 'CLOSE-DRILL'
if ($AttachOnly) { $Drill = 'ATTACH-ONLY' }
$UsageClose = 'usage (close drill): merge-test.ps1 -TargetHwnd <h> -SourceHwnd <h> -CommandLine "<verbatim source command line>" -SessionName <name> [-Mode Titled -WindowTitle <prefix>]'
$UsageAttach = 'usage (attach-only): merge-test.ps1 -AttachOnly -TargetHwnd <h> -CommandLine "<verbatim source command line>" -SessionName <name> [-Mode Titled -WindowTitle <prefix>]'

function Fail-Merge([string]$Message, [int]$Code) {
    [Console]::Error.WriteLine('ERROR: ' + $Message)
    exit $Code
}

Write-Output ('live merge (' + $Drill + '): MANUAL VERIFICATION (morning checklist) - NOT an acceptance claim')

if ($TargetHwnd -eq 0 -or [string]::IsNullOrEmpty($CommandLine) -or [string]::IsNullOrEmpty($SessionName)) {
    [Console]::Error.WriteLine('ERROR: ' + $Drill + ' requires -TargetHwnd, -CommandLine and -SessionName')
    Write-Output $UsageClose
    Write-Output $UsageAttach
    exit 1
}
if ($AttachOnly) {
    if ($SourceHwnd -ne 0) {
        [Console]::Error.WriteLine('ERROR: ATTACH-ONLY rejects -SourceHwnd (attach-only never inspects or closes a source window; supply NO source handle)')
        Write-Output $UsageAttach
        exit 1
    }
}
elseif ($SourceHwnd -eq 0) {
    [Console]::Error.WriteLine('ERROR: CLOSE-DRILL requires -SourceHwnd <h> (the close drill inspects and closes the source window; use -AttachOnly when there is no source)')
    Write-Output $UsageClose
    exit 1
}
if ($Mode -eq 'Titled' -and [string]::IsNullOrEmpty($WindowTitle)) {
    [Console]::Error.WriteLine('ERROR: titled mode requires -WindowTitle <window-title prefix>')
    exit 1
}

Write-Output ('live merge (' + $Drill + '): target hwnd ' + $TargetHwnd + ', source hwnd ' + $(if ($AttachOnly) { '<none by design>' } else { $SourceHwnd }) +
    ', session ' + $SessionName + ', mode ' + $Mode + ', launcher ' + $LauncherImage)
Write-Output ('live merge: command line (verbatim): ' + $CommandLine)

# B5: A1's full identity inspection. The close drill captures BOTH identities (its
# plan must be mutation-trusted end to end); attach-only captures only the target.
# Inability to capture a trusted identity is exit 4 - never a handle-only fallback.
$session = New-TSession $SessionName 'Local' $CommandLine
$targetIdentity = [TerminalOrganizer.Core.Windows.WindowInspector]::ReadIdentity([IntPtr]$TargetHwnd)
$targetTrusted = ($null -ne $targetIdentity) -and $targetIdentity.EqualsForMutation($targetIdentity)
$sourceIdentity = $null
if (-not $AttachOnly) {
    $sourceIdentity = [TerminalOrganizer.Core.Windows.WindowInspector]::ReadIdentity([IntPtr]$SourceHwnd)
    $sourceTrusted = ($null -ne $sourceIdentity) -and $sourceIdentity.EqualsForMutation($sourceIdentity)
    if (-not $sourceTrusted) {
        Fail-Merge ('could not capture a trusted identity for the source window ' + $SourceHwnd + ' (pid/start-time/class/title read failed)') 4
    }
}
if (-not $targetTrusted) {
    Fail-Merge ('could not capture a trusted identity for the target window ' + $TargetHwnd + ' (pid/start-time/class/title read failed)') 4
}

# ATTACH-ONLY carries NO source identity (a zero-HWND source is never faked through
# the close state machine); CLOSE-DRILL carries the real source identity + tab title.
$sourceTabTitle = $null
if (-not $AttachOnly) { $sourceTabTitle = $sourceIdentity.RawWindowTitle }
$merge = [TerminalOrganizer.Core.Overflow.PlannedMerge]::new('live-source', $sourceIdentity, 'live-target', $targetIdentity, $session, $sourceTabTitle)

$modeEnum = [TerminalOrganizer.Core.Overflow.MergeTargetingMode]::Parse([TerminalOrganizer.Core.Overflow.MergeTargetingMode], $Mode)
$windowTitleArg = $null
if ($Mode -eq 'Titled') { $windowTitleArg = $WindowTitle }
$executor = New-Object TerminalOrganizer.Core.Overflow.MergeExecutor -ArgumentList $LauncherImage, $modeEnum, $windowTitleArg, $null, $PollIntervalMs, $BudgetMs

try {
    if ($AttachOnly) { $outcome = $executor.RunAttachOnly($merge) }
    else { $outcome = $executor.RunMerge($merge) }
}
catch {
    Fail-Merge ('the drill failed with an exception: ' + $_.Exception.Message) 4
}

Write-Output ('live merge outcome (' + $Drill + '): ' + $outcome.Kind.ToString() + ' (' + $outcome.Detail + ')')
$expectedOutcome = 'Merged'
if ($AttachOnly) { $expectedOutcome = 'Attached' }
if ($outcome.Kind.ToString() -ne $expectedOutcome) {
    if ($AttachOnly) {
        Write-Output 'live merge: the source was never touched by design; no window was closed by this drill'
    }
    else {
        Write-Output 'live merge: an aborted drill leaves the source window open by design'
    }
    Fail-Merge ($Drill + ' did not reach its expected safe outcome (expected ' + $expectedOutcome + ', got ' + $outcome.Kind.ToString() + ')') 4
}
if ($AttachOnly) {
    Write-Output 'live merge: the launched tab is confirmed on the target; the SOURCE path was never entered - no window was closed'
}
Write-Output 'live merge: MANUAL VERIFICATION - check the TARGET window for a stray tab left by a mis-target (plan.md G) and close it by hand'
exit 0
