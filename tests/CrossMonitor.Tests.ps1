# P2 unit C2 (night-design-2026-09-25): pure cross-monitor redistribution planner.
# Pester 3.4.0, Windows PowerShell 5.1. Run through test.ps1, which builds first.
# Expected values come from the design doc, never from the code under test.

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here
$coreDll = Join-Path $repoRoot 'bin\TerminalOrganizer.Core.dll'
$appExe = Join-Path $repoRoot 'bin\TerminalOrganizer.App.exe'

# Byte-load keeps the files unlocked so build.ps1 can rebuild while this session runs.
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($coreDll))
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($appExe))

# Pinned label characters are built from [char] codes so this file stays pure ASCII
# (PS 5.1 reads a BOM-less .ps1 as ANSI; a literal U+2014 would arrive mojibake'd).
$script:XEm = [string][char]0x2014        # em dash
$script:XTimes = [string][char]0x00D7     # multiplication sign

# --- fixtures: the standing two-monitor world (zones are absolute screen rects) ---
# Left  SN-L: z0 @ 0,0 960x1040; z1 @ 960,0 960x1040 (equal areas -> ZoneOrdering [0,1]).
# Right SN-R: z0 @ 1920,0 960x1040; z1 @ 2880,0 960x1040 (equal areas -> ZoneOrdering [0,1]).

function New-XMonitorKey([string]$Serial) {
    [TerminalOrganizer.Core.Monitors.MonitorKey]::new($Serial, $null, $null, $null)
}

function New-XZone([int]$Id, [int]$Left, [int]$Top, [int]$Width, [int]$Height) {
    [TerminalOrganizer.Core.Geometry.Zone]::new($Id, $Left, $Top, $Width, $Height)
}

function New-XLayout([string]$Serial, [string]$Label, [object[]]$Zones) {
    [TerminalOrganizer.Core.Overflow.MonitorLayoutSnapshot]::new(
        (New-XMonitorKey $Serial), $Label, [TerminalOrganizer.Core.Geometry.Zone[]]$Zones)
}

function New-XLeftLayout {
    New-XLayout 'SN-L' ('Left ' + $script:XEm + ' 1920' + $script:XTimes + '1080') @(
        (New-XZone 0 0 0 960 1040), (New-XZone 1 960 0 960 1040))
}

function New-XRightLayout {
    New-XLayout 'SN-R' ('Right ' + $script:XEm + ' 1920' + $script:XTimes + '1080') @(
        (New-XZone 0 1920 0 960 1040), (New-XZone 1 2880 0 960 1040))
}

# A valid identity (handle + pid + start-time ticks): the C2 identity/handle guard.
function New-XIdentity([long]$Handle) {
    [TerminalOrganizer.Core.Windows.WindowIdentity]::new(
        [IntPtr]$Handle, 4242, 1234567890, 'CASCADIA_HOSTING_WINDOW_CLASS', ('raw-' + $Handle))
}

function New-XPriority {
    param([string]$Id, [object]$DeclaredRank = $null, [string]$DerivedClass = 'LocalSession',
          [bool]$Manager = $false, [bool]$FullScreen = $false, [bool]$Stable = $false,
          [int]$Z = 0, [string]$MonitorKey = 'SN-L', [int]$Top = 0, [int]$Left = 0)
    $classEnum = [TerminalOrganizer.Core.Overflow.DerivedPriorityClass]::Parse(
        [TerminalOrganizer.Core.Overflow.DerivedPriorityClass], $DerivedClass)
    # C1 alignment: the immovable rows carry their matching derived class alongside
    # the booleans so resolution and immovability can never disagree.
    [TerminalOrganizer.Core.Overflow.PriorityResolver]::Resolve(
        [TerminalOrganizer.Core.Overflow.PriorityInput]::new(
            $Id, $null, $DeclaredRank, $classEnum,
            $Manager, $FullScreen, $Stable, $Z, $MonitorKey, $Top, $Left))
}

