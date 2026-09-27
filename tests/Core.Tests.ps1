# SPEC-LAYOUT-001 acceptance suite (Pester 3.4.0, Windows PowerShell 5.1).
# Run through test.ps1, which builds first and starts a fresh powershell.exe.
# Expected values come from acceptance.md and research.md, never from the code under test.

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here
$fixtures = Join-Path $here 'fixtures'
$dllPath = Join-Path $repoRoot 'bin\TerminalOrganizer.Core.dll'

# Byte-load keeps the DLL file unlocked so build.ps1 can rebuild while this session runs.
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dllPath))

$realCustomJson = [IO.File]::ReadAllText((Join-Path $fixtures 'custom-layouts.json'))
$realAppliedJson = [IO.File]::ReadAllText((Join-Path $fixtures 'applied-layouts.json'))
$synCustomJson = [IO.File]::ReadAllText((Join-Path $fixtures 'synthetic-custom-layouts.json'))
$synAppliedJson = [IO.File]::ReadAllText((Join-Path $fixtures 'synthetic-applied-layouts.json'))

function New-WorkArea([int]$Left, [int]$Top, [int]$Width, [int]$Height, [int]$Dpi) {
    New-Object TerminalOrganizer.Core.Geometry.WorkArea -ArgumentList $Left, $Top, $Width, $Height, $Dpi
}

function New-DeviceKey([string]$Monitor, [string]$Instance, [int]$Number, [string]$Serial, [string]$Desktop) {
    New-Object TerminalOrganizer.Core.Layouts.DeviceKey -ArgumentList $Monitor, $Instance, $Number, $Serial, $Desktop
}

function Read-Custom([string]$Json) { [TerminalOrganizer.Core.Layouts.FancyZonesJsonReader]::ParseCustomLayouts($Json) }
function Read-Applied([string]$Json) { [TerminalOrganizer.Core.Layouts.FancyZonesJsonReader]::ParseAppliedLayouts($Json) }

function Resolve-Uuid([string]$CustomJson, [string]$Uuid, $WorkArea) {
    [TerminalOrganizer.Core.Layouts.LayoutResolver]::ResolveCustomLayout($CustomJson, $Uuid, $WorkArea)
}

function Resolve-Key([string]$AppliedJson, [string]$CustomJson, $Key, $WorkArea) {
    [TerminalOrganizer.Core.Layouts.LayoutResolver]::ResolveByDeviceKey($AppliedJson, $CustomJson, $Key, $WorkArea)
}

function Get-Zones($Result) { ($Result.Zones | ForEach-Object { $_.ToString() }) -join ' ' }
function Get-Warnings($Result) { ($Result.Warnings | ForEach-Object { $_.ToString() }) -join ',' }
function Get-Order($Zones, [int]$ToleranceBp) { ([TerminalOrganizer.Core.Geometry.ZoneOrdering]::Order($Zones, $ToleranceBp)) -join ',' }

function Get-Entry($Document, [string]$Monitor) {
    $Document.Entries | Where-Object { $_.Device.Monitor -ceq $Monitor } | Select-Object -First 1
}

function Get-RealUuid([string]$Name) {
    $layout = (Read-Custom $realCustomJson).Layouts | Where-Object { $_.Name -ceq $Name } | Select-Object -First 1
    $layout.Uuid
}

function Resolve-RealByName([string]$Name, $WorkArea) { Resolve-Uuid $realCustomJson (Get-RealUuid $Name) $WorkArea }

function Get-InnerException($ErrorRecord) {
    $e = $ErrorRecord.Exception
    while ($e.InnerException -ne $null) { $e = $e.InnerException }
    $e
}

# Normalizes a research-format zone list ("z0:WxH@L,T z2:..." in emission order) to ascending id order.
function ConvertTo-IdOrder([string]$ZoneText) {
    $tokens = $ZoneText.Trim() -split '\s+'
    $sorted = $tokens | Sort-Object { [int](($_ -split ':')[0].Substring(1)) }
    $sorted -join ' '
}

$WA_A = New-WorkArea 0 0 1920 1152 96
$WA_B = New-WorkArea 0 0 1920 1032 96

$U_SYN_1X2 = '{5A000000-0000-4000-8000-000000000001}'
$U_HS_DEFAULTS = '{5A000000-0000-4000-8000-000000000006}'
$U_FRACTIONAL = '{5A000000-0000-4000-8000-000000000007}'
$U_HS_9999 = '{5A000000-0000-4000-8000-000000000010}'
$U_CANVAS = '{5A000000-0000-4000-8000-000000000011}'
$U_NEG_INDEX = '{5A000000-0000-4000-8000-000000000012}'
$U_SPACING_M30 = '{5A000000-0000-4000-8000-000000000013}'
$U_NEG_SIZE = '{5A000000-0000-4000-8000-000000000014}'
$U_DUP_INDEX = '{5A000000-0000-4000-8000-000000000015}'
$U_OVERFLOW = '{5A000000-0000-4000-8000-000000000016}'
$U_CANVAS_REF0 = '{5A000000-0000-4000-8000-000000000017}'
$U_CANVAS_EMPTY = '{5A000000-0000-4000-8000-000000000018}'
$U_SPACING_M20 = '{5A000000-0000-4000-8000-000000000019}'
$U_NEG_INDEX_M30 = '{5A000000-0000-4000-8000-000000000020}'
$U_DUP_INDEX_M30 = '{5A000000-0000-4000-8000-000000000021}'
$U_CANVAS_REF0_EMPTY = '{5A000000-0000-4000-8000-000000000022}'
$U_OVERFLOW_NEG = '{5A000000-0000-4000-8000-000000000023}'
$U_GAP = '{5A000000-0000-4000-8000-000000000024}'
$U_L_SHAPE = '{5A000000-0000-4000-8000-000000000025}'
$U_BELOW_ONE = '{5A000000-0000-4000-8000-000000000026}'
$U_ZERO_ROWS = '{5A000000-0000-4000-8000-000000000028}'
$U_ZERO_COLUMNS = '{5A000000-0000-4000-8000-000000000029}'
$U_CANVAS_EDGE = '{5A000000-0000-4000-8000-000000000030}'
$U_CANVAS_NEG_SIZE = '{5A000000-0000-4000-8000-000000000031}'
$U_CANVAS_OVERFLOW = '{5A000000-0000-4000-8000-000000000032}'
$U_NOT_PRESENT = '{5A000000-0000-4000-8000-0000000000FF}'

$K1_DESKTOP = '{AAAAAAAA-1111-4222-8333-444444444444}'

