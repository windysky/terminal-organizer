# P2 unit C1 (night-design-2026-09-25): deterministic overflow priority model.
# Pester 3.4.0, Windows PowerShell 5.1. Run through test.ps1, which builds first.
# Expected values come from the design doc, never from the code under test.

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here
$coreDll = Join-Path $repoRoot 'bin\TerminalOrganizer.Core.dll'
$appExe = Join-Path $repoRoot 'bin\TerminalOrganizer.App.exe'

# Byte-load keeps the files unlocked so build.ps1 can rebuild while this session runs.
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($coreDll))
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($appExe))

# One PriorityInput; null ManualRank/DeclaredRank mean "none" (Nullable[int] null).
function New-PInput {
    param(
        [string]$WindowId,
        [object]$ManualRank = $null,
        [object]$DeclaredRank = $null,
        [string]$DerivedClass = 'LocalSession',
        [bool]$Manager = $false,
        [bool]$FullScreen = $false,
        [bool]$StableOccupant = $false,
        [int]$ZOrderIndex = 0,
        [string]$MonitorKey = 'M1',
        [int]$Top = 0,
        [int]$Left = 0
    )
    $classEnum = [TerminalOrganizer.Core.Overflow.DerivedPriorityClass]::Parse(
        [TerminalOrganizer.Core.Overflow.DerivedPriorityClass], $DerivedClass)
    [TerminalOrganizer.Core.Overflow.PriorityInput]::new(
        $WindowId, $ManualRank, $DeclaredRank, $classEnum,
        $Manager, $FullScreen, $StableOccupant, $ZOrderIndex, $MonitorKey, $Top, $Left)
}

function Invoke-PResolve($PriorityInput) {
    [TerminalOrganizer.Core.Overflow.PriorityResolver]::Resolve($PriorityInput)
}