$script:XSeed = 31000
function New-XWindow {
    param([string]$Id, [string]$Monitor = 'SN-L', [bool]$Verified = $true, [bool]$Trusted = $true,
          [bool]$FullScreen = $false, [bool]$Manager = $false, [bool]$Stable = $false,
          [bool]$Overflow = $false, [bool]$InvalidIdentity = $false, [int]$ZoneId = -1,
          [int]$Left = 0, [int]$Top = 0, [int]$Width = 400, [int]$Height = 300,
          [object]$DeclaredRank = $null, [int]$Z = 0)
    $script:XSeed = $script:XSeed + 1
    $identity = New-XIdentity $script:XSeed
    if ($InvalidIdentity) {
        $identity = [TerminalOrganizer.Core.Windows.WindowIdentity]::new(
            [IntPtr]$script:XSeed, 0, 0, $null, $null)
    }
    $class = $DerivedClassDefault = 'LocalSession'
    if ($Manager) { $class = 'Manager' }
    elseif ($FullScreen) { $class = 'FullScreen' }
    elseif ($Stable) { $class = 'StableOccupant' }
    $priority = New-XPriority -Id $Id -DeclaredRank $DeclaredRank -DerivedClass $class `
        -Manager $Manager -FullScreen $FullScreen -Stable $Stable -Z $Z `
        -MonitorKey $Monitor -Top $Top -Left $Left
    [TerminalOrganizer.Core.Overflow.CrossMonitorWindow]::new(
        $Id, [IntPtr]$script:XSeed, $identity, (New-XMonitorKey $Monitor),
        $Verified, $Trusted, $FullScreen, $Manager, $Stable, $Overflow, $ZoneId,
        $Left, $Top, $Width, $Height, $priority)
}

function Invoke-XPlan([object[]]$Monitors, [object[]]$Windows) {
    [TerminalOrganizer.Core.Overflow.CrossMonitorPlanner]::Plan(
        [TerminalOrganizer.Core.Overflow.CrossMonitorSnapshot]::new(
            '{00000000-0000-4000-8000-00000000000C}',
            [TerminalOrganizer.Core.Overflow.MonitorLayoutSnapshot[]]$Monitors,
            [TerminalOrganizer.Core.Overflow.CrossMonitorWindow[]]$Windows))
}