Describe 'AC-001 The real fixtures parse completely' {
    $custom = Read-Custom $realCustomJson
    $applied = Read-Applied $realAppliedJson

    It 'REAL-C yields 17 accepted grid layouts and 0 rejected entries' {
        $custom.IsValid | Should Be $true
        $custom.Layouts.Length | Should Be 17
        $custom.Rejected.Length | Should Be 0
        @($custom.Layouts | Where-Object { $_.Type -cne 'grid' }).Count | Should Be 0
    }

    It 'REAL-A yields 50 accepted entries by type (custom 19, priority-grid 26, grid 3, blank 2) and 0 rejected' {
        $applied.IsValid | Should Be $true
        $applied.Entries.Length | Should Be 50
        $applied.Rejected.Length | Should Be 0
        @($applied.Entries | Where-Object { $_.Type -ceq 'custom' }).Count | Should Be 19
        @($applied.Entries | Where-Object { $_.Type -ceq 'priority-grid' }).Count | Should Be 26
        @($applied.Entries | Where-Object { $_.Type -ceq 'grid' }).Count | Should Be 3
        @($applied.Entries | Where-Object { $_.Type -ceq 'blank' }).Count | Should Be 2
    }

    It 'maps the fields of "5 Terminals"' {
        $five = $custom.Layouts | Where-Object { $_.Name -ceq '5 Terminals' } | Select-Object -First 1
        $five.Grid.Rows | Should Be 2
        $five.Grid.Columns | Should Be 3
        ($five.Grid.RowsPercentage -join ',') | Should BeExactly '4971,5029'
        ($five.Grid.ColumnsPercentage -join ',') | Should BeExactly '2641,2628,4731'
        $map = $five.Grid.CellChildMap
        (($map | ForEach-Object { '[' + ($_ -join ',') + ']' }) -join ',') | Should BeExactly '[0,2,4],[1,3,4]'
        $five.Grid.Spacing | Should Be 5
    }

    It 'decodes the monitor-instance of REAL-A entry 14' {
        $applied.Entries[14].Device.MonitorInstance | Should BeExactly '5&1b98c55a&0&UID4354'
    }
}

Describe 'AC-002 Defaults and truncation' {
    $synC = Read-Custom $synCustomJson
    $synA = Read-Applied $synAppliedJson

    It 'applies custom grid defaults show-spacing true, spacing 16, sensitivity-radius 20' {
        $layout = $synC.Layouts | Where-Object { $_.Name -ceq 'Syn Half-Split Defaults' } | Select-Object -First 1
        $layout.Grid.ShowSpacing | Should Be $true
        $layout.Grid.Spacing | Should Be 16
        $layout.Grid.SensitivityRadius | Should Be 20
    }

    It 'resolves the defaulted Half-Split against WA-A to the Half-Split zones' {
        $r = Resolve-Uuid $synCustomJson $U_HS_DEFAULTS $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        Get-Zones $r | Should BeExactly 'z0:935x1120@16,16 z1:937x1120@967,16'
    }

    It 'accepts the applied entry with monitor-instance "", serial-number "", monitor-number 0, sensitivity-radius 20' {
        $entry = Get-Entry $synA 'SYN-DEFAULTS'
        $entry | Should Not BeNullOrEmpty
        $entry.Device.MonitorInstance | Should BeExactly ''
        $entry.Device.SerialNumber | Should BeExactly ''
        $entry.Device.MonitorNumber | Should Be 0
        $entry.SensitivityRadius | Should Be 20
    }

    It 'truncates spacing 16.9 to 16 and percentages 4999.7 and 5000.2 to 4999 and 5000' {
        $layout = $synC.Layouts | Where-Object { $_.Name -ceq 'Syn Fractional' } | Select-Object -First 1
        $layout.Grid.Spacing | Should Be 16
        ($layout.Grid.ColumnsPercentage -join ',') | Should BeExactly '4999,5000'
    }
}