Describe 'C1 priority resolution' {
    It 'C1 manual rank overrides declared and derived' {
        $r = Invoke-PResolve (New-PInput 'w-manual' -ManualRank 700 -DeclaredRank 1 -DerivedClass 'LocalSession')
        $r.Source.ToString() | Should BeExactly 'Manual'
        $r.Rank | Should Be 700
        $r.Immovable | Should Be $false
        $r.ImmovableReason | Should BeNullOrEmpty
        # Out-of-range manual ranks (outside 0..999) are ignored, so the declared rank applies.
        $bad = Invoke-PResolve (New-PInput 'w-manual-bad' -ManualRank 1000 -DeclaredRank 1 -DerivedClass 'LocalSession')
        $bad.Source.ToString() | Should BeExactly 'Declared'
        $bad.Rank | Should Be 1
    }

    It 'C1 declared rank overrides derived' {
        $r = Invoke-PResolve (New-PInput 'w-decl' -DeclaredRank 8 -DerivedClass 'LocalSession')
        $r.Source.ToString() | Should BeExactly 'Declared'
        $r.Rank | Should Be 8
        $r.Immovable | Should Be $false
    }

    It 'C1 manager fullscreen stable are immovable' {
        # Manager also carries a manual rank: immovability precedes every rank source.
        $rows = @(
            @{ Input = (New-PInput 'm-mgr' -Manager $true -DerivedClass 'Manager' -ManualRank 500); Reason = 'manager' },
            @{ Input = (New-PInput 'm-fs' -FullScreen $true -DerivedClass 'FullScreen'); Reason = 'full-screen' },
            @{ Input = (New-PInput 'm-st' -StableOccupant $true -DerivedClass 'StableOccupant'); Reason = 'stable occupant' }
        )
        foreach ($row in $rows) {
            $r = Invoke-PResolve $row.Input
            $r.Immovable | Should Be $true
            $r.ImmovableReason | Should BeExactly $row.Reason
        }
    }

    It 'C1 derived priority table is pinned' {
        $pinned = @(
            @('WindowsNative', 100),
            @('LocalSession', 300),
            @('RemoteSession', 400),
            @('Unidentified', 500)
        )
        $ranks = @()
        foreach ($row in $pinned) {
            $r = Invoke-PResolve (New-PInput ('d-' + $row[0]) -DerivedClass $row[0])
            $r.Source.ToString() | Should BeExactly 'Derived'
            $ranks += $r.Rank
        }
        ($ranks -join ',') | Should BeExactly '100,300,400,500'
    }

    It 'C1 shuffled priority inputs sort identically' {
        $inputs = @(
            (New-PInput 'w-immov' -Manager $true -DerivedClass 'Manager'),
            (New-PInput 'w-decl8' -DeclaredRank 8),
            (New-PInput 'w-local' -DerivedClass 'LocalSession'),
            (New-PInput 'w-remote' -DerivedClass 'RemoteSession'),
            (New-PInput 'w-unk' -DerivedClass 'Unidentified'),
            (New-PInput 'w-man700' -ManualRank 700)
        )
        # Deterministic shuffle: input order differs from the expected output order.
        $shuffled = @($inputs[5], $inputs[0], $inputs[4], $inputs[2], $inputs[1], $inputs[3])
        $list = New-Object 'System.Collections.Generic.List[TerminalOrganizer.Core.Overflow.ResolvedPriority]'
        foreach ($i in $shuffled) { $list.Add((Invoke-PResolve $i)) }
        # Comparison delegate body touches no enclosing locals (PS 5.1 dynamic scoping).
        $list.Sort([Comparison[TerminalOrganizer.Core.Overflow.ResolvedPriority]] {
            param($a, $b)
            [TerminalOrganizer.Core.Overflow.PriorityResolver]::CompareForRedistribution($a, $b)
        })
        (($list | ForEach-Object { $_.WindowId }) -join '|') |
            Should BeExactly 'w-man700|w-unk|w-remote|w-local|w-decl8|w-immov'
    }

    It 'C1 equal ranks use captured Z-order then geometry then ID' {
        $cmp = [TerminalOrganizer.Core.Overflow.PriorityResolver]

        # All rows derive LocalSession (rank 300); only one captured key differs per pair.
        $zOld = Invoke-PResolve (New-PInput 'tie-z-old' -ZOrderIndex 5)
        $zNew = Invoke-PResolve (New-PInput 'tie-z-new' -ZOrderIndex 1)
        # EnumWindows index 0 is topmost; the larger captured index is older/lower, so first.
        $cmp::CompareForRedistribution($zOld, $zNew) | Should Be -1
        $cmp::CompareForRedistribution($zNew, $zOld) | Should Be 1

        $mon1 = Invoke-PResolve (New-PInput 'tie-mon-1' -MonitorKey 'MON-1')
        $mon2 = Invoke-PResolve (New-PInput 'tie-mon-2' -MonitorKey 'MON-2')
        $cmp::CompareForRedistribution($mon1, $mon2) | Should Be -1
        $cmp::CompareForRedistribution($mon2, $mon1) | Should Be 1

        $topLow = Invoke-PResolve (New-PInput 'tie-top-low' -Top 10)
        $topHigh = Invoke-PResolve (New-PInput 'tie-top-high' -Top 20)
        $cmp::CompareForRedistribution($topLow, $topHigh) | Should Be -1
        $cmp::CompareForRedistribution($topHigh, $topLow) | Should Be 1

        $leftLow = Invoke-PResolve (New-PInput 'tie-left-low' -Left 100)
        $leftHigh = Invoke-PResolve (New-PInput 'tie-left-high' -Left 200)
        $cmp::CompareForRedistribution($leftLow, $leftHigh) | Should Be -1
        $cmp::CompareForRedistribution($leftHigh, $leftLow) | Should Be 1

        $idA = Invoke-PResolve (New-PInput 'tie-a')
        $idB = Invoke-PResolve (New-PInput 'tie-b')
        $cmp::CompareForRedistribution($idA, $idB) | Should Be -1
        $cmp::CompareForRedistribution($idB, $idA) | Should Be 1

        $same = Invoke-PResolve (New-PInput 'tie-same')
        $cmp::CompareForRedistribution($same, $same) | Should Be 0
    }
}