Describe 'C2 cross-monitor redistribution planner' {
    It 'C2 overflow moves to a genuinely free zone on another monitor' {
        $plan = Invoke-XPlan @((New-XLeftLayout), (New-XRightLayout)) @(
            (New-XWindow 'ST1' -Stable $true -ZoneId 0 -Left 0 -Top 0 -Width 960 -Height 1040),
            (New-XWindow 'ST2' -Stable $true -ZoneId 1 -Left 960 -Top 0 -Width 960 -Height 1040),
            (New-XWindow 'OV1' -Overflow $true -Left 100 -Top 500)
        )
        $plan.Moves.Length | Should Be 1
        $move = $plan.Moves[0]
        $move.WindowId | Should BeExactly 'OV1'
        $move.SourceMonitorKey | Should BeExactly (New-XMonitorKey 'SN-L').CanonicalValue
        $move.SourceMonitorLabel | Should BeExactly ('Left ' + $script:XEm + ' 1920' + $script:XTimes + '1080')
        $move.DestinationMonitorKey | Should BeExactly (New-XMonitorKey 'SN-R').CanonicalValue
        $move.DestinationMonitorLabel | Should BeExactly ('Right ' + $script:XEm + ' 1920' + $script:XTimes + '1080')
        $move.DestinationZoneId | Should Be 0
        $move.TargetLeft | Should Be 1920
        $move.TargetTop | Should Be 0
        $move.TargetWidth | Should Be 960
        $move.TargetHeight | Should Be 1040
        $plan.UnchangedOverflowWindowIds.Length | Should Be 0
        $plan.HasMoves | Should Be $true
    }

    It 'C2 no free zone leaves overflow stacked' {
        $plan = Invoke-XPlan @((New-XLeftLayout), (New-XRightLayout)) @(
            (New-XWindow 'R0' -Monitor 'SN-R' -Stable $true -ZoneId 0 -Left 1920 -Top 0 -Width 960 -Height 1040),
            (New-XWindow 'R1' -Monitor 'SN-R' -Stable $true -ZoneId 1 -Left 2880 -Top 0 -Width 960 -Height 1040),
            (New-XWindow 'OV1' -Overflow $true)
        )
        $plan.Moves.Length | Should Be 0
        $plan.HasMoves | Should Be $false
        (@($plan.UnchangedOverflowWindowIds) -join ',') | Should BeExactly 'OV1'
    }

    It 'C2 manager fullscreen unknown and stable windows are immovable' {
        # Every immovable row carries overflow=$true (and the stable one even a declared
        # rank): the exclusion comes from the flags, never from priority arithmetic.
        $plan = Invoke-XPlan @((New-XLeftLayout), (New-XRightLayout)) @(
            (New-XWindow 'IM-MGR' -Manager $true -Overflow $true),
            (New-XWindow 'IM-FS' -FullScreen $true -Overflow $true),
            (New-XWindow 'IM-UNK' -Verified $false -Overflow $true),
            (New-XWindow 'IM-TRUST' -Trusted $false -Overflow $true),
            (New-XWindow 'IM-HWND' -InvalidIdentity $true -Overflow $true),
            (New-XWindow 'IM-ST' -Stable $true -Overflow $true -DeclaredRank 9)
        )
        $plan.Moves.Length | Should Be 0
        # Immovable rows are not candidates at all: none is recorded as unchanged overflow.
        $plan.UnchangedOverflowWindowIds.Length | Should Be 0
    }

    It 'C2 occupied destination is never displaced' {
        # A rank-1 (high priority) source and a rank-9 stable occupant on the only other
        # monitor: the planner never displaces an occupant to improve priority.
        $plan = Invoke-XPlan @((New-XLeftLayout), (New-XRightLayout)) @(
            (New-XWindow 'R0' -Monitor 'SN-R' -Stable $true -ZoneId 0 -DeclaredRank 9 -Left 1920 -Top 0 -Width 960 -Height 1040),
            (New-XWindow 'R1' -Monitor 'SN-R' -Stable $true -ZoneId 1 -Left 2880 -Top 0 -Width 960 -Height 1040),
            (New-XWindow 'HI-OV' -Overflow $true -DeclaredRank 1)
        )
        $plan.Moves.Length | Should Be 0
        (@($plan.UnchangedOverflowWindowIds) -join ',') | Should BeExactly 'HI-OV'
    }

    It 'C2 lowest priority source moves first' {
        $plan = Invoke-XPlan @((New-XLeftLayout), (New-XRightLayout)) @(
            (New-XWindow 'ST-R1' -Monitor 'SN-R' -Stable $true -ZoneId 1 -Left 2880 -Top 0 -Width 960 -Height 1040),
            (New-XWindow 'OV-HI' -Overflow $true -DeclaredRank 2),
            (New-XWindow 'OV-LO' -Overflow $true -DeclaredRank 8)
        )
        # One free zone: the lowest-priority source sheds first (C1 comparator, as-is).
        $plan.Moves.Length | Should Be 1
        $plan.Moves[0].WindowId | Should BeExactly 'OV-LO'
        (@($plan.UnchangedOverflowWindowIds) -join ',') | Should BeExactly 'OV-HI'
    }

    It 'C2 priority ties use captured deterministic fields' {
        $plan = Invoke-XPlan @((New-XLeftLayout), (New-XRightLayout)) @(
            (New-XWindow 'ST-R1' -Monitor 'SN-R' -Stable $true -ZoneId 1 -Left 2880 -Top 0 -Width 960 -Height 1040),
            (New-XWindow 'TIE-Z1' -Overflow $true -DeclaredRank 5 -Z 1),
            (New-XWindow 'TIE-Z5' -Overflow $true -DeclaredRank 5 -Z 5)
        )
        # Equal ranks: the larger captured Z-order index is the older/lower window, so it
        # is planned first and takes the single free zone.
        $plan.Moves.Length | Should Be 1
        $plan.Moves[0].WindowId | Should BeExactly 'TIE-Z5'
        (@($plan.UnchangedOverflowWindowIds) -join ',') | Should BeExactly 'TIE-Z1'
    }

    It 'C2 shuffled monitor window and zone inputs yield identical plan' {
        function Get-XPlanText([object[]]$Monitors, [object[]]$Windows) {
            $p = Invoke-XPlan $Monitors $Windows
            $rows = @($p.Moves | ForEach-Object {
                $_.WindowId + '>' + $_.DestinationMonitorKey + ':z' + $_.DestinationZoneId +
                '@' + $_.TargetLeft + ',' + $_.TargetTop + ' ' + $_.TargetWidth + 'x' + $_.TargetHeight +
                '|' + $_.PriorityReason })
            (($rows -join ';') + '||' + ((@($p.UnchangedOverflowWindowIds)) -join ','))
        }
        $windows = @(
            (New-XWindow 'ST-L0' -Stable $true -ZoneId 0 -Left 0 -Top 0 -Width 960 -Height 1040),
            (New-XWindow 'OV-A' -Overflow $true -DeclaredRank 3),
            (New-XWindow 'OV-B' -Overflow $true -DeclaredRank 7),
            (New-XWindow 'ST-R1' -Monitor 'SN-R' -Stable $true -ZoneId 1 -Left 2880 -Top 0 -Width 960 -Height 1040)
        )
        $ordered = Get-XPlanText @((New-XLeftLayout), (New-XRightLayout)) $windows
        # PS 5.1 binds $null strings as empty, so the null-part canonical renders "0:"
        # (never assumed — computed from the same fixture key the planner consumed).
        $rightCanonical = (New-XMonitorKey 'SN-R').CanonicalValue
        $ordered | Should BeExactly ('OV-B>' + $rightCanonical + ':z0@1920,0 960x1040|declared rank 7||OV-A')
        # Shuffles: monitors reversed, windows reversed, zone arrays reversed (ids stay
        # attached to their rects; only input ORDER changes).
        $revLeft = New-XLayout 'SN-L' ('Left ' + $script:XEm + ' 1920' + $script:XTimes + '1080') @(
            (New-XZone 1 960 0 960 1040), (New-XZone 0 0 0 960 1040))
        $revRight = New-XLayout 'SN-R' ('Right ' + $script:XEm + ' 1920' + $script:XTimes + '1080') @(
            (New-XZone 1 2880 0 960 1040), (New-XZone 0 1920 0 960 1040))
        $shuffledWindows = @($windows[3], $windows[2], $windows[1], $windows[0])
        $shuffled = Get-XPlanText @($revRight, $revLeft) $shuffledWindows
        $shuffled | Should BeExactly $ordered
    }

    It 'C2 destination must differ from source monitor' {
        # Left still has a free local zone and the other monitor has none: a local free
        # zone is never a cross-monitor move, so the overflow stays stacked.
        $plan = Invoke-XPlan @((New-XLeftLayout), (New-XRightLayout)) @(
            (New-XWindow 'ST-L0' -Stable $true -ZoneId 0 -Left 0 -Top 0 -Width 960 -Height 1040),
            (New-XWindow 'R0' -Monitor 'SN-R' -Stable $true -ZoneId 0 -Left 1920 -Top 0 -Width 960 -Height 1040),
            (New-XWindow 'R1' -Monitor 'SN-R' -Stable $true -ZoneId 1 -Left 2880 -Top 0 -Width 960 -Height 1040),
            (New-XWindow 'OV1' -Overflow $true)
        )
        $plan.Moves.Length | Should Be 0
        (@($plan.UnchangedOverflowWindowIds) -join ',') | Should BeExactly 'OV1'
    }

    It 'C2 second reconstructed run produces zero moves' {
        # RELEASE obligation: after applying the first plan's output (rect, monitor,
        # zone; stable occupant), the reconstructed snapshot plans zero moves.
        $monitors = @((New-XLeftLayout), (New-XRightLayout))
        $windows = @(
            (New-XWindow 'ST1' -Stable $true -ZoneId 0 -Left 0 -Top 0 -Width 960 -Height 1040),
            (New-XWindow 'ST2' -Stable $true -ZoneId 1 -Left 960 -Top 0 -Width 960 -Height 1040),
            (New-XWindow 'OV1' -Overflow $true -Left 100 -Top 500)
        )
        $first = Invoke-XPlan $monitors $windows
        $first.Moves.Length | Should Be 1
        $move = $first.Moves[0]
        $reconstructed = @(
            $windows[0],
            $windows[1],
            (New-XWindow 'OV1' -Monitor 'SN-R' -Stable $true -ZoneId $move.DestinationZoneId `
                -Left $move.TargetLeft -Top $move.TargetTop -Width $move.TargetWidth -Height $move.TargetHeight)
        )
        $second = Invoke-XPlan $monitors $reconstructed
        $second.Moves.Length | Should Be 0
        $second.UnchangedOverflowWindowIds.Length | Should Be 0
    }
}