Describe 'AC-003 Malformed documents and rejected entries' {
    It 'returns document-level MalformedJson for truncated JSON, a missing wrapper and a JSON type error, without throwing' {
        $texts = @('{"custom-layouts": [', '[]', '{"custom-layouts":[{"uuid":"{5A000000-0000-4000-8000-000000000001}","name":"x","type":"grid","info":{"rows":"two","columns":1,"rows-percentage":[10000],"columns-percentage":[10000],"cell-child-map":[[0]]}}]}')
        foreach ($t in $texts) {
            $doc = Read-Custom $t
            $doc.IsValid | Should Be $false
            $doc.Reason.ToString() | Should BeExactly 'MalformedJson'
            $doc.Detail | Should Not BeNullOrEmpty
        }
    }

    It 'returns MalformedJson for the same texts through the applied reader' {
        foreach ($t in @('{"applied-layouts": [', '[]', '{"applied-layouts":[{"device":{"monitor":"M","monitor-number":"three","virtual-desktop":"{AAAAAAAA-1111-4222-8333-444444444444}"}}]}')) {
            $doc = Read-Applied $t
            $doc.IsValid | Should Be $false
            $doc.Reason.ToString() | Should BeExactly 'MalformedJson'
            $doc.Detail | Should Not BeNullOrEmpty
        }
    }

    It 'rejects the SYN-C shape mismatches and malformed entries (including an unbraced GUID) with their positions and keeps the valid grid' {
        $doc = Read-Custom $synCustomJson
        $doc.IsValid | Should Be $true
        (($doc.Rejected | ForEach-Object { '{0}:{1}' -f $_.Position, $_.Reason }) -join ',') |
            Should BeExactly '0:ShapeMismatch,1:ShapeMismatch,2:MalformedEntry,3:MalformedEntry,4:MalformedEntry,5:MalformedEntry,12:ShapeMismatch'
        $doc.Rejected[2].Uuid | Should BeExactly 'not-a-guid'
        $doc.Rejected[5].Uuid | Should BeExactly '3efce2f7-0aa5-40d0-978b-a1ab24e6d21e'
        @($doc.Layouts | Where-Object { $_.Name -ceq 'Syn 1x2' }).Count | Should Be 1
        $doc.Layouts.Length | Should Be 28
    }

    It 'rejects custom canvas entries missing a required field as MalformedEntry' {
        $head = '{"custom-layouts":[{"uuid":"{5A000000-0000-4000-8000-000000000040}","name":"c","type":"canvas","info":'
        $infos = @(
            '{"ref-height":1000,"zones":[]}',
            '{"ref-width":1000,"zones":[]}',
            '{"ref-width":1000,"ref-height":1000}',
            '{"ref-width":1000,"ref-height":1000,"zones":[{"X":0,"Y":0,"width":100}]}'
        )
        foreach ($info in $infos) {
            $doc = Read-Custom ($head + $info + '}]}')
            ('{0} valid={1} accepted={2} rejected={3}' -f $info, $doc.IsValid, $doc.Layouts.Length, (($doc.Rejected | ForEach-Object { $_.Reason }) -join ',')) |
                Should BeExactly ('{0} valid=True accepted=0 rejected=MalformedEntry' -f $info)
        }
    }

    It 'rejects applied entries missing type, show-spacing or zone-count, or with an unbraced virtual-desktop, as MalformedEntry' {
        $device = '"device":{"monitor":"M","virtual-desktop":"{AAAAAAAA-1111-4222-8333-444444444444}"}'
        $layouts = @(
            '{"uuid":"{00000000-0000-0000-0000-000000000000}","show-spacing":true,"spacing":16,"zone-count":3}',
            '{"uuid":"{00000000-0000-0000-0000-000000000000}","type":"grid","spacing":16,"zone-count":3}',
            '{"uuid":"{00000000-0000-0000-0000-000000000000}","type":"grid","show-spacing":true,"spacing":16}'
        )
        foreach ($layout in $layouts) {
            $doc = Read-Applied ('{"applied-layouts":[{' + $device + ',"applied-layout":' + $layout + '}]}')
            ('{0} valid={1} accepted={2} rejected={3}' -f $layout, $doc.IsValid, $doc.Entries.Length, (($doc.Rejected | ForEach-Object { $_.Reason }) -join ',')) |
                Should BeExactly ('{0} valid=True accepted=0 rejected=MalformedEntry' -f $layout)
        }
        $unbraced = '{"applied-layouts":[{"device":{"monitor":"M","virtual-desktop":"aaaaaaaa-1111-4222-8333-444444444444"},"applied-layout":{"uuid":"{00000000-0000-0000-0000-000000000000}","type":"grid","show-spacing":true,"spacing":16,"zone-count":3}}]}'
        $doc = Read-Applied $unbraced
        ('accepted={0} rejected={1}' -f $doc.Entries.Length, (($doc.Rejected | ForEach-Object { $_.Reason }) -join ',')) |
            Should BeExactly 'accepted=0 rejected=MalformedEntry'
    }

    It 'rejects the SYN-A entries without spacing, without a device object and with a non-GUID virtual-desktop' {
        $doc = Read-Applied $synAppliedJson
        $doc.IsValid | Should Be $true
        (($doc.Rejected | ForEach-Object { '{0}:{1}' -f $_.Position, $_.Reason }) -join ',') |
            Should BeExactly '0:MalformedEntry,3:MalformedEntry,4:MalformedEntry,5:MalformedEntry'
        $doc.Rejected[2].Device | Should BeNullOrEmpty
        $doc.Rejected[3].Device.VirtualDesktop | Should BeExactly 'desktop-1'
        $doc.Entries.Length | Should Be 14
    }
}

Describe 'AC-004 Lookup and custom resolution branches' {
    $k1 = New-DeviceKey 'SYN-K1' '4&1aaa&0&UID101' 1 'SER-K1' $K1_DESKTOP

    It 'K1: the first accepted entry wins over a leading rejected entry and a later priority-grid entry' {
        $r = Resolve-Key $synAppliedJson $synCustomJson $k1 $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.LayoutName | Should BeExactly 'Syn 1x2'
        $r.LayoutType | Should BeExactly 'custom'
        Get-Zones $r | Should BeExactly 'z0:960x1152@0,0 z1:960x1152@960,0'
    }

    It 'K1 with a lower-case virtual-desktop matches by GUID value' {
        $k = New-DeviceKey 'SYN-K1' '4&1aaa&0&UID101' 1 'SER-K1' $K1_DESKTOP.ToLowerInvariant()
        $r = Resolve-Key $synAppliedJson $synCustomJson $k $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.LayoutName | Should BeExactly 'Syn 1x2'
    }

    It 'K1 with an upper-cased monitor-instance does not match (ordinal comparison)' {
        $k = New-DeviceKey 'SYN-K1' '4&1AAA&0&UID101' 1 'SER-K1' $K1_DESKTOP
        $r = Resolve-Key $synAppliedJson $synCustomJson $k $WA_A
        $r.Kind.ToString() | Should BeExactly 'Unsupported'
        $r.Reason.ToString() | Should BeExactly 'NoAppliedLayout'
    }

    It 'K2, matching only a rejected entry, is Invalid with MalformedEntry' {
        $k2 = New-DeviceKey 'SYN-K2' '4&2bbb&0&UID102' 2 'SER-K2' '{BBBBBBBB-1111-4222-8333-444444444444}'
        $r = Resolve-Key $synAppliedJson $synCustomJson $k2 $WA_A
        $r.Kind.ToString() | Should BeExactly 'Invalid'
        $r.Reason.ToString() | Should BeExactly 'MalformedEntry'
        $r.Zones.Length | Should Be 0
    }

    It 'K3, matching nothing, is Unsupported with NoAppliedLayout' {
        $k3 = New-DeviceKey 'SYN-K3' '4&3ddd&0&UID199' 9 'SER-K3' '{CCCCCCCC-1111-4222-8333-444444444444}'
        $r = Resolve-Key $synAppliedJson $synCustomJson $k3 $WA_A
        $r.Kind.ToString() | Should BeExactly 'Unsupported'
        $r.Reason.ToString() | Should BeExactly 'NoAppliedLayout'
    }

    $synA = Read-Applied $synAppliedJson

    It 'a custom entry whose uuid is absent from SYN-C is Unsupported with CustomLayoutNotFound' {
        $r = Resolve-Key $synAppliedJson $synCustomJson (Get-Entry $synA 'SYN-K4').Device $WA_A
        $r.Kind.ToString() | Should BeExactly 'Unsupported'
        $r.Reason.ToString() | Should BeExactly 'CustomLayoutNotFound'
    }

    It 'a custom entry whose uuid is carried only by the rejected rows-2 grid is Invalid with ShapeMismatch' {
        $r = Resolve-Key $synAppliedJson $synCustomJson (Get-Entry $synA 'SYN-K5').Device $WA_A
        $r.Kind.ToString() | Should BeExactly 'Invalid'
        $r.Reason.ToString() | Should BeExactly 'ShapeMismatch'
        $r.Zones.Length | Should Be 0
    }

    It 'a uuid carried by two accepted grids resolves to the last one (effective spacing 16)' {
        $r = Resolve-Key $synAppliedJson $synCustomJson (Get-Entry $synA 'SYN-K6').Device $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.EffectiveSpacing | Should Be 16
        $r.LayoutName | Should BeExactly 'Syn Dup Spacing 16'
    }

    It 'a uuid carried by an accepted grid followed by a rejected grid resolves Supported with the accepted grid' {
        $r = Resolve-Key $synAppliedJson $synCustomJson (Get-Entry $synA 'SYN-K7').Device $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.LayoutName | Should BeExactly 'Syn Accepted Before Rejected'
    }
}

