# SPEC-ZONE-005 acceptance suite (Pester 3.4.0, Windows PowerShell 5.1).
# Run through test.ps1, which builds first and starts a fresh powershell.exe.
# Expected values come from acceptance.md and plan.md section J, never from the code under test.

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here
$dllPath = Join-Path $repoRoot 'bin\TerminalOrganizer.Core.dll'

# Byte-load keeps the DLL file unlocked so build.ps1 can rebuild while this session runs.
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dllPath))

# Standing zone set (plan.md J.1): Zone(id, left, top, width, height) in screen coords.
# ZoneOrdering order (tolerance 100 bp): z1 (largest) first, then z0, then z2 (top-left rule).
function New-StandingZones {
    @(
        [TerminalOrganizer.Core.Geometry.Zone]::new(0, 16, 16, 456, 1120),   # z0: 456x1120 @ 16,16
        [TerminalOrganizer.Core.Geometry.Zone]::new(1, 488, 16, 944, 1120),  # z1: 944x1120 @ 488,16 (largest)
        [TerminalOrganizer.Core.Geometry.Zone]::new(2, 1448, 16, 456, 1120)  # z2: 456x1120 @ 1448,16
    )
}

$script:FactHandleSeed = 1000

# One WindowFact (plan.md J.2 rect form L/T/W/H; name defaults to the id; all normal state unless switched).
function New-Fact {
    param(
        [string]$Id,
        [int]$Left = 0,
        [int]$Top = 0,
        [int]$Width = 400,
        [int]$Height = 300,
        [string]$Name = $null,
        [bool]$Identified = $true,
        [switch]$Maximized,
        [switch]$Minimized,
        [switch]$FullScreen
    )
    if ([string]::IsNullOrEmpty($Name)) { $Name = $Id }
    $script:FactHandleSeed = $script:FactHandleSeed + 1
    [TerminalOrganizer.Core.Assignment.WindowFact]::new(
        $Id, [IntPtr]$script:FactHandleSeed, $Name, $Identified,
        $Left, $Top, $Width, $Height,
        [bool]$Maximized.IsPresent, [bool]$Minimized.IsPresent, [bool]$FullScreen.IsPresent)
}

function Invoke-Assign($Zones, $Facts, [string]$ManagerName) {
    [TerminalOrganizer.Core.Assignment.ZoneAssigner]::Assign($Zones, $Facts, $ManagerName)
}

function Get-Move($Plan, [string]$WindowId) {
    @($Plan.Moves | Where-Object { $_.WindowId -eq $WindowId })[0]
}

Describe 'AC-001 Manager pin' {
    It 'W-MGR -> z1 alone; W-A -> z0; W-B -> z2; no non-manager window planned into z1' {
        $zones = New-StandingZones
        $facts = @(
            (New-Fact 'MGR' -Left 100 -Top 100),
            (New-Fact 'A' -Left 0 -Top 0),
            (New-Fact 'B' -Left 500 -Top 0)
        )
        $plan = Invoke-Assign $zones $facts 'MGR'
        $plan.ManagerPinned | Should Be $true
        $plan.ManagerWindowId | Should BeExactly 'MGR'
        $plan.ManagerSkippedReason | Should BeNullOrEmpty
        (Get-Move $plan 'MGR').ZoneId | Should Be 1
        (Get-Move $plan 'MGR').Stacked | Should Be $false
        (Get-Move $plan 'A').ZoneId | Should Be 0
        (Get-Move $plan 'B').ZoneId | Should Be 2
        @($plan.Moves | Where-Object { $_.ZoneId -eq 1 -and $_.WindowId -ne 'MGR' }).Count | Should Be 0
    }
}

Describe 'AC-002 Position fill order' {
    It 'W-A (top 0, left 0), W-B (top 0, left 500), W-C (top 400, left 0), no manager: A, B, C -> z1, z0, z2' {
        $zones = New-StandingZones
        $facts = @(
            (New-Fact 'A' -Left 0 -Top 0),
            (New-Fact 'B' -Left 500 -Top 0),
            (New-Fact 'C' -Left 0 -Top 400)
        )
        $plan = Invoke-Assign $zones $facts $null
        $plan.ManagerPinned | Should Be $false
        (Get-Move $plan 'A').ZoneId | Should Be 1
        (Get-Move $plan 'B').ZoneId | Should Be 0
        (Get-Move $plan 'C').ZoneId | Should Be 2
    }

    It 'a tie at top 0 left 0 orders id D before id E (ordinal compare), regardless of input order' {
        $zones = New-StandingZones
        $facts = @(
            (New-Fact 'E' -Left 0 -Top 0),
            (New-Fact 'D' -Left 0 -Top 0)
        )
        $plan = Invoke-Assign $zones $facts $null
        (Get-Move $plan 'D').ZoneId | Should Be 1
        (Get-Move $plan 'E').ZoneId | Should Be 0
    }
}