Describe 'C1 priority override settings' {
    $tempDir = Join-Path ([IO.Path]::GetTempPath()) ('to-c1-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempDir | Out-Null
    $settingsPath = Join-Path $tempDir 'settings.json'

    function New-C1Selector([string]$Kind, [string]$Value) {
        $kindEnum = [TerminalOrganizer.Core.Assignment.ManagerSelectorKind]::Parse(
            [TerminalOrganizer.Core.Assignment.ManagerSelectorKind], $Kind)
        [TerminalOrganizer.Core.Assignment.ManagerSelector]::new($kindEnum, $Value, $null)
    }

    It 'C1 priority overrides serialize sorted and round-trip' {
        # Shuffled input rows; canonical saved order sorts by kind, then value ordinal, then rank.
        $shuffledRows = [TerminalOrganizer.App.PriorityOverride[]]@(
            [TerminalOrganizer.App.PriorityOverride]::new((New-C1Selector 'RawTitle' 'RAW-MGR'), 900),
            [TerminalOrganizer.App.PriorityOverride]::new((New-C1Selector 'Session' 'YODA2'), 50),
            [TerminalOrganizer.App.PriorityOverride]::new((New-C1Selector 'UserLabel' 'lab'), 0),
            [TerminalOrganizer.App.PriorityOverride]::new((New-C1Selector 'Session' 'YODA1'), 10)
        )
        $settings = [TerminalOrganizer.App.AppSettings]::new(
            'Ctrl+Alt+O', 'C:\temp\c1.log', $null, $false, $false, $false, $null, $shuffledRows)
        $store = [TerminalOrganizer.App.SettingsStore]::new($settingsPath)
        $store.Save($settings)

        # The saved JSON itself is in canonical order.
        $doc = ConvertFrom-Json ([IO.File]::ReadAllText($settingsPath))
        (($doc.priorityOverrides | ForEach-Object { $_.kind + ':' + $_.value + ':' + $_.rank }) -join '|') |
            Should BeExactly 'Session:YODA1:10|Session:YODA2:50|UserLabel:lab:0|RawTitle:RAW-MGR:900'

        # Round-trip: Load returns the same rows in the same canonical order.
        $loaded = $store.Load()
        (($loaded.PriorityOverrides | ForEach-Object {
            $_.Selector.Kind.ToString() + ':' + $_.Selector.Value + ':' + $_.Rank }) -join '|') |
            Should BeExactly 'Session:YODA1:10|Session:YODA2:50|UserLabel:lab:0|RawTitle:RAW-MGR:900'

        # Invalid persisted rank (1000 > 999) is ignored with a settings diagnostic; the
        # valid row survives. Sink is script-scoped: PS 5.1 delegates see only $script: vars.
        $script:C1DiagSink = New-Object 'System.Collections.Generic.List[string]'
        $diagStore = [TerminalOrganizer.App.SettingsStore]::new($settingsPath,
            [Action[string]]{ param($message) $script:C1DiagSink.Add($message) })
        $raw = '{"hotkey":"Ctrl+Alt+O","priorityOverrides":[' +
            '{"kind":"Session","value":"YODA1","rawTitleFallback":null,"rank":1000},' +
            '{"kind":"Session","value":"YODA2","rawTitleFallback":null,"rank":42}]}'
        [IO.File]::WriteAllText($settingsPath, $raw)
        $repaired = $diagStore.Load()
        $repaired.PriorityOverrides.Length | Should Be 1
        $repaired.PriorityOverrides[0].Selector.Value | Should BeExactly 'YODA2'
        $repaired.PriorityOverrides[0].Rank | Should Be 42
        @($script:C1DiagSink | Where-Object { $_ -like '*priority override*' }).Count | Should BeGreaterThan 0
    }

    Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
}