Describe 'AC-005 Type classification and template descriptor' {
    $synA = Read-Applied $synAppliedJson
    $realA = Read-Applied $realAppliedJson

    It 'focus, blank and spiral are Unsupported with Focus, Blank and UnknownType, each carrying its type string' {
        $cases = @(@('SYN-FOCUS', 'Focus', 'focus'), @('SYN-BLANK', 'Blank', 'blank'), @('SYN-SPIRAL', 'UnknownType', 'spiral'))
        foreach ($c in $cases) {
            $r = Resolve-Key $synAppliedJson $synCustomJson (Get-Entry $synA $c[0]).Device $WA_A
            $r.Kind.ToString() | Should BeExactly 'Unsupported'
            $r.Reason.ToString() | Should BeExactly $c[1]
            $r.LayoutType | Should BeExactly $c[2]
        }
        $spiral = Resolve-Key $synAppliedJson $synCustomJson (Get-Entry $synA 'SYN-SPIRAL').Device $WA_A
        $spiral.Detail | Should Match 'spiral'
    }

    It 'the REAL-A blank entry (index 11) is Unsupported with Blank' {
        $realA.Entries[11].Type | Should BeExactly 'blank'
        $r = Resolve-Key $realAppliedJson $realCustomJson $realA.Entries[11].Device $WA_A
        $r.Kind.ToString() | Should BeExactly 'Unsupported'
        $r.Reason.ToString() | Should BeExactly 'Blank'
        $r.LayoutType | Should BeExactly 'blank'
    }

    It 'REAL-A entry 9 (priority-grid) is Supported with the SPEC-LAYOUT-002 pinned zones' {
        $r = Resolve-Key $realAppliedJson $realCustomJson $realA.Entries[9].Device $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.LayoutType | Should BeExactly 'priority-grid'
        $r.LayoutName | Should BeNullOrEmpty
        $r.EffectiveSpacing | Should Be 16
        Get-Warnings $r | Should BeExactly ''
        Get-Zones $r | Should BeExactly 'z0:456x1120@16,16 z1:944x1120@488,16 z2:456x1120@1448,16'
    }

    It 'SYN-A rows, columns and grid entries are Supported with the SPEC-LAYOUT-002 pinned zones' {
        $cases = @(
            @('SYN-ROWS', 'rows', 'z0:1888x362@16,16 z1:1888x363@16,394 z2:1888x363@16,773'),
            @('SYN-COLUMNS', 'columns', 'z0:618x1120@16,16 z1:619x1120@650,16 z2:619x1120@1285,16'),
            @('SYN-GRID', 'grid', 'z0:615x1120@16,16 z1:624x1120@647,16 z2:617x1120@1287,16')
        )
        foreach ($c in $cases) {
            $r = Resolve-Key $synAppliedJson $synCustomJson (Get-Entry $synA $c[0]).Device $WA_A
            $r.Kind.ToString() | Should BeExactly 'Supported'
            $r.LayoutType | Should BeExactly $c[1]
            Get-Warnings $r | Should BeExactly ''
            Get-Zones $r | Should BeExactly $c[2]
        }
    }

    It 'the focus entry against a work area of width 0 is still Unsupported Focus (type precedes work area)' {
        $r = Resolve-Key $synAppliedJson $synCustomJson (Get-Entry $synA 'SYN-FOCUS').Device (New-WorkArea 0 0 0 1152 96)
        $r.Kind.ToString() | Should BeExactly 'Unsupported'
        $r.Reason.ToString() | Should BeExactly 'Focus'
    }
}

Describe 'AC-006 Left7 and the Supported result fields' {
    $r = Resolve-RealByName 'Left7' $WA_A

    It 'is Supported with no warnings, type custom, name Left7 and effective spacing 0' {
        $r.Kind.ToString() | Should BeExactly 'Supported'
        Get-Warnings $r | Should BeExactly ''
        $r.LayoutType | Should BeExactly 'custom'
        $r.LayoutName | Should BeExactly 'Left7'
        $r.EffectiveSpacing | Should Be 0
    }

    It 'returns z0 1299x1152 @ 0,0 then z1 621x1152 @ 1299,0 in id order' {
        Get-Zones $r | Should BeExactly 'z0:1299x1152@0,0 z1:621x1152@1299,0'
    }

    It 'orders as 0,1' {
        Get-Order $r.Zones 100 | Should BeExactly '0,1'
    }
}

Describe 'AC-007 The stale applied snapshot is ignored' {
    $realA = Read-Applied $realAppliedJson
    $entry = $realA.Entries[14]

    It 'entry 14 is the stale Left7 snapshot for DELA07B' {
        $entry.Device.Monitor | Should BeExactly 'DELA07B'
        $entry.Device.MonitorInstance | Should BeExactly '5&1b98c55a&0&UID4354'
        $entry.Device.SerialNumber | Should BeExactly 'YMYH14CO3VKS'
        $entry.Device.VirtualDesktop | Should BeExactly '{A4E51EC3-B149-4070-A47D-1902CA048C5B}'
        $entry.Device.MonitorNumber | Should Be 3
        $entry.Type | Should BeExactly 'custom'
        $entry.ShowSpacing | Should Be $true
        $entry.Spacing | Should Be 16
        $entry.ZoneCount | Should Be 3
    }

    It 'resolves with the custom-layouts.json spacing 0, not the stale spacing 16, and no ZoneIndexGap' {
        $key = New-DeviceKey 'DELA07B' '5&1b98c55a&0&UID4354' 3 'YMYH14CO3VKS' '{A4E51EC3-B149-4070-A47D-1902CA048C5B}'
        $r = Resolve-Key $realAppliedJson $realCustomJson $key $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.LayoutName | Should BeExactly 'Left7'
        $r.EffectiveSpacing | Should Be 0
        Get-Zones $r | Should BeExactly 'z0:1299x1152@0,0 z1:621x1152@1299,0'
        Get-Zones $r | Should Not BeExactly 'z0:1275x1120@16,16 z1:597x1120@1307,16'
        Get-Warnings $r | Should BeExactly ''
    }
}