Describe 'AC-003 Overflow stacking' {
    It 'W-MGR + W-A..W-D (5 windows, 3 zones): MGR -> z1; A -> z0; B -> z2; C and D -> z2 stacked, C then D' {
        $zones = New-StandingZones
        $facts = @(
            (New-Fact 'MGR' -Left 100 -Top 100),
            (New-Fact 'A' -Left 0 -Top 0),
            (New-Fact 'B' -Left 500 -Top 0),
            (New-Fact 'C' -Left 0 -Top 400),
            (New-Fact 'D' -Left 0 -Top 800)
        )
        $plan = Invoke-Assign $zones $facts 'MGR'
        (Get-Move $plan 'MGR').ZoneId | Should Be 1
        (Get-Move $plan 'A').ZoneId | Should Be 0
        (Get-Move $plan 'B').ZoneId | Should Be 2
        (Get-Move $plan 'B').Stacked | Should Be $false
        (Get-Move $plan 'C').ZoneId | Should Be 2
        (Get-Move $plan 'C').Stacked | Should Be $true
        (Get-Move $plan 'D').ZoneId | Should Be 2
        (Get-Move $plan 'D').Stacked | Should Be $true
        $stacked = @($plan.Moves | Where-Object { $_.Stacked })
        $stacked.Count | Should Be 2
        $stacked[0].WindowId | Should BeExactly 'C'
        $stacked[1].WindowId | Should BeExactly 'D'
    }
}

Describe 'AC-004 Manager skipped' {
    It 'manager choice GHOST (no such window): plain fill A -> z1, B -> z0, ManagerSkipped "window not found"' {
        $zones = New-StandingZones
        $facts = @(
            (New-Fact 'A' -Left 0 -Top 0),
            (New-Fact 'B' -Left 500 -Top 0)
        )
        $plan = Invoke-Assign $zones $facts 'GHOST'
        $plan.ManagerPinned | Should Be $false
        $plan.ManagerSkippedReason | Should BeExactly 'window not found'
        (Get-Move $plan 'A').ZoneId | Should Be 1
        (Get-Move $plan 'B').ZoneId | Should Be 0
    }

    It 'manager choice set but its window unidentified: plain fill, ManagerSkipped "window unidentified"' {
        $zones = New-StandingZones
        $facts = @(
            (New-Fact 'A' -Left 0 -Top 0),
            (New-Fact 'MGR' -Left 100 -Top 100 -Identified:$false)
        )
        $plan = Invoke-Assign $zones $facts 'MGR'
        $plan.ManagerPinned | Should Be $false
        $plan.ManagerSkippedReason | Should BeExactly 'window unidentified'
        (Get-Move $plan 'A').ZoneId | Should Be 1
        (Get-Move $plan 'MGR').ZoneId | Should Be 0
    }

    It 'no manager choice at all: plain fill, ManagerSkipped "no choice"' {
        $zones = New-StandingZones
        $facts = @(
            (New-Fact 'A' -Left 0 -Top 0),
            (New-Fact 'B' -Left 500 -Top 0)
        )
        $plan = Invoke-Assign $zones $facts $null
        $plan.ManagerPinned | Should Be $false
        $plan.ManagerSkippedReason | Should BeExactly 'no choice'
        (Get-Move $plan 'A').ZoneId | Should Be 1
        (Get-Move $plan 'B').ZoneId | Should Be 0
    }
}

