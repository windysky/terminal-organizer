# SPEC-LAYOUT-002 acceptance suite (Pester 3.4.0, Windows PowerShell 5.1).
# Run through test.ps1, which builds first and starts a fresh powershell.exe.
# Expected values come from SPEC-LAYOUT-002 acceptance.md and plan.md §J, never from the code under test.

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here
$fixtures = Join-Path $here 'fixtures'
$dllPath = Join-Path $repoRoot 'bin\TerminalOrganizer.Core.dll'

# Byte-load keeps the DLL file unlocked so build.ps1 can rebuild while this session runs.
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dllPath))

$realCustomJson = [IO.File]::ReadAllText((Join-Path $fixtures 'custom-layouts.json'))
$realAppliedJson = [IO.File]::ReadAllText((Join-Path $fixtures 'applied-layouts.json'))
$synTemplatesJson = [IO.File]::ReadAllText((Join-Path $fixtures 'synthetic-applied-templates.json'))

function New-WorkArea([int]$Left, [int]$Top, [int]$Width, [int]$Height, [int]$Dpi) {
    New-Object TerminalOrganizer.Core.Geometry.WorkArea -ArgumentList $Left, $Top, $Width, $Height, $Dpi
}

function New-DeviceKey([string]$Monitor, [string]$Instance, [int]$Number, [string]$Serial, [string]$Desktop) {
    New-Object TerminalOrganizer.Core.Layouts.DeviceKey -ArgumentList $Monitor, $Instance, $Number, $Serial, $Desktop
}

function Resolve-Template([string]$Type, [int]$ZoneCount, [bool]$ShowSpacing, [int]$Spacing, $WorkArea) {
    [TerminalOrganizer.Core.Layouts.LayoutResolver]::ResolveTemplate($Type, $ZoneCount, $ShowSpacing, $Spacing, $WorkArea)
}

function Resolve-Key([string]$AppliedJson, [string]$CustomJson, $Key, $WorkArea) {
    [TerminalOrganizer.Core.Layouts.LayoutResolver]::ResolveByDeviceKey($AppliedJson, $CustomJson, $Key, $WorkArea)
}

function Get-Zones($Result) { ($Result.Zones | ForEach-Object { $_.ToString() }) -join ' ' }
function Get-Warnings($Result) { ($Result.Warnings | ForEach-Object { $_.ToString() }) -join ',' }
function Get-Order($Zones) { ([TerminalOrganizer.Core.Geometry.ZoneOrdering]::Order($Zones, 100)) -join ',' }

function Get-Entry($Document, [string]$Monitor) {
    $Document.Entries | Where-Object { $_.Device.Monitor -ceq $Monitor } | Select-Object -First 1
}

$WA_A = New-WorkArea 0 0 1920 1152 96
$T_DESKTOP = '{F0000000-0000-4000-8000-000000000001}'

$AC001_ZONES = 'z0:456x1120@16,16 z1:944x1120@488,16 z2:456x1120@1448,16'

Describe 'AC-001 The real default priority-grid entry' {
    $realA = [TerminalOrganizer.Core.Layouts.FancyZonesJsonReader]::ParseAppliedLayouts($realAppliedJson)

    It 'REAL-A entry 9 resolves Supported with type priority-grid, empty name, spacing 16 and no warnings' {
        $r = Resolve-Key $realAppliedJson $realCustomJson $realA.Entries[9].Device $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.LayoutType | Should BeExactly 'priority-grid'
        $r.LayoutName | Should BeNullOrEmpty
        $r.EffectiveSpacing | Should Be 16
        Get-Warnings $r | Should BeExactly ''
    }

    It 'REAL-A entry 9 gives z0 456x1120@16,16, z1 944x1120@488,16 and z2 456x1120@1448,16' {
        $r = Resolve-Key $realAppliedJson $realCustomJson $realA.Entries[9].Device $WA_A
        Get-Zones $r | Should BeExactly $AC001_ZONES
    }

    It 'the greedy order at tolBp 100 is 1,0,2' {
        $r = Resolve-Key $realAppliedJson $realCustomJson $realA.Entries[9].Device $WA_A
        Get-Order $r.Zones | Should BeExactly '1,0,2'
    }
}