Describe 'AC-008 Half-Split and the index-based spacing rule' {
    It 'Half-Split resolves to z0 935x1120 @ 16,16 and z1 937x1120 @ 967,16' {
        $r = Resolve-RealByName 'Half-Split' $WA_A
        Get-Zones $r | Should BeExactly 'z0:935x1120@16,16 z1:937x1120@967,16'
        Get-Warnings $r | Should BeExactly ''
    }

    It 'the 9999-sum variant warns exactly PercentSumNot10000 and gives z1 936 wide (full spacing on the last column)' {
        $r = Resolve-Uuid $synCustomJson $U_HS_9999 $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        Get-Warnings $r | Should BeExactly 'PercentSumNot10000'
        Get-Zones $r | Should BeExactly 'z0:935x1120@16,16 z1:936x1120@967,16'
    }
}

Describe 'AC-009 Spacing 5 and a merged cell' {
    It '3-Terms right gives 4 px inner gaps and 5 px outer insets' {
        $r = Resolve-RealByName '3-Terms right' $WA_A
        Get-Zones $r | Should BeExactly 'z0:500x1142@5,5 z1:500x1142@509,5 z2:902x1142@1013,5'
    }

    It '5 Terminals computes the merged zone z4 902x1142 @ 1013,5' {
        $r = Resolve-RealByName '5 Terminals' $WA_A
        Get-Zones $r | Should BeExactly 'z0:500x565@5,5 z1:500x573@5,574 z2:500x565@509,5 z3:500x573@509,574 z4:902x1142@1013,5'
    }
}

Describe 'AC-010 The work-area origin offsets the rects' {
    It 'origin (1920,0) offsets both zones' {
        $r = Resolve-RealByName 'Half-Split' (New-WorkArea 1920 0 1920 1152 96)
        $r.Kind.ToString() | Should BeExactly 'Supported'
        Get-Zones $r | Should BeExactly 'z0:935x1120@1936,16 z1:937x1120@2887,16'
    }

    It 'origin (-1920,0) stays Supported because the -20 rule applies to relative rects' {
        $r = Resolve-RealByName 'Half-Split' (New-WorkArea -1920 0 1920 1152 96)
        $r.Kind.ToString() | Should BeExactly 'Supported'
        Get-Zones $r | Should BeExactly 'z0:935x1120@-1904,16 z1:937x1120@-953,16'
    }

    It 'origin (0,48) offsets z0 to 16,64' {
        $r = Resolve-RealByName 'Half-Split' (New-WorkArea 0 48 1920 1152 96)
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.Zones[0].ToString() | Should BeExactly 'z0:935x1120@16,64'
    }
}