Describe 'AC-005 Idempotent no-move' {
    It 'W-A already at exactly z1 rect (488,16 944x1120) assigned z1 -> planned no-move' {
        $zones = New-StandingZones
        $facts = @((New-Fact 'A' -Left 488 -Top 16 -Width 944 -Height 1120))
        $plan = Invoke-Assign $zones $facts $null
        $move = Get-Move $plan 'A'
        $move.ZoneId | Should Be 1
        $move.MoveRequired | Should Be $false
        $move.RestoreFirst | Should Be $false
        $move.FullScreenSkipped | Should Be $false
        $move.TargetLeft | Should Be 488
        $move.TargetTop | Should Be 16
        $move.TargetWidth | Should Be 944
        $move.TargetHeight | Should Be 1120
    }

    It 'W-A at z1 rect shifted by 1px -> planned move to z1 rect' {
        $zones = New-StandingZones
        $facts = @((New-Fact 'A' -Left 489 -Top 16 -Width 944 -Height 1120))
        $plan = Invoke-Assign $zones $facts $null
        $move = Get-Move $plan 'A'
        $move.ZoneId | Should Be 1
        $move.MoveRequired | Should Be $true
        $move.TargetLeft | Should Be 488
        $move.TargetTop | Should Be 16
        $move.TargetWidth | Should Be 944
        $move.TargetHeight | Should Be 1120
    }
}

Describe 'AC-006 Restore-first and full-screen' {
    It 'a maximized W-A assigned z0 -> plan carries restore-first + target rect' {
        $zones = New-StandingZones
        $facts = @(
            (New-Fact 'MGR' -Left 700 -Top 700),
            (New-Fact 'A' -Left 0 -Top 0 -Width 1920 -Height 1040 -Maximized)
        )
        $plan = Invoke-Assign $zones $facts 'MGR'
        $move = Get-Move $plan 'A'
        $move.ZoneId | Should Be 0
        $move.RestoreFirst | Should Be $true
        $move.MoveRequired | Should Be $true
        $move.TargetLeft | Should Be 16
        $move.TargetTop | Should Be 16
        $move.TargetWidth | Should Be 456
        $move.TargetHeight | Should Be 1120
    }

    It 'a maximized W-A whose rect ALREADY equals the target rect -> still restore-first (state outranks rect equality)' {
        $zones = New-StandingZones
        $facts = @(
            (New-Fact 'MGR' -Left 700 -Top 700),
            (New-Fact 'A' -Left 16 -Top 16 -Width 456 -Height 1120 -Maximized)
        )
        $plan = Invoke-Assign $zones $facts 'MGR'
        $move = Get-Move $plan 'A'
        $move.ZoneId | Should Be 0
        $move.RestoreFirst | Should Be $true
        $move.MoveRequired | Should Be $true
    }

    It 'a minimized W-A assigned z0 -> restore-first + target rect' {
        $zones = New-StandingZones
        $facts = @(
            (New-Fact 'MGR' -Left 700 -Top 700),
            (New-Fact 'A' -Left 0 -Top 0 -Width 800 -Height 600 -Minimized)
        )
        $plan = Invoke-Assign $zones $facts 'MGR'
        $move = Get-Move $plan 'A'
        $move.ZoneId | Should Be 0
        $move.RestoreFirst | Should Be $true
        $move.MoveRequired | Should Be $true
        $move.TargetWidth | Should Be 456
    }

    It 'a full-screen-flagged W-A -> no-move with FullScreenSkipped marker, regardless of rect' {
        $zones = New-StandingZones
        $facts = @(
            (New-Fact 'MGR' -Left 700 -Top 700),
            (New-Fact 'A' -Left 488 -Top 16 -Width 944 -Height 1120 -FullScreen)
        )
        $plan = Invoke-Assign $zones $facts 'MGR'
        $move = Get-Move $plan 'A'
        $move.ZoneId | Should Be -1
        $move.MoveRequired | Should Be $false
        $move.FullScreenSkipped | Should Be $true
        $move.RestoreFirst | Should Be $false
    }
}