Describe 'AC-002 The priority-grid table for n = 1 to 10' {
    # Pinned in acceptance.md AC-002 (provenance plan.md §J.2: the reference port over the §J.1 inputs).
    $expected = @(
        'z0:1888x1120@16,16',
        'z0:1256x1120@16,16 z1:616x1120@1288,16',
        'z0:456x1120@16,16 z1:944x1120@488,16 z2:456x1120@1448,16',
        'z0:456x1120@16,16 z1:944x1120@488,16 z2:456x552@1448,16 z3:456x552@1448,584',
        'z0:456x552@16,16 z1:944x1120@488,16 z2:456x552@1448,16 z3:456x552@16,584 z4:456x552@1448,584',
        'z0:456x744@16,16 z1:944x1120@488,16 z2:456x359@1448,16 z3:456x369@1448,391 z4:456x360@16,776 z5:456x360@1448,776',
        'z0:456x359@16,16 z1:944x1120@488,16 z2:456x359@1448,16 z3:456x369@16,391 z4:456x369@1448,391 z5:456x360@16,776 z6:456x360@1448,776',
        'z0:456x359@16,16 z1:464x1120@488,16 z2:464x1120@968,16 z3:456x359@1448,16 z4:456x369@16,391 z5:456x369@1448,391 z6:456x360@16,776 z7:456x360@1448,776',
        'z0:456x359@16,16 z1:464x1120@488,16 z2:464x744@968,16 z3:456x359@1448,16 z4:456x369@16,391 z5:456x369@1448,391 z6:456x360@16,776 z7:464x360@968,776 z8:456x360@1448,776',
        'z0:456x359@16,16 z1:464x1120@488,16 z2:464x359@968,16 z3:456x359@1448,16 z4:456x369@16,391 z5:464x369@968,391 z6:456x369@1448,391 z7:456x360@16,776 z8:464x360@968,776 z9:456x360@1448,776'
    )

    It 'each n from 1 to 10 is Supported with exactly n zones, no warnings, and the pinned rects' {
        for ($n = 1; $n -le 10; $n++) {
            $r = Resolve-Template 'priority-grid' $n $true 16 $WA_A
            ('n={0} kind={1} zones={2}' -f $n, $r.Kind, $r.Zones.Length) | Should BeExactly ('n={0} kind=Supported zones={0}' -f $n)
            Get-Warnings $r | Should BeExactly ''
            Get-Zones $r | Should BeExactly $expected[$n - 1]
        }
    }

    It 'for n = 5 the greedy order at tolBp 100 is 1,0,2,3,4' {
        $r = Resolve-Template 'priority-grid' 5 $true 16 $WA_A
        Get-Order $r.Zones | Should BeExactly '1,0,2,3,4'
    }
}

Describe 'AC-003 priority-grid with 11 zones falls back to grid' {
    It 'direct priority-grid n=11 and direct grid n=11 give the same 11 zones on a 3x4 grid' {
        $p = Resolve-Template 'priority-grid' 11 $true 16 $WA_A
        $g = Resolve-Template 'grid' 11 $true 16 $WA_A
        $p.Kind.ToString() | Should BeExactly 'Supported'
        $g.Kind.ToString() | Should BeExactly 'Supported'
        $p.Zones.Length | Should Be 11
        Get-Zones $p | Should BeExactly (Get-Zones $g)
    }

    It 'includes z0 456x359@16,16, z4 456x368@16,391 and the absorbing z10 936x361@968,775' {
        $r = Resolve-Template 'grid' 11 $true 16 $WA_A
        $r.Zones[0].ToString() | Should BeExactly 'z0:456x359@16,16'
        $r.Zones[4].ToString() | Should BeExactly 'z4:456x368@16,391'
        $r.Zones[10].ToString() | Should BeExactly 'z10:936x361@968,775'
    }
}

Describe 'AC-004 grid template, including the real grid entry' {
    It 'direct grid n=3 gives z0 615x1120@16,16, z1 624x1120@647,16 and z2 617x1120@1287,16' {
        $r = Resolve-Template 'grid' 3 $true 16 $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        Get-Warnings $r | Should BeExactly ''
        Get-Zones $r | Should BeExactly 'z0:615x1120@16,16 z1:624x1120@647,16 z2:617x1120@1287,16'
    }

    It 'direct grid n=5 gives the 2x3 layout with the absorbing z4 1257x552@647,584' {
        $r = Resolve-Template 'grid' 5 $true 16 $WA_A
        Get-Zones $r | Should BeExactly 'z0:615x552@16,16 z1:624x552@647,16 z2:617x552@1287,16 z3:615x552@16,584 z4:1257x552@647,584'
    }

    It 'REAL-A entry 1 (grid, 3 zones, spacing 16) gives the n=3 zones with layout type grid' {
        $realA = [TerminalOrganizer.Core.Layouts.FancyZonesJsonReader]::ParseAppliedLayouts($realAppliedJson)
        $realA.Entries[1].Type | Should BeExactly 'grid'
        $realA.Entries[1].ZoneCount | Should Be 3
        $r = Resolve-Key $realAppliedJson $realCustomJson $realA.Entries[1].Device $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.LayoutType | Should BeExactly 'grid'
        Get-Zones $r | Should BeExactly 'z0:615x1120@16,16 z1:624x1120@647,16 z2:617x1120@1287,16'
    }
}