Describe 'AC-011 All 17 real layouts at two work-area sizes' {
    # Transcribed verbatim from research.md "Worked geometry" (zones in emission order).
    $worked = @'
=== work area 1920x1152
Left7            sp=0  order=0,1  | z0:1299x1152@0,0 z1:621x1152@1299,0 | top-2 gap=52.19%
Righ8            sp=16 order=1,0  | z0:411x1120@16,16 z1:1461x1120@443,16 | top-2 gap=71.87%
ZoomTop          sp=16 order=1,0  | z0:1888x414@16,16 z1:1888x690@16,446 | top-2 gap=40%
Half-Split       sp=16 order=1,0  | z0:935x1120@16,16 z1:937x1120@967,16 | top-2 gap=0.21%
Left6            sp=16 order=0,1  | z0:1255x1120@16,16 z1:617x1120@1287,16 | top-2 gap=50.84%
Left4            sp=16 order=1,0  | z0:581x1120@16,16 z1:1291x1120@613,16 | top-2 gap=55%
2:4:4            sp=16 order=2,1,0  | z0:355x1120@16,16 z1:750x1120@387,16 z2:751x1120@1153,16 | top-2 gap=0.13%
1:5:4            sp=16 order=1,2,0  | z0:212x1120@16,16 z1:1027x1120@244,16 z2:617x1120@1287,16 | top-2 gap=39.92%
3-Terms Left     sp=0  order=0,2,1  | z0:999x1152@0,0 z1:460x1152@999,0 z2:461x1152@1459,0 | top-2 gap=53.85%
3-Terms right    sp=5  order=2,0,1  | z0:500x1142@5,5 z1:500x1142@509,5 z2:902x1142@1013,5 | top-2 gap=44.57%
5 Terminals      sp=5  order=4,1,3,0,2  | z0:500x565@5,5 z2:500x565@509,5 z4:902x1142@1013,5 z1:500x573@5,574 z3:500x573@509,574 | top-2 gap=72.19%
6 Terminals      sp=5  order=5,4,1,3,0,2  | z0:500x565@5,5 z2:500x565@509,5 z4:902x565@1013,5 z1:500x573@5,574 z3:500x573@509,574 z5:902x573@1013,574 | top-2 gap=1.4%
4-Terms          sp=5  order=1,3,0,2  | z0:474x1142@5,5 z1:475x1142@483,5 z2:474x1142@962,5 z3:475x1142@1440,5 | top-2 gap=0%
8-Terms          sp=5  order=3,7,1,5,2,6,0,4  | z0:474x567@5,5 z2:475x567@483,5 z4:474x567@962,5 z6:475x567@1440,5 z1:474x571@5,576 z3:475x571@483,576 z5:474x571@962,576 z7:475x571@1440,576 | top-2 gap=0%
7-Terms          sp=5  order=0,2,6,4,1,5,3  | z0:474x1142@5,5 z1:475x567@483,5 z3:474x567@962,5 z5:475x567@1440,5 z2:475x571@483,576 z4:474x571@962,576 z6:475x571@1440,576 | top-2 gap=49.89%
6 Terminals (1)  sp=5  order=1,0,3,5,2,4  | z0:900x565@5,5 z2:501x565@909,5 z4:501x565@1414,5 z1:900x573@5,574 z3:501x573@909,574 z5:501x573@1414,574 | top-2 gap=1.4%
6 Terminals (2)  sp=5  order=1,0,3,5,2,4  | z0:900x565@5,5 z2:501x565@909,5 z4:501x565@1414,5 z1:900x573@5,574 z3:501x573@909,574 z5:501x573@1414,574 | top-2 gap=1.4%
=== work area 1920x1032
Left7            sp=0  order=0,1  | z0:1299x1032@0,0 z1:621x1032@1299,0 | top-2 gap=52.19%
Righ8            sp=16 order=1,0  | z0:411x1000@16,16 z1:1461x1000@443,16 | top-2 gap=71.87%
ZoomTop          sp=16 order=1,0  | z0:1888x369@16,16 z1:1888x615@16,401 | top-2 gap=40%
Half-Split       sp=16 order=1,0  | z0:935x1000@16,16 z1:937x1000@967,16 | top-2 gap=0.21%
Left6            sp=16 order=0,1  | z0:1255x1000@16,16 z1:617x1000@1287,16 | top-2 gap=50.84%
Left4            sp=16 order=1,0  | z0:581x1000@16,16 z1:1291x1000@613,16 | top-2 gap=55%
2:4:4            sp=16 order=2,1,0  | z0:355x1000@16,16 z1:750x1000@387,16 z2:751x1000@1153,16 | top-2 gap=0.13%
1:5:4            sp=16 order=1,2,0  | z0:212x1000@16,16 z1:1027x1000@244,16 z2:617x1000@1287,16 | top-2 gap=39.92%
3-Terms Left     sp=0  order=0,2,1  | z0:999x1032@0,0 z1:460x1032@999,0 z2:461x1032@1459,0 | top-2 gap=53.85%
3-Terms right    sp=5  order=2,0,1  | z0:500x1022@5,5 z1:500x1022@509,5 z2:902x1022@1013,5 | top-2 gap=44.57%
5 Terminals      sp=5  order=4,1,3,0,2  | z0:500x506@5,5 z2:500x506@509,5 z4:902x1022@1013,5 z1:500x512@5,515 z3:500x512@509,515 | top-2 gap=72.23%
6 Terminals      sp=5  order=5,4,1,3,0,2  | z0:500x506@5,5 z2:500x506@509,5 z4:902x506@1013,5 z1:500x512@5,515 z3:500x512@509,515 z5:902x512@1013,515 | top-2 gap=1.17%
4-Terms          sp=5  order=1,3,0,2  | z0:474x1022@5,5 z1:475x1022@483,5 z2:474x1022@962,5 z3:475x1022@1440,5 | top-2 gap=0%
8-Terms          sp=5  order=3,7,1,5,2,6,0,4  | z0:474x507@5,5 z2:475x507@483,5 z4:474x507@962,5 z6:475x507@1440,5 z1:474x511@5,516 z3:475x511@483,516 z5:474x511@962,516 z7:475x511@1440,516 | top-2 gap=0%
7-Terms          sp=5  order=0,2,6,4,1,5,3  | z0:474x1022@5,5 z1:475x507@483,5 z3:474x507@962,5 z5:475x507@1440,5 z2:475x511@483,516 z4:474x511@962,516 z6:475x511@1440,516 | top-2 gap=49.89%
6 Terminals (1)  sp=5  order=1,0,3,5,2,4  | z0:900x506@5,5 z2:501x506@909,5 z4:501x506@1414,5 z1:900x512@5,515 z3:501x512@909,515 z5:501x512@1414,515 | top-2 gap=1.17%
6 Terminals (2)  sp=5  order=1,0,3,5,2,4  | z0:900x506@5,5 z2:501x506@909,5 z4:501x506@1414,5 z1:900x512@5,515 z3:501x512@909,515 z5:501x512@1414,515 | top-2 gap=1.17%
'@

    $cases = @()
    $height = 0
    foreach ($line in ($worked -split "`r?`n")) {
        if ($line -match '^=== work area 1920x(\d+)$') { $height = [int]$Matches[1]; continue }
        if ($line.Trim().Length -eq 0) { continue }
        $parts = $line -split ' \| '
        $name = ($parts[0] -replace '\s+sp=.*$', '')
        $cases += New-Object PSObject -Property @{ Name = $name; Height = $height; Expected = (ConvertTo-IdOrder $parts[1]) }
    }

    It 'transcribes 34 cases (17 layouts x 2 work areas)' {
        $cases.Count | Should Be 34
        @($cases | Where-Object { $_.Height -eq 1152 }).Count | Should Be 17
    }

    It 'matches every zone rect of every real layout at 1920x1152 and 1920x1032, with no warnings' {
        $mismatches = @()
        foreach ($c in $cases) {
            $wa = New-WorkArea 0 0 1920 $c.Height 96
            $r = Resolve-RealByName $c.Name $wa
            $actual = Get-Zones $r
            $warn = Get-Warnings $r
            if ($r.Kind.ToString() -cne 'Supported' -or $actual -cne $c.Expected -or $warn -ne '') {
                $mismatches += ('{0}@{1}: expected [{2}] got [{3}] kind={4} warnings=[{5}]' -f $c.Name, $c.Height, $c.Expected, $actual, $r.Kind, $warn)
            }
        }
        ($mismatches -join '; ') | Should BeExactly ''
    }

    It 'includes 6 Terminals and 8-Terms as named in acceptance.md' {
        Get-Zones (Resolve-RealByName '6 Terminals' $WA_A) |
            Should BeExactly 'z0:500x565@5,5 z1:500x573@5,574 z2:500x565@509,5 z3:500x573@509,574 z4:902x565@1013,5 z5:902x573@1013,574'
        Get-Zones (Resolve-RealByName '8-Terms' $WA_A) |
            Should BeExactly 'z0:474x567@5,5 z1:474x571@5,576 z2:475x567@483,5 z3:475x571@483,576 z4:474x567@962,5 z5:474x571@962,576 z6:475x567@1440,5 z7:475x571@1440,576'
    }
}

Describe 'AC-012 Canvas truncation' {
    It 'resolves the canvas against WA-A with truncation (796, not 797)' {
        $r = Resolve-Uuid $synCustomJson $U_CANVAS $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        Get-Zones $r | Should BeExactly 'z0:796x1152@0,0 z1:1114x576@796,0 z2:1114x576@796,576'
    }

    It 'resolves the canvas against 2880x1728 at DPI 144' {
        $r = Resolve-Uuid $synCustomJson $U_CANVAS (New-WorkArea 0 0 2880 1728 144)
        $r.Kind.ToString() | Should BeExactly 'Supported'
        Get-Zones $r | Should BeExactly 'z0:1195x1728@0,0 z1:1670x864@1195,0 z2:1670x864@1195,864'
    }

    It 'has effective spacing 0 although its applied entry says show-spacing true and spacing 16' {
        $synA = Read-Applied $synAppliedJson
        $entry = Get-Entry $synA 'SYN-K8'
        $entry.ShowSpacing | Should Be $true
        $entry.Spacing | Should Be 16
        $r = Resolve-Key $synAppliedJson $synCustomJson $entry.Device $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.EffectiveSpacing | Should Be 0
        $r.LayoutType | Should BeExactly 'custom'
        Get-Zones $r | Should BeExactly 'z0:796x1152@0,0 z1:1114x576@796,0 z2:1114x576@796,576'
    }
}