Describe 'A2 stable ownership and full-screen partition' {
    It 'A3 untrusted state consumes no zone and cannot be manager' {
        $bad = [TerminalOrganizer.Core.Assignment.WindowFact]::new('BAD', [IntPtr]1, 'BAD', $true, 0,0,0,0,$false,$false,$false,$false)
        $plan = Invoke-Assign (New-StandingZones) @($bad, (New-Fact 'A'), (New-Fact 'B'), (New-Fact 'C')) 'BAD'
        $plan.ManagerPinned | Should Be $false
        $move = Get-Move $plan 'BAD'
        $move.ZoneId | Should Be -1
        $move.SkipReason.ToString() | Should BeExactly 'UntrustedState'
        $move.OccupiesZone | Should Be $false
        $move.MoveRequired | Should Be $false
        @($plan.Moves | Where-Object { $_.OccupiesZone }).Count | Should Be 3
    }
    It 'A3 failed native state read explicitly prevents mutation' {
        $state = ([TerminalOrganizer.Core.Assignment.WindowStateReader]::new()).Read([IntPtr]::Zero)
        $state.Status.ToString() | Should BeExactly 'RectUnavailable'
        $state.IsValidForMutation | Should Be $false
    }
    It 'three settled unequal zones retain exact mapping twice with shuffled input' {
        $facts = @(
            (New-Fact 'C' -Left 1448 -Top 16 -Width 456 -Height 1120),
            (New-Fact 'A' -Left 16 -Top 16 -Width 456 -Height 1120),
            (New-Fact 'B' -Left 488 -Top 16 -Width 944 -Height 1120)
        )
        foreach ($order in @(@(0,1,2), @(2,0,1))) {
            $plan = Invoke-Assign (New-StandingZones) @($facts[$order[0]], $facts[$order[1]], $facts[$order[2]]) $null
            (Get-Move $plan 'A').ZoneId | Should Be 0
            (Get-Move $plan 'B').ZoneId | Should Be 1
            (Get-Move $plan 'C').ZoneId | Should Be 2
            @($plan.Moves | Where-Object { $_.MoveRequired }).Count | Should Be 0
        }
    }
    It 'settled stack stays on its existing zone in ordinal ID order' {
        $facts = @(
            (New-Fact 'Z' -Left 16 -Top 16 -Width 456 -Height 1120),
            (New-Fact 'A' -Left 16 -Top 16 -Width 456 -Height 1120),
            (New-Fact 'B' -Left 488 -Top 16 -Width 944 -Height 1120)
        )
        $plan = Invoke-Assign (New-StandingZones) $facts $null
        (Get-Move $plan 'A').ZoneId | Should Be 0
        (Get-Move $plan 'Z').ZoneId | Should Be 0
        (Get-Move $plan 'A').Stacked | Should Be $false
        (Get-Move $plan 'Z').Stacked | Should Be $true
        @($plan.Moves | Where-Object { $_.MoveRequired }).Count | Should Be 0
    }
    It 'exact occupants precede positional fill and manager is the retention override' {
        $facts = @((New-Fact 'B' -Left 1448 -Top 16 -Width 456 -Height 1120), (New-Fact 'A'))
        $plan = Invoke-Assign (New-StandingZones) $facts $null
        (Get-Move $plan 'B').ZoneId | Should Be 2
        (Get-Move $plan 'A').ZoneId | Should Be 1
        $facts = @((New-Fact 'MGR' -Left 1448 -Top 16 -Width 456 -Height 1120), (New-Fact 'A' -Left 488 -Top 16 -Width 944 -Height 1120))
        $plan = Invoke-Assign (New-StandingZones) $facts 'MGR'
        (Get-Move $plan 'MGR').ZoneId | Should Be 1
        (Get-Move $plan 'MGR').MoveRequired | Should Be $true
        (Get-Move $plan 'A').ZoneId | Should Be 0
    }
    It 'full-screen windows consume no zones and cannot pin as manager' {
        $facts = @((New-Fact 'FULL' -FullScreen), (New-Fact 'A' -Top 1), (New-Fact 'B' -Top 2), (New-Fact 'C' -Top 3))
        $plan = Invoke-Assign (New-StandingZones) $facts 'FULL'
        $plan.ManagerPinned | Should Be $false
        $plan.ManagerSkippedReason | Should BeExactly 'window full screen'
        $full = Get-Move $plan 'FULL'
        $full.ZoneId | Should Be -1
        $full.OccupiesZone | Should Be $false
        $full.Stacked | Should Be $false
        $full.TargetWidth | Should Be 400
        (($plan.Moves | Where-Object { $_.OccupiesZone } | ForEach-Object { $_.ZoneId } | Sort-Object) -join ',') | Should BeExactly '0,1,2'
    }
    It 'identical zones select earliest ordered zone for the whole exact-rect group' {
        $zones = @([TerminalOrganizer.Core.Geometry.Zone]::new(3, 0, 0, 400, 300), [TerminalOrganizer.Core.Geometry.Zone]::new(2, 0, 0, 400, 300))
        $plan = Invoke-Assign $zones @((New-Fact 'B'), (New-Fact 'A')) $null
        (Get-Move $plan 'A').ZoneId | Should Be 2
        (Get-Move $plan 'B').ZoneId | Should Be 2
        (Get-Move $plan 'B').Stacked | Should Be $true
    }
}