Describe 'AC-005 rows template, direct and through a device key' {
    It 'direct rows n=3 gives z0 1888x362@16,16, z1 1888x363@16,394 and z2 1888x363@16,773 in id order' {
        $r = Resolve-Template 'rows' 3 $true 16 $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        Get-Warnings $r | Should BeExactly ''
        Get-Zones $r | Should BeExactly 'z0:1888x362@16,16 z1:1888x363@16,394 z2:1888x363@16,773'
    }

    It 'the SYN-T rows entry resolves by its device key to the same zones with layout type rows' {
        $doc = [TerminalOrganizer.Core.Layouts.FancyZonesJsonReader]::ParseAppliedLayouts($synTemplatesJson)
        $key = (Get-Entry $doc 'SYN-T-ROWS').Device
        $key | Should Not BeNullOrEmpty
        $r = Resolve-Key $synTemplatesJson $realCustomJson $key $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.LayoutType | Should BeExactly 'rows'
        Get-Zones $r | Should BeExactly 'z0:1888x362@16,16 z1:1888x363@16,394 z2:1888x363@16,773'
    }
}

Describe 'AC-006 columns template, device key and origin offset' {
    It 'direct columns n=3 gives z0 618x1120@16,16, z1 619x1120@650,16 and z2 619x1120@1285,16' {
        $r = Resolve-Template 'columns' 3 $true 16 $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        Get-Zones $r | Should BeExactly 'z0:618x1120@16,16 z1:619x1120@650,16 z2:619x1120@1285,16'
    }

    It 'the SYN-T columns entry at work-area origin (1920,0) offsets the lefts to 1936, 2570 and 3205' {
        $doc = [TerminalOrganizer.Core.Layouts.FancyZonesJsonReader]::ParseAppliedLayouts($synTemplatesJson)
        $key = (Get-Entry $doc 'SYN-T-COLUMNS').Device
        $wa = New-WorkArea 1920 0 1920 1152 96
        $r = Resolve-Key $synTemplatesJson $realCustomJson $key $wa
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.LayoutType | Should BeExactly 'columns'
        Get-Zones $r | Should BeExactly 'z0:618x1120@1936,16 z1:619x1120@2570,16 z2:619x1120@3205,16'
    }
}

Describe 'AC-007 show-spacing false, and the direct type string' {
    It 'direct priority-grid n=3 with show-spacing false gives 480/960/480 wide zones at spacing 0' {
        $r = Resolve-Template 'priority-grid' 3 $false 16 $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.EffectiveSpacing | Should Be 0
        $r.LayoutType | Should BeExactly 'priority-grid'
        $r.LayoutName | Should BeNullOrEmpty
        Get-Zones $r | Should BeExactly 'z0:480x1152@0,0 z1:960x1152@480,0 z2:480x1152@1440,0'
    }

    It 'direct rows n=3 with show-spacing false gives three 1920x384 zones at 0, 384 and 768' {
        $r = Resolve-Template 'rows' 3 $false 16 $WA_A
        Get-Zones $r | Should BeExactly 'z0:1920x384@0,0 z1:1920x384@0,384 z2:1920x384@0,768'
    }
}