Describe 'AC-013 Invalid layouts and reason precedence' {
    $wa7680 = New-WorkArea 0 0 7680 1152 96

    It 'reports each single fault with its reason and zero zones' {
        $cases = @(
            @($U_NEG_INDEX, $WA_A, 'NegativeZoneIndex'),
            @($U_SPACING_M30, $WA_A, 'EdgeBelowMinimum'),
            @($U_NEG_SIZE, $WA_A, 'NegativeSize'),
            @($U_DUP_INDEX, $WA_A, 'DuplicateZoneIndex'),
            @($U_OVERFLOW, $wa7680, 'ArithmeticOverflow'),
            @($U_CANVAS_REF0, $WA_A, 'InvalidCanvasReference'),
            @($U_CANVAS_EMPTY, $WA_A, 'EmptyLayout')
        )
        foreach ($c in $cases) {
            $r = Resolve-Uuid $synCustomJson $c[0] $c[1]
            ('{0}:{1}:{2}' -f $c[2], $r.Kind, $r.Reason) | Should BeExactly ('{0}:Invalid:{0}' -f $c[2])
            $r.Zones.Length | Should Be 0
        }
    }

    It 'reports InvalidWorkArea for width 0 and for DPI 0' {
        foreach ($wa in @((New-WorkArea 0 0 0 1152 96), (New-WorkArea 0 0 1920 1152 0))) {
            $r = Resolve-RealByName 'Half-Split' $wa
            $r.Kind.ToString() | Should BeExactly 'Invalid'
            $r.Reason.ToString() | Should BeExactly 'InvalidWorkArea'
            $r.Zones.Length | Should Be 0
        }
    }

    It 'accepts a 1x2 grid with spacing -20 because an edge of -20 is allowed' {
        $r = Resolve-Uuid $synCustomJson $U_SPACING_M20 $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
    }

    It 'follows the precedence rule for multi-fault layouts' {
        $cases = @(
            @($U_NEG_INDEX_M30, $WA_A, 'NegativeZoneIndex'),
            @($U_DUP_INDEX_M30, $WA_A, 'EdgeBelowMinimum'),
            @($U_CANVAS_REF0_EMPTY, $WA_A, 'InvalidCanvasReference'),
            @($U_OVERFLOW_NEG, $wa7680, 'ArithmeticOverflow')
        )
        foreach ($c in $cases) {
            $r = Resolve-Uuid $synCustomJson $c[0] $c[1]
            ('{0}:{1}:{2}' -f $c[2], $r.Kind, $r.Reason) | Should BeExactly ('{0}:Invalid:{0}' -f $c[2])
            $r.Zones.Length | Should Be 0
        }
    }

    It 'checks the work area (step 4) before the custom uuid lookup (step 6)' {
        $r = Resolve-Uuid $synCustomJson $U_NOT_PRESENT (New-WorkArea 0 0 0 1152 96)
        $r.Kind.ToString() | Should BeExactly 'Invalid'
        $r.Reason.ToString() | Should BeExactly 'InvalidWorkArea'
    }

    It 'reports EmptyLayout for a grid with 0 rows and a grid with 0 columns, with no zones and no warnings' {
        foreach ($uuid in @($U_ZERO_ROWS, $U_ZERO_COLUMNS)) {
            $r = Resolve-Uuid $synCustomJson $uuid $WA_A
            ('{0}:{1}:{2}:{3}:[{4}]' -f $uuid, $r.Kind, $r.Reason, $r.Zones.Length, (Get-Warnings $r)) |
                Should BeExactly ('{0}:Invalid:EmptyLayout:0:[]' -f $uuid)
        }
    }

    It 'reports the canvas per-zone reasons EdgeBelowMinimum, NegativeSize and ArithmeticOverflow' {
        $cases = @(
            @($U_CANVAS_EDGE, 'EdgeBelowMinimum'),
            @($U_CANVAS_NEG_SIZE, 'NegativeSize'),
            @($U_CANVAS_OVERFLOW, 'ArithmeticOverflow')
        )
        foreach ($c in $cases) {
            $r = Resolve-Uuid $synCustomJson $c[0] $WA_A
            ('{0}:{1}:{2}' -f $c[1], $r.Kind, $r.Reason) | Should BeExactly ('{0}:Invalid:{0}' -f $c[1])
            $r.Zones.Length | Should Be 0
        }
    }

    It 'returns MalformedJson through the resolver for a malformed applied document (step 1) and custom document (step 5)' {
        $k1 = New-DeviceKey 'SYN-K1' '4&1aaa&0&UID101' 1 'SER-K1' $K1_DESKTOP
        $r = Resolve-Key '{"applied-layouts": [' $synCustomJson $k1 $WA_A
        ('{0}:{1}:{2}' -f $r.Kind, $r.Reason, $r.Zones.Length) | Should BeExactly 'Invalid:MalformedJson:0'
        $r = Resolve-Key $synAppliedJson '{"custom-layouts": [' $k1 $WA_A
        ('{0}:{1}:{2}' -f $r.Kind, $r.Reason, $r.Zones.Length) | Should BeExactly 'Invalid:MalformedJson:0'
    }

    It 'checks the work area (step 4) before the custom document (step 5)' {
        $r = Resolve-Uuid '{"custom-layouts": [' $U_SYN_1X2 (New-WorkArea 0 0 0 1152 96)
        ('{0}:{1}' -f $r.Kind, $r.Reason) | Should BeExactly 'Invalid:InvalidWorkArea'
    }

    It 'skips the custom document for a template entry (steps 5-6 apply to custom layouts only)' {
        $synA = Read-Applied $synAppliedJson
        $r = Resolve-Key $synAppliedJson '{"custom-layouts": [' (Get-Entry $synA 'SYN-GRID').Device $WA_A
        ('{0}:{1}' -f $r.Kind, $r.Reason) | Should BeExactly 'Supported:None'
    }
}

Describe 'AC-014 The other warnings' {
    It 'an index gap warns exactly ZoneIndexGap and keeps the zones' {
        $r = Resolve-Uuid $synCustomJson $U_GAP $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        Get-Warnings $r | Should BeExactly 'ZoneIndexGap'
        Get-Zones $r | Should BeExactly 'z0:960x1152@0,0 z2:960x1152@960,0'
    }

    It 'a single-top-left L-shape warns exactly NonRectangularMerge' {
        $r = Resolve-Uuid $synCustomJson $U_L_SHAPE $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        Get-Warnings $r | Should BeExactly 'NonRectangularMerge'
        Get-Zones $r | Should BeExactly 'z0:960x1152@0,0 z1:960x576@960,0'
    }

    It 'a zero percentage warns exactly PercentBelowOne and no NonRectangularMerge' {
        $r = Resolve-Uuid $synCustomJson $U_BELOW_ONE $WA_A
        $r.Kind.ToString() | Should BeExactly 'Supported'
        Get-Warnings $r | Should BeExactly 'PercentBelowOne'
        Get-Zones $r | Should BeExactly 'z0:0x1152@0,0 z1:1920x1152@0,0'
    }
}