Describe 'AC-007 One-zone layout' {
    It 'W-MGR -> zOnly alone-placed; W-A and W-B -> zOnly stacked, in order A, B' {
        $zones = @([TerminalOrganizer.Core.Geometry.Zone]::new(9, 0, 0, 1920, 1040))
        $facts = @(
            (New-Fact 'MGR' -Left 100 -Top 100),
            (New-Fact 'B' -Left 500 -Top 0),
            (New-Fact 'A' -Left 0 -Top 0)
        )
        $plan = Invoke-Assign $zones $facts 'MGR'
        $plan.ManagerPinned | Should Be $true
        (Get-Move $plan 'MGR').ZoneId | Should Be 9
        (Get-Move $plan 'MGR').Stacked | Should Be $false
        (Get-Move $plan 'A').ZoneId | Should Be 9
        (Get-Move $plan 'A').Stacked | Should Be $true
        (Get-Move $plan 'B').ZoneId | Should Be 9
        (Get-Move $plan 'B').Stacked | Should Be $true
        $stacked = @($plan.Moves | Where-Object { $_.Stacked })
        $stacked[0].WindowId | Should BeExactly 'A'
        $stacked[1].WindowId | Should BeExactly 'B'
    }
}

function New-Store([string]$Path) {
    New-Object TerminalOrganizer.Core.Assignment.ManagerStore -ArgumentList $Path
}