Describe 'AC-008 Zone-count bounds and precedence' {
    It 'n of 0 or -1 gives Invalid InvalidZoneCount with zero zones' {
        foreach ($n in @(0, -1)) {
            $r = Resolve-Template 'priority-grid' $n $true 16 $WA_A
            ('n={0} kind={1} reason={2} zones={3}' -f $n, $r.Kind, $r.Reason, $r.Zones.Length) |
                Should BeExactly ('n={0} kind=Invalid reason=InvalidZoneCount zones=0' -f $n)
        }
    }

    It 'n of 129 or 2147483647 gives Invalid ZoneCountOutOfRange without throwing and in under 1 second' {
        foreach ($n in @(129, 2147483647)) {
            $elapsed = Measure-Command { $r = Resolve-Template 'priority-grid' $n $true 16 $WA_A }
            ('n={0} kind={1} reason={2} zones={3}' -f $n, $r.Kind, $r.Reason, $r.Zones.Length) |
                Should BeExactly ('n={0} kind=Invalid reason=ZoneCountOutOfRange zones=0' -f $n)
            ($elapsed.TotalSeconds -lt 1) | Should Be $true
        }
    }

    It 'grid with n=128 returns Supported with exactly 128 zones' {
        $r = Resolve-Template 'grid' 128 $true 16 $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.Zones.Length | Should Be 128
        Get-Warnings $r | Should BeExactly ''
    }

    It 'the SYN-T grid entry with zone-count 0 is Invalid InvalidZoneCount by its device key' {
        $doc = [TerminalOrganizer.Core.Layouts.FancyZonesJsonReader]::ParseAppliedLayouts($synTemplatesJson)
        $key = (Get-Entry $doc 'SYN-T-GRID-ZERO').Device
        $r = Resolve-Key $synTemplatesJson $realCustomJson $key $WA_A
        ('{0}:{1}:{2}' -f $r.Kind, $r.Reason, $r.Zones.Length) | Should BeExactly 'Invalid:InvalidZoneCount:0'
    }

    It 'n=0 against a work area of width 0 gives InvalidWorkArea, because step 4 precedes step 7' {
        $r = Resolve-Template 'priority-grid' 0 $true 16 (New-WorkArea 0 0 0 1152 96)
        ('{0}:{1}' -f $r.Kind, $r.Reason) | Should BeExactly 'Invalid:InvalidWorkArea'
    }

    It 'direct rows n=3 with spacing 400 gives Invalid NegativeSize (zone 0 spans top 400 to bottom 251)' {
        $r = Resolve-Template 'rows' 3 $true 400 $WA_A
        ('{0}:{1}:{2}' -f $r.Kind, $r.Reason, $r.Zones.Length) | Should BeExactly 'Invalid:NegativeSize:0'
    }
}

Describe 'AC-009 TemplateGeometryPending is retired' {
    It 'TemplateGeometryPending is absent from the public reason enumeration' {
        $names = [enum]::GetNames([TerminalOrganizer.Core.Layouts.LayoutReason])
        ($names -contains 'TemplateGeometryPending') | Should Be $false
    }

    It 'InvalidZoneCount and ZoneCountOutOfRange are present in the public reason enumeration' {
        $names = [enum]::GetNames([TerminalOrganizer.Core.Layouts.LayoutReason])
        ($names -contains 'InvalidZoneCount') | Should Be $true
        ($names -contains 'ZoneCountOutOfRange') | Should Be $true
    }
}

Describe 'AC-010 Spacing is raw pixels at DPI 144' {
    It 'priority-grid n=3 on 2880x1728 at DPI 144 keeps 16 px insets (not 24 px DPI-scaled ones)' {
        $r = Resolve-Template 'priority-grid' 3 $true 16 (New-WorkArea 0 0 2880 1728 144)
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.EffectiveSpacing | Should Be 16
        Get-Zones $r | Should BeExactly 'z0:696x1696@16,16 z1:1424x1696@728,16 z2:696x1696@2168,16'
    }
}

Describe 'AC-011 Templates do not depend on the custom-layouts document' {
    $realA = [TerminalOrganizer.Core.Layouts.FancyZonesJsonReader]::ParseAppliedLayouts($realAppliedJson)
    $key = $realA.Entries[9].Device

    It 'a malformed custom-layouts document leaves REAL-A entry 9 Supported with the AC-001 zones' {
        $r = Resolve-Key $realAppliedJson '{"custom-layouts": [' $key $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.Reason.ToString() | Should BeExactly 'None'
        Get-Zones $r | Should BeExactly $AC001_ZONES
    }

    It 'an empty custom-layouts document leaves REAL-A entry 9 Supported with the AC-001 zones' {
        $r = Resolve-Key $realAppliedJson '' $key $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        Get-Zones $r | Should BeExactly $AC001_ZONES
    }
}

Describe 'AC-012 Template overflow is checked at step 8' {
    It 'direct rows n=3 with spacing 1000000000 gives Invalid ArithmeticOverflow, not NegativeSize' {
        $r = Resolve-Template 'rows' 3 $true 1000000000 $WA_A
        ('{0}:{1}:{2}' -f $r.Kind, $r.Reason, $r.Zones.Length) | Should BeExactly 'Invalid:ArithmeticOverflow:0'
        $r.Reason.ToString() | Should Not BeExactly 'NegativeSize'
    }
}