Describe 'AC-015 The ordering contract' {
    $names = @('Half-Split', '6 Terminals', '8-Terms', '2:4:4', '4-Terms', '3-Terms Left', '7-Terms', '5 Terminals')
    $zonesByName = @{}
    foreach ($n in $names) { $zonesByName[$n] = (Resolve-RealByName $n $WA_A).Zones }

    It 'orders at tolBp 100 as listed in acceptance.md' {
        $expected = @('0,1', '5,4,1,3,0,2', '0,2,4,6,1,3,5,7', '1,2,0', '0,1,2,3', '0,1,2', '0,1,3,5,2,4,6', '4,1,3,0,2')
        $actual = @($names | ForEach-Object { Get-Order $zonesByName[$_] 100 })
        ($actual -join ' | ') | Should BeExactly ($expected -join ' | ')
    }

    It 'orders at tolBp 0 as listed in acceptance.md' {
        $expected = @('1,0', '5,4,1,3,0,2', '3,7,1,5,2,6,0,4', '2,1,0', '1,3,0,2', '0,2,1', '0,2,6,4,1,5,3', '4,1,3,0,2')
        $actual = @($names | ForEach-Object { Get-Order $zonesByName[$_] 0 })
        ($actual -join ' | ') | Should BeExactly ($expected -join ' | ')
    }

    It 'exposes a default of 100 and a default-tolerance entry point' {
        [TerminalOrganizer.Core.Geometry.ZoneOrdering]::DefaultToleranceBp | Should Be 100
        ([TerminalOrganizer.Core.Geometry.ZoneOrdering]::Order($zonesByName['Half-Split']) -join ',') | Should BeExactly '0,1'
    }

    It 'is independent of the input order (reversed and shuffled 6 Terminals)' {
        $z = $zonesByName['6 Terminals']
        $reversed = [TerminalOrganizer.Core.Geometry.Zone[]]@($z[5], $z[4], $z[3], $z[2], $z[1], $z[0])
        $shuffled = [TerminalOrganizer.Core.Geometry.Zone[]]@($z[3], $z[0], $z[5], $z[1], $z[4], $z[2])
        Get-Order $reversed 100 | Should BeExactly '5,4,1,3,0,2'
        Get-Order $shuffled 100 | Should BeExactly '5,4,1,3,0,2'
    }

    It 'raises an argument-out-of-range error for tolBp -1 and 10001' {
        foreach ($bad in @(-1, 10001)) {
            $caught = $null
            try { [TerminalOrganizer.Core.Geometry.ZoneOrdering]::Order($zonesByName['Half-Split'], $bad) } catch { $caught = $_ }
            $caught | Should Not BeNullOrEmpty
            (Get-InnerException $caught).GetType().FullName | Should BeExactly 'System.ArgumentOutOfRangeException'
        }
    }

    It 'orders 6 Terminals in pure top-left order at tolBp 10000' {
        Get-Order $zonesByName['6 Terminals'] 10000 | Should BeExactly '0,2,4,1,3,5'
    }
}

Describe 'AC-016 The toolchain' {
    $buildScript = Join-Path $repoRoot 'build.ps1'
    $testScript = Join-Path $repoRoot 'test.ps1'
    $ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $pwshCommand = Get-Command pwsh -ErrorAction SilentlyContinue
    $pwsh = if ($pwshCommand) { $pwshCommand.Path } else { Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe' }

    It 'build.ps1 pins /noconfig /target:library /langversion:5 /warnaserror and exactly eight /r: references in the Core invocation' {
        $text = [IO.File]::ReadAllText($buildScript)
        foreach ($flag in @('/noconfig', '/target:library', '/langversion:5', '/warnaserror')) {
            $text.Contains($flag) | Should Be $true
        }
        # SPEC-TRAY-007: the second (App winexe) target carries its own refs; the frozen
        # count applies to the Core invocation only - the substring before /target:winexe,
        # or the whole file when no winexe target exists yet (backward compatible).
        $winexeAt = $text.IndexOf('/target:winexe')
        $coreText = if ($winexeAt -ge 0) { $text.Substring(0, $winexeAt) } else { $text }
        ([regex]::Matches($coreText, '/r:')).Count | Should Be 8
        foreach ($asm in @('System.dll', 'System.Core.dll', 'System.Runtime.Serialization.dll', 'System.Xml.dll', 'System.Web.Extensions.dll', 'System.Management.dll', 'UIAutomationClient.dll', 'UIAutomationTypes.dll')) {
            $text.Contains($asm) | Should Be $true
        }
    }

    It 'the built DLL exists' {
        Test-Path $dllPath | Should Be $true
    }

    It 'build.ps1 succeeds with no warning while this session holds the DLL byte-loaded (from 5.1 and pwsh 7)' {
        foreach ($shell in @($ps51, $pwsh)) {
            Test-Path $shell | Should Be $true
            # csc writes diagnostics to stdout, so stdout alone carries any 'warning CS' line.
            $out = & $shell -NoProfile -ExecutionPolicy Bypass -File $buildScript
            $code = $LASTEXITCODE
            ('{0} exit={1}' -f $shell, $code) | Should BeExactly ('{0} exit=0' -f $shell)
            (($out | Out-String) -match 'warning CS') | Should Be $false
        }
    }

    It 'test.ps1 exits non-zero on a failing Pester file (from 5.1 and pwsh 7)' {
        $tmp = Join-Path ([IO.Path]::GetTempPath()) ('to-fail-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tmp | Out-Null
        try {
            Set-Content -Path (Join-Path $tmp 'Fail.Tests.ps1') -Value "Describe 'deliberate' { It 'fails' { 1 | Should Be 2 } }"
            foreach ($shell in @($ps51, $pwsh)) {
                $null = & $shell -NoProfile -ExecutionPolicy Bypass -File $testScript -TestPath $tmp
                $code = $LASTEXITCODE
                ('{0} nonzero={1}' -f $shell, ($code -ne 0)) | Should BeExactly ('{0} nonzero=True' -f $shell)
            }
        }
        finally {
            Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
        }
    }

    It 'tests/*.Tests.ps1 contain no generic method invocation' {
        $pattern = 'Make' + 'GenericMethod|\]::\w+\['
        $hits = @(Get-ChildItem -Path $here -Filter '*.Tests.ps1' | Select-String -Pattern $pattern)
        $hits.Count | Should Be 0
    }
}