Describe 'AC-008 Manager store' {
    It 'save MGR to a temp path: a fresh load returns MGR and the file contains managerWindowName' {
        $path = Join-Path ([IO.Path]::GetTempPath()) ('zone005-store-' + [guid]::NewGuid().ToString('N') + '.json')
        try {
            $store = New-Store $path
            $store.Save('MGR')
            $fresh = New-Store $path
            $fresh.Load() | Should BeExactly 'MGR'
            (Get-Content -LiteralPath $path -Raw) | Should Match 'managerWindowName'
        }
        finally {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
    }

    It 'load from a missing path returns no choice; from "{bad json" returns no choice without an exception' {
        $missing = Join-Path ([IO.Path]::GetTempPath()) ('zone005-missing-' + [guid]::NewGuid().ToString('N') + '.json')
        $store = New-Store $missing
        $store.Load() | Should BeNullOrEmpty

        $badPath = Join-Path ([IO.Path]::GetTempPath()) ('zone005-bad-' + [guid]::NewGuid().ToString('N') + '.json')
        try {
            [IO.File]::WriteAllText($badPath, '{bad json')
            $badStore = New-Store $badPath
            $caught = $null
            $choice = $null
            try { $choice = $badStore.Load() } catch { $caught = $_ }
            $caught | Should BeNullOrEmpty
            $choice | Should BeNullOrEmpty
        }
        finally {
            if (Test-Path -LiteralPath $badPath) { Remove-Item -LiteralPath $badPath -Force }
        }
    }

    It 'save of the empty string clears: a subsequent load returns no choice' {
        $path = Join-Path ([IO.Path]::GetTempPath()) ('zone005-empty-' + [guid]::NewGuid().ToString('N') + '.json')
        try {
            $store = New-Store $path
            $store.Save('MGR')
            $store.Load() | Should BeExactly 'MGR'
            $store.Save('')
            $store.Load() | Should BeNullOrEmpty
        }
        finally {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
    }
}

Describe 'AC-009 Placer surface and degradation' {
    It 'Apply on a fake plan with two invalid IntPtr handles (12345, 54321) reports BOTH windows, without throwing' {
        $placer = New-Object TerminalOrganizer.Core.Assignment.WindowPlacer
        $m1 = [TerminalOrganizer.Core.Assignment.PlannedMove]::new('FAKE1', [IntPtr]12345, 0, 16, 16, 456, 1120, $true, $true, $false, $false)
        $m2 = [TerminalOrganizer.Core.Assignment.PlannedMove]::new('FAKE2', [IntPtr]54321, 0, 16, 16, 456, 1120, $true, $true, $false, $false)
        $plan = [TerminalOrganizer.Core.Assignment.AssignmentPlan]::new(@($m1, $m2), $false, $null, 'no choice')
        $caught = $null
        $results = $null
        try { $results = $placer.Apply($plan) } catch { $caught = $_ }
        $caught | Should BeNullOrEmpty
        @($results).Length | Should Be 2
        $results[0].WindowId | Should BeExactly 'FAKE1'
        $results[1].WindowId | Should BeExactly 'FAKE2'
        $results[0].Success | Should Be $false
        $results[1].Success | Should Be $false
        $results[0].Error | Should Not BeNullOrEmpty
        $results[1].Error | Should Not BeNullOrEmpty
    }
}

Describe 'AC-010 Dry-run tool' {
    It 'tools/organize-dryrun.ps1 -SelfTest under PS 5.1 exercises AC-001..AC-006 rows and exits 0 only on all-pass' {
        $ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $tool = Join-Path $repoRoot 'tools\organize-dryrun.ps1'
        $output = & $ps51 -NoProfile -ExecutionPolicy Bypass -File $tool -SelfTest
        $code = $LASTEXITCODE
        $lines = @($output | Where-Object { $_ -cmatch '^(PASS|FAIL):' })
        $lines.Count | Should BeGreaterThan 0
        @($lines | Where-Object { $_ -cmatch '^FAIL:' }).Count | Should Be 0
        $code | Should Be 0
    }

    It 'the dry-run tool never references WindowPlacer (it must never move a window)' {
        $tool = Join-Path $repoRoot 'tools\organize-dryrun.ps1'
        @(Select-String -LiteralPath $tool -Pattern 'WindowPlacer').Count | Should Be 0
    }
}

# --- B2: canonical manager selector resolution (night-design-2026-09-25 unit B2) ---

function New-B2SessionTab([string]$Title, [string]$SessionName) {
    $session = $null
    if ($SessionName) {
        $session = [TerminalOrganizer.Core.Windows.SessionRecord]::new(
            $SessionName, [TerminalOrganizer.Core.Windows.SessionKind]::Local,
            ('wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach ' + $SessionName))
    }
    [TerminalOrganizer.Core.Windows.TabSnapshot]::new($Title, $Title, $session)
}

# One fact+snapshot pair joined by handle (DiscoveryPolicy shape: fact name = raw title).
function New-B2Window {
    param([long]$Handle, [string]$RawTitle, [object[]]$Tabs, [bool]$Identified)
    $identity = [TerminalOrganizer.Core.Windows.WindowIdentity]::new(
        [IntPtr]$Handle, 42, 12345, 'CASCADIA_HOSTING_WINDOW_CLASS', $RawTitle)
    $unmatched = @()
    if (-not $Identified) { $unmatched = @($Tabs | ForEach-Object { $_.Title }) }
    $snapshot = [TerminalOrganizer.Core.Windows.WindowSnapshot]::new(
        $identity, $null, $null, [TerminalOrganizer.Core.Windows.TabSnapshot[]]$Tabs, $Identified,
        [string[]]$unmatched, $false, [TerminalOrganizer.Core.Windows.TabReadQuality]::Trusted)
    $fact = [TerminalOrganizer.Core.Assignment.WindowFact]::new(
        ('w' + $Handle), [IntPtr]$Handle, $RawTitle, $Identified, 0, 0, 400, 300, $false, $false, $false)
    @{ Fact = $fact; Snapshot = $snapshot }
}

Describe 'B2 Canonical manager resolution' {
    It 'B2 changed active tab still resolves canonical session' {
        # The manager was chosen when the window hosted MGR; the active tab has since
        # changed, but another tab still carries the canonical session.
        $window = New-B2Window 8101 'title-now' @(
            (New-B2SessionTab 'OC_OTHER' 'OTHER'),
            (New-B2SessionTab 'OC_MGR' 'MGR')) $true
        $facts = [TerminalOrganizer.Core.Assignment.WindowFact[]]@($window.Fact)
        $snaps = [TerminalOrganizer.Core.Windows.WindowSnapshot[]]@($window.Snapshot)
        $selector = [TerminalOrganizer.Core.Assignment.ManagerSelector]::new(
            [TerminalOrganizer.Core.Assignment.ManagerSelectorKind]::Session, 'MGR', 'title-then')
        $resolution = [TerminalOrganizer.Core.Assignment.ManagerResolver]::Resolve($selector, $facts, $snaps)
        $resolution.Status.ToString() | Should BeExactly 'Resolved'
        $resolution.WindowId | Should BeExactly 'w8101'
        $resolution.Handle.ToInt64() | Should Be 8101
        $resolution.CandidateCount | Should Be 1

        # The ZoneAssigner resolution overload pins that window as the manager.
        $plan = [TerminalOrganizer.Core.Assignment.ZoneAssigner]::Assign((New-StandingZones), $facts, $resolution)
        $plan.ManagerPinned | Should Be $true
        $plan.ManagerWindowId | Should BeExactly 'w8101'
        $plan.ManagerSkippedReason | Should BeNullOrEmpty
    }

    It 'B2 raw-title compatibility selector resolves exact only' {
        $exact = New-B2Window 8201 'Window X' @((New-B2SessionTab 'OC_A' 'SESS-A')) $true
        $cased = New-B2Window 8202 'window x' @((New-B2SessionTab 'OC_B' 'SESS-B')) $true
        $rawSelector = [TerminalOrganizer.Core.Assignment.ManagerSelector]::new(
            [TerminalOrganizer.Core.Assignment.ManagerSelectorKind]::RawTitle, 'Window X', 'Window X')
        $facts = [TerminalOrganizer.Core.Assignment.WindowFact[]]@($exact.Fact, $cased.Fact)
        $snaps = [TerminalOrganizer.Core.Windows.WindowSnapshot[]]@($exact.Snapshot, $cased.Snapshot)
        $resolution = [TerminalOrganizer.Core.Assignment.ManagerResolver]::Resolve($rawSelector, $facts, $snaps)
        $resolution.Status.ToString() | Should BeExactly 'Resolved'
        $resolution.WindowId | Should BeExactly 'w8201'
        $resolution.CandidateCount | Should Be 1

        # Ordinal exactness: a case-differing raw title never matches.
        $casedOnly = [TerminalOrganizer.Core.Assignment.ManagerResolver]::Resolve($rawSelector,
            [TerminalOrganizer.Core.Assignment.WindowFact[]]@($cased.Fact),
            [TerminalOrganizer.Core.Windows.WindowSnapshot[]]@($cased.Snapshot))
        $casedOnly.Status.ToString() | Should BeExactly 'NotFound'
        $casedOnly.CandidateCount | Should Be 0

        # Session selector with zero session hits falls back to the raw title (exact ordinal).
        $sessionSelector = [TerminalOrganizer.Core.Assignment.ManagerSelector]::new(
            [TerminalOrganizer.Core.Assignment.ManagerSelectorKind]::Session, 'GHOST', 'Window X')
        $fallback = [TerminalOrganizer.Core.Assignment.ManagerResolver]::Resolve($sessionSelector, $facts, $snaps)
        $fallback.Status.ToString() | Should BeExactly 'Resolved'
        $fallback.WindowId | Should BeExactly 'w8201'
    }
}

Describe 'AC-011 WindowStateReader surface and degradation' {
    It 'Read on an invalid handle ([IntPtr]::Zero) yields normal-state defaults + zero rect, never an exception' {
        $reader = New-Object TerminalOrganizer.Core.Assignment.WindowStateReader
        $caught = $null
        $state = $null
        try { $state = $reader.Read([IntPtr]::Zero) } catch { $caught = $_ }
        $caught | Should BeNullOrEmpty
        $state | Should Not BeNullOrEmpty
        $state.Left | Should Be 0
        $state.Top | Should Be 0
        $state.Width | Should Be 0
        $state.Height | Should Be 0
        $state.Maximized | Should Be $false
        $state.Minimized | Should Be $false
        $state.FullScreen | Should Be $false
    }

    It 'the full-screen determination is a pinned pure computation over injected style/rect inputs' {
        $readerType = [TerminalOrganizer.Core.Assignment.WindowStateReader]
        # A borderless window (no WS_CAPTION | WS_THICKFRAME) whose rect covers the monitor rect reports the flag.
        $readerType::IsFullScreenWindow(0, 0, 0, 1920, 1040, 0, 0, 1920, 1040) | Should Be $true
        # A captioned, thick-framed window at the same rect does not.
        $readerType::IsFullScreenWindow(0x00C40000, 0, 0, 1920, 1040, 0, 0, 1920, 1040) | Should Be $false
        # A borderless window that does not cover the monitor rect does not.
        $readerType::IsFullScreenWindow(0, 100, 100, 800, 600, 0, 0, 1920, 1040) | Should Be $false
    }
}

# --- Invisible-border compensation (FancyZones parity) ---
# Windows 10/11 top-level windows carry an invisible resize border: the visible frame
# (DWMWA_EXTENDED_FRAME_BOUNDS) is inset from GetWindowRect. Expected values are real
# measurements of Windows Terminal at 96 dpi, never derived from the code under test.
Describe 'Frame bounds (invisible borders match FancyZones)' {
    $frameBounds = [TerminalOrganizer.Core.Assignment.FrameBounds]
    $margins = [TerminalOrganizer.Core.Assignment.FrameMargins]

    It 'Measure derives frame-minus-window margins from a real WT rect pair' {
        # window (647,16)-(1271,1016) vs frame (654,16)-(1264,1009) => 7,0,-7,-7
        $m = $frameBounds::Measure(647, 16, 1271, 1016, 654, 16, 1264, 1009)
        $m.Left | Should Be 7
        $m.Top | Should Be 0
        $m.Right | Should Be -7
        $m.Bottom | Should Be -7
    }

    It 'ExpandTarget of zone z4 (463x509 @ 2864,5) yields the live FancyZones-placed outer rect' {
        $m = $margins::new(7, 0, -7, -7)
        $r = $frameBounds::ExpandTarget(2864, 5, 463, 509, $m)
        ($r -join ',') | Should BeExactly '2857,5,477,516'
    }

    It 'VisibleRect of the FancyZones-placed outer rect (2857,5 477x516) is exactly the zone' {
        $m = $margins::new(7, 0, -7, -7)
        $r = $frameBounds::VisibleRect(2857, 5, 477, 516, $m)
        ($r -join ',') | Should BeExactly '2864,5,463,509'
    }

    It 'ExpandTarget and VisibleRect are exact inverses' {
        $m = $margins::new(7, 0, -7, -7)
        $outer = $frameBounds::ExpandTarget(100, 20, 800, 600, $m)
        $back = $frameBounds::VisibleRect($outer[0], $outer[1], $outer[2], $outer[3], $m)
        ($back -join ',') | Should BeExactly '100,20,800,600'
    }

    It 'FrameMargins.None and a null margins argument are identity for both directions' {
        (($frameBounds::ExpandTarget(10, 20, 300, 200, $margins::None)) -join ',') | Should BeExactly '10,20,300,200'
        (($frameBounds::VisibleRect(10, 20, 300, 200, $margins::None)) -join ',') | Should BeExactly '10,20,300,200'
        (($frameBounds::ExpandTarget(10, 20, 300, 200, $null)) -join ',') | Should BeExactly '10,20,300,200'
        (($frameBounds::VisibleRect(10, 20, 300, 200, $null)) -join ',') | Should BeExactly '10,20,300,200'
        $margins::None.Left | Should Be 0
        $margins::None.Top | Should Be 0
        $margins::None.Right | Should Be 0
        $margins::None.Bottom | Should Be 0
    }

    It 'a WindowState built with margins exposes them; the legacy 7-arg constructor reports None' {
        $m = $margins::new(7, 0, -7, -7)
        $withMargins = [TerminalOrganizer.Core.Assignment.WindowState]::new(2864, 5, 463, 509, $false, $false, $false, $m)
        $withMargins.Margins.Left | Should Be 7
        $withMargins.Margins.Right | Should Be -7
        $withMargins.Margins.Bottom | Should Be -7
        $legacy = [TerminalOrganizer.Core.Assignment.WindowState]::new(0, 0, 400, 300, $false, $false, $false)
        [object]::ReferenceEquals($legacy.Margins, $margins::None) | Should Be $true
    }
}

# --- B5: dry-run scoping parity (night-design-2026-09-25 unit B5, test 6) ---
# The dry-run self-test must pin the production scope rule: a monitor's plan consumes
# only that monitor's windows (WindowScope.Filter; never re-implemented in PowerShell).
Describe 'B5 dry-run scope semantics' {
    It 'B5 dry-run plans no cross-monitor facts' {
        $ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $tool = Join-Path $repoRoot 'tools\organize-dryrun.ps1'
        $output = & $ps51 -NoProfile -ExecutionPolicy Bypass -File $tool -SelfTest
        $code = $LASTEXITCODE
        $code | Should Be 0
        (@($output | Where-Object { $_ -like 'PASS: b5/*' }).Count -ge 1) | Should Be $true
        (@($output | Where-Object { $_ -like 'FAIL: *' }).Count) | Should Be 0
    }
}
