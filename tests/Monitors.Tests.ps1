# SPEC-MON-003 acceptance suite (Pester 3.4.0, Windows PowerShell 5.1).
# Run through test.ps1, which builds first and starts a fresh powershell.exe.
# Expected values come from acceptance.md and plan.md section J, never from the code under test.

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here
$fixtures = Join-Path $here 'fixtures'
$dllPath = Join-Path $repoRoot 'bin\TerminalOrganizer.Core.dll'

# Byte-load keeps the DLL file unlocked so build.ps1 can rebuild while this session runs.
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dllPath))

$realAppliedJson = [IO.File]::ReadAllText((Join-Path $fixtures 'applied-layouts.json'))
$realCustomJson = [IO.File]::ReadAllText((Join-Path $fixtures 'custom-layouts.json'))

# MON-14 identity (acceptance.md Conventions; plan.md section J.1).
$MON14_PATH = '\\?\DISPLAY#DELA07B#5&1b98c55a&0&UID4354#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}'
$MON14_ID = 'DELA07B'
$MON14_INSTANCE = '5&1b98c55a&0&UID4354'
$MON14_NUMBER = 3
$MON14_SERIAL = 'YMYH14CO3VKS'
$DESKTOP_1 = '{A4E51EC3-B149-4070-A47D-1902CA048C5B}'
$DESKTOP_2 = '{E66ED430-0000-0000-0000-000000000000}'

function Parse-Path([string]$Path) {
    [TerminalOrganizer.Core.Monitors.InterfacePathParser]::Parse($Path)
}

function Parse-Serial([byte[]]$Edid) {
    [TerminalOrganizer.Core.Monitors.EdidParser]::ParseSerial($Edid)
}

# Builds a section J.2 synthetic EDID: block-0 descriptor at offset 54, tag 0xFF,
# header FF 00 00 00, then the given 14 (or fewer, zero-padded) text bytes.
function New-SyntheticEdid([byte[]]$TextBytes) {
    $edid = New-Object byte[] 128
    $edid[54] = 0xFF
    $edid[55] = 0x00
    $edid[56] = 0x00
    $edid[57] = 0x00
    for ($i = 0; $i -lt $TextBytes.Length; $i++) { $edid[58 + $i] = $TextBytes[$i] }
    ,$edid
}

Describe 'AC-001 Interface path parsing' {
    It 'parses the documented shape into id DELA07B and instance 5&1b98c55a&0&UID4354' {
        $parts = Parse-Path $MON14_PATH
        $parts.MonitorId | Should BeExactly $MON14_ID
        $parts.Instance | Should BeExactly $MON14_INSTANCE
    }

    It 'returns empty id and instance for garbage, ONLYONE, the empty string and null, without throwing' {
        foreach ($bad in @('garbage', '\\?\DISPLAY#ONLYONE', '', $null)) {
            $parts = Parse-Path $bad
            ('id=[{0}] instance=[{1}]' -f $parts.MonitorId, $parts.Instance) | Should BeExactly 'id=[] instance=[]'
        }
    }

    It 'parsing is ordinal: a lower-case id stays lower-case and does not yield DELA07B' {
        $parts = Parse-Path '\\?\DISPLAY#dela07b#5&1b98c55a&0&UID4354#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}'
        $parts.MonitorId | Should BeExactly 'dela07b'
        $parts.MonitorId | Should Not BeExactly 'DELA07B'
    }
}

Describe 'AC-002 EDID serial parsing' {
    It 'case A: YMYH14CO3VKS + 0x0A + space pad parses to YMYH14CO3VKS' {
        $text = [System.Text.Encoding]::ASCII.GetBytes('YMYH14CO3VKS')
        $bytes = New-Object byte[] ($text.Length + 2)
        [Array]::Copy($text, 0, $bytes, 0, $text.Length)
        $bytes[$text.Length] = 0x0A
        $bytes[$text.Length + 1] = 0x20
        Parse-Serial (New-SyntheticEdid $bytes) | Should BeExactly 'YMYH14CO3VKS'
    }

    It 'case B: leading space + SN-001 + 0x0A parses to SN-001' {
        $text = [System.Text.Encoding]::ASCII.GetBytes(' SN-001')
        $bytes = New-Object byte[] ($text.Length + 1)
        [Array]::Copy($text, 0, $bytes, 0, $text.Length)
        $bytes[$text.Length] = 0x0A
        Parse-Serial (New-SyntheticEdid $bytes) | Should BeExactly 'SN-001'
    }

    It 'case C: an EDID with no 0xFF descriptor returns the empty string' {
        Parse-Serial (New-Object byte[] 128) | Should BeExactly ''
    }

    It 'case D: a 10-byte input returns the empty string, and so does a null input' {
        Parse-Serial (New-Object byte[] 10) | Should BeExactly ''
        Parse-Serial $null | Should BeExactly ''
    }
}

function New-MonitorIdentity([string]$MonitorId, [string]$Instance, [int]$Number, [string]$Serial) {
    New-Object TerminalOrganizer.Core.Monitors.MonitorIdentity -ArgumentList $MonitorId, $Instance, $Number, $Serial
}

function New-DeviceKey([string]$Monitor, [string]$Instance, [int]$Number, [string]$Serial, [string]$Desktop) {
    New-Object TerminalOrganizer.Core.Layouts.DeviceKey -ArgumentList $Monitor, $Instance, $Number, $Serial, $Desktop
}

function Read-Applied([string]$Json) { [TerminalOrganizer.Core.Layouts.FancyZonesJsonReader]::ParseAppliedLayouts($Json) }

function Test-Match($EntryDevice, $Identity, [string]$Desktop) {
    [TerminalOrganizer.Core.Monitors.AppliedEntrySelector]::Match($EntryDevice, $Identity, $Desktop)
}

function Select-Entry($Document, $Identity, [string]$Desktop) {
    [TerminalOrganizer.Core.Monitors.AppliedEntrySelector]::Select($Document, $Identity, $Desktop)
}

function Resolve-Desktop([string]$Win10, [string]$Win11, [string]$Window) {
    [TerminalOrganizer.Core.Monitors.DesktopIdResolver]::Resolve($Win10, $Win11, $Window)
}

# One applied-layouts entry whose device carries the given five fields (valid layout, accepted).
function New-AppliedEntryJson([string]$Monitor, [string]$Instance, [int]$Number, [string]$Serial, [string]$Desktop) {
    '{"device":{"monitor":"' + $Monitor + '","monitor-instance":"' + $Instance + '","monitor-number":' + $Number +
        ',"serial-number":"' + $Serial + '","virtual-desktop":"' + $Desktop +
        '"},"applied-layout":{"uuid":"{00000000-0000-0000-0000-000000000001}","type":"custom","show-spacing":true,"spacing":0,"zone-count":2}}'
}

function New-AppliedDoc([string[]]$EntryJsons) { '{"applied-layouts":[' + ($EntryJsons -join ',') + ']}' }

Describe 'AC-003 Fuzzy matcher truth table' {
    It 'matches the entry-side rows for the MON-14 query with DESKTOP-1' {
        $identity = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL
        $rows = @(
            @( (New-DeviceKey $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL $DESKTOP_1), $true ),
            @( (New-DeviceKey $MON14_ID 'UID9999' $MON14_NUMBER $MON14_SERIAL $DESKTOP_1), $true ),
            @( (New-DeviceKey $MON14_ID 'UID9999' 7 $MON14_SERIAL $DESKTOP_1), $false ),
            @( (New-DeviceKey $MON14_ID $MON14_INSTANCE $MON14_NUMBER '' $DESKTOP_1), $true ),
            @( (New-DeviceKey $MON14_ID $MON14_INSTANCE $MON14_NUMBER 'OTHER-SERIAL' $DESKTOP_1), $false ),
            @( (New-DeviceKey $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL $DESKTOP_2), $false )
        )
        for ($i = 0; $i -lt $rows.Count; $i++) {
            $actual = Test-Match $rows[$i][0] $identity $DESKTOP_1
            ('row {0}: expected={1} actual={2}' -f $i, $rows[$i][1], $actual) |
                Should BeExactly ('row {0}: expected={1} actual={1}' -f $i, $rows[$i][1])
        }
    }

    It 'matches when the query serial is empty and the entry serial is non-empty (either side empty)' {
        $identity = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER ''
        $entry = New-DeviceKey $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL $DESKTOP_1
        Test-Match $entry $identity $DESKTOP_1 | Should Be $true
    }

    It 'matches the legacy empty-instance shape by monitor number' {
        $identity = New-MonitorIdentity '\\.\DISPLAY1' 'X' 3 ''
        $entry = New-DeviceKey '\\.\DISPLAY1' '' 3 '' $DESKTOP_1
        Test-Match $entry $identity $DESKTOP_1 | Should Be $true
    }

    It 'returns the FIRST matching entry in document order when two entries match' {
        $identity = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL
        $first = New-AppliedEntryJson $MON14_ID $MON14_INSTANCE $MON14_NUMBER '' $DESKTOP_1
        $second = New-AppliedEntryJson $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL $DESKTOP_1
        $doc = Read-Applied (New-AppliedDoc @($first, $second))
        $doc.Entries.Length | Should Be 2
        $selected = Select-Entry $doc $identity $DESKTOP_1
        $selected | Should Not BeNullOrEmpty
        $selected.Position | Should Be 0
    }
}

Describe 'AC-004 Desktop GUID precedence' {
    $gB = [guid]'{BBBBBBBB-0000-0000-0000-000000000001}'
    $gC = [guid]'{CCCCCCCC-0000-0000-0000-000000000002}'
    $gD = [guid]'{DDDDDDDD-0000-0000-0000-000000000003}'

    It 'resolves each precedence row: win10, then win11, then window, then null' {
        $rows = @(
            @('{BBBBBBBB-0000-0000-0000-000000000001}', '{CCCCCCCC-0000-0000-0000-000000000002}', '{DDDDDDDD-0000-0000-0000-000000000003}', $gB),
            @($null, '{CCCCCCCC-0000-0000-0000-000000000002}', '{DDDDDDDD-0000-0000-0000-000000000003}', $gC),
            @('not-a-guid', '{CCCCCCCC-0000-0000-0000-000000000002}', '{DDDDDDDD-0000-0000-0000-000000000003}', $gC),
            @($null, $null, '{DDDDDDDD-0000-0000-0000-000000000003}', $gD),
            @($null, $null, $null, $null)
        )
        for ($i = 0; $i -lt $rows.Count; $i++) {
            $resolved = Resolve-Desktop $rows[$i][0] $rows[$i][1] $rows[$i][2]
            if ($null -eq $rows[$i][3]) {
                ('row {0}: null={1}' -f $i, ($null -eq $resolved)) | Should BeExactly ('row {0}: null=True' -f $i)
            }
            else {
                ('row {0}: equal={1}' -f $i, ($resolved -eq $rows[$i][3])) | Should BeExactly ('row {0}: equal=True' -f $i)
            }
        }
    }

    It 'compares GUIDs by value: a lower-case value equals the upper-case GUID' {
        $resolved = Resolve-Desktop $DESKTOP_1.ToLowerInvariant() $null $null
        ($resolved -eq [guid]$DESKTOP_1) | Should Be $true
    }
}

Describe 'AC-005 Single-entry fallback on unknown desktop' {
    It 'returns the single desktop-ignored match from a synthetic single-entry document' {
        $identity = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL
        $doc = Read-Applied (New-AppliedDoc @(New-AppliedEntryJson $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL $DESKTOP_1))
        $selected = Select-Entry $doc $identity $null
        $selected | Should Not BeNullOrEmpty
        $selected.Device.VirtualDesktop | Should BeExactly $DESKTOP_1
    }

    It 'returns no-match on REAL-A where several DELA07B-family entries match ignoring desktop' {
        $identity = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL
        $selected = Select-Entry (Read-Applied $realAppliedJson) $identity $null
        $selected | Should BeNullOrEmpty
    }

    It 'returns no-match when no entry matches ignoring desktop' {
        $identity = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL
        $doc = Read-Applied (New-AppliedDoc @(New-AppliedEntryJson 'OTHER-ID' 'INST' 9 'SER' $DESKTOP_1))
        $selected = Select-Entry $doc $identity $null
        $selected | Should BeNullOrEmpty
    }
}

Describe 'AC-006 Selector hygiene' {
    # 2026-09-26 two-tier fix: entry 4 (document-first instance-exact row on DESKTOP-1) now wins.
    # Rows 4/11/14 all carry the exact MON-14 instance on DESKTOP-1; the old serial veto skipped
    # 4 and 11 (cross-wired old-episode serials) and landed on 14. Instance equality outranks serial.
    It 'REAL-A + MON-14 + DESKTOP-1 selects entry index 4 (0-based), the document-first instance-exact row' {
        $identity = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL
        $selected = Select-Entry (Read-Applied $realAppliedJson) $identity $DESKTOP_1
        $selected | Should Not BeNullOrEmpty
        $selected.Position | Should Be 4
        $selected.Device.Monitor | Should BeExactly $MON14_ID
        $selected.Device.MonitorInstance | Should BeExactly $MON14_INSTANCE
        $selected.Device.MonitorNumber | Should Be 2
        $selected.Device.SerialNumber | Should BeExactly 'YMYH14BB0D6S'
        $selected.Device.VirtualDesktop | Should BeExactly $DESKTOP_1
    }

    It 'a document whose only matching entry is rejected yields no-match (rejected entries are not considered)' {
        $identity = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL
        # A valid device but an applied layout missing zone-count: the parser rejects the entry.
        $rejectedOnlyJson = '{"applied-layouts":[{"device":{"monitor":"' + $MON14_ID + '","monitor-instance":"' + $MON14_INSTANCE +
            '","monitor-number":' + $MON14_NUMBER + ',"serial-number":"' + $MON14_SERIAL + '","virtual-desktop":"' + $DESKTOP_1 +
            '"},"applied-layout":{"uuid":"{00000000-0000-0000-0000-000000000001}","type":"custom","show-spacing":true,"spacing":0}}]}'
        $doc = Read-Applied $rejectedOnlyJson
        $doc.Entries.Length | Should Be 0
        $doc.Rejected.Length | Should Be 1
        $selected = Select-Entry $doc $identity $DESKTOP_1
        $selected | Should BeNullOrEmpty
    }

    It 'does not mutate the document: the Entries array length stays 50 after selection' {
        $identity = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL
        $doc = Read-Applied $realAppliedJson
        $before = $doc.Entries.Length
        $before | Should Be 50
        $null = Select-Entry $doc $identity $DESKTOP_1
        $doc.Entries.Length | Should Be 50
    }
}

# 2026-09-26 live-bug fix (two-tier selection). Live evidence from the user's machine: FancyZones
# cross-wires serials across monitor rows (one serial stamped on both monitors' rows) and replaces
# the live episode's row in place, so the just-applied row can carry a serial that mismatches the
# monitor's real EDID serial while older ghost rows keep the real one. The old serial veto rejected
# the fresh row (or every row on a desktop — NoAppliedLayout) and let a dead ghost win. Tier 1 now
# matches on monitor id + instance + desktop only; serial never vetoes or ranks.
Describe 'Two-tier selection: instance-exact before fuzzy' {
    $LIVE_ID = 'DEL30A9'
    $LIVE_INSTANCE = '5&22cafbe4&0&UID4352'
    $LIVE_DESKTOP = '{F00CF935-0000-4000-8000-000000000000}'
    $OTHER_DESKTOP = '{9089F388-0000-4000-8000-000000000000}'
    $REAL_SERIAL = 'XPG0H42JA001'    # the monitor's true EDID serial
    $CROSSED_SERIAL = 'XPG0H42JB002' # the serial FancyZones stamped on the live episode's rows

    It 'face 1: the document-first instance-exact row beats a later serial-exact ghost row' {
        $identity = New-MonitorIdentity $LIVE_ID $LIVE_INSTANCE 1 $REAL_SERIAL
        $liveRow = New-AppliedEntryJson $LIVE_ID $LIVE_INSTANCE 1 $CROSSED_SERIAL $LIVE_DESKTOP
        $ghostRow = New-AppliedEntryJson $LIVE_ID $LIVE_INSTANCE 1 $REAL_SERIAL $LIVE_DESKTOP
        $doc = Read-Applied (New-AppliedDoc @($liveRow, $ghostRow))
        $selected = Select-Entry $doc $identity $LIVE_DESKTOP
        $selected | Should Not BeNullOrEmpty
        $selected.Position | Should Be 0
        $selected.Device.SerialNumber | Should BeExactly $CROSSED_SERIAL
    }

    It 'face 1 (reverse order): document order alone decides; serial exactness never ranks rows' {
        $identity = New-MonitorIdentity $LIVE_ID $LIVE_INSTANCE 1 $REAL_SERIAL
        $ghostRow = New-AppliedEntryJson $LIVE_ID $LIVE_INSTANCE 1 $REAL_SERIAL $LIVE_DESKTOP
        $liveRow = New-AppliedEntryJson $LIVE_ID $LIVE_INSTANCE 1 $CROSSED_SERIAL $LIVE_DESKTOP
        $doc = Read-Applied (New-AppliedDoc @($ghostRow, $liveRow))
        $selected = Select-Entry $doc $identity $LIVE_DESKTOP
        $selected | Should Not BeNullOrEmpty
        $selected.Position | Should Be 0
        $selected.Device.SerialNumber | Should BeExactly $REAL_SERIAL
    }

    It 'face 2: a desktop whose only row is instance-exact with a mismatched serial still selects it' {
        $identity = New-MonitorIdentity $LIVE_ID $LIVE_INSTANCE 1 $REAL_SERIAL
        $onlyRow = New-AppliedEntryJson $LIVE_ID $LIVE_INSTANCE 1 $CROSSED_SERIAL $OTHER_DESKTOP
        $doc = Read-Applied (New-AppliedDoc @($onlyRow))
        $selected = Select-Entry $doc $identity $OTHER_DESKTOP
        $selected | Should Not BeNullOrEmpty
        $selected.Position | Should Be 0
    }

    It 'tier 1 still requires the desktop GUID: an earlier instance-exact row on another desktop is skipped' {
        $identity = New-MonitorIdentity $LIVE_ID $LIVE_INSTANCE 1 $REAL_SERIAL
        $otherDesktopRow = New-AppliedEntryJson $LIVE_ID $LIVE_INSTANCE 1 $CROSSED_SERIAL $OTHER_DESKTOP
        $currentDesktopRow = New-AppliedEntryJson $LIVE_ID $LIVE_INSTANCE 1 $CROSSED_SERIAL $LIVE_DESKTOP
        $doc = Read-Applied (New-AppliedDoc @($otherDesktopRow, $currentDesktopRow))
        $selected = Select-Entry $doc $identity $LIVE_DESKTOP
        $selected | Should Not BeNullOrEmpty
        $selected.Position | Should Be 1
    }

    It 'tier 2 fallback: with no instance-exact row the legacy number-fallback fuzzy rule still selects' {
        $identity = New-MonitorIdentity $LIVE_ID $LIVE_INSTANCE 1 $REAL_SERIAL
        $numberRow = New-AppliedEntryJson $LIVE_ID 'OTHER-INSTANCE' 1 $REAL_SERIAL $LIVE_DESKTOP
        $doc = Read-Applied (New-AppliedDoc @($numberRow))
        $selected = Select-Entry $doc $identity $LIVE_DESKTOP
        $selected | Should Not BeNullOrEmpty
        $selected.Position | Should Be 0
    }

    It 'tier 1 beats an earlier document-order tier-2-only row' {
        $identity = New-MonitorIdentity $LIVE_ID $LIVE_INSTANCE 1 $REAL_SERIAL
        $fuzzyRow = New-AppliedEntryJson $LIVE_ID 'OTHER-INSTANCE' 1 $REAL_SERIAL $LIVE_DESKTOP
        $exactRow = New-AppliedEntryJson $LIVE_ID $LIVE_INSTANCE 1 $CROSSED_SERIAL $LIVE_DESKTOP
        $doc = Read-Applied (New-AppliedDoc @($fuzzyRow, $exactRow))
        $selected = Select-Entry $doc $identity $LIVE_DESKTOP
        $selected | Should Not BeNullOrEmpty
        $selected.Position | Should Be 1
    }

    It 'unknown desktop: a single instance-exact row with a mismatched serial is the match' {
        $identity = New-MonitorIdentity $LIVE_ID $LIVE_INSTANCE 1 $REAL_SERIAL
        $onlyRow = New-AppliedEntryJson $LIVE_ID $LIVE_INSTANCE 1 $CROSSED_SERIAL $LIVE_DESKTOP
        $doc = Read-Applied (New-AppliedDoc @($onlyRow))
        $selected = Select-Entry $doc $identity $null
        $selected | Should Not BeNullOrEmpty
        $selected.Position | Should Be 0
    }

    It 'unknown desktop: several instance-exact rows are still ambiguous and yield no match' {
        $identity = New-MonitorIdentity $LIVE_ID $LIVE_INSTANCE 1 $REAL_SERIAL
        $rowA = New-AppliedEntryJson $LIVE_ID $LIVE_INSTANCE 1 $REAL_SERIAL $LIVE_DESKTOP
        $rowB = New-AppliedEntryJson $LIVE_ID $LIVE_INSTANCE 1 $REAL_SERIAL $OTHER_DESKTOP
        $doc = Read-Applied (New-AppliedDoc @($rowA, $rowB))
        $selected = Select-Entry $doc $identity $null
        $selected | Should BeNullOrEmpty
    }
}

function New-WorkArea([int]$Left, [int]$Top, [int]$Width, [int]$Height, [int]$Dpi) {
    New-Object TerminalOrganizer.Core.Geometry.WorkArea -ArgumentList $Left, $Top, $Width, $Height, $Dpi
}

function New-MonitorInfo([string]$Path, [string]$Id, [string]$Instance, [string]$Serial, [int]$Number, $WorkArea) {
    New-Object TerminalOrganizer.Core.Monitors.MonitorInfo -ArgumentList $Path, $Id, $Instance, $Serial, $Number, 0, 0, 1920, 1200, $WorkArea
}

function Build-Context($Entry, $Monitor) {
    [TerminalOrganizer.Core.Monitors.OrganizerContext]::Build($Entry, $Monitor)
}

function Resolve-Key([string]$AppliedJson, [string]$CustomJson, $Key, $WorkArea) {
    [TerminalOrganizer.Core.Layouts.LayoutResolver]::ResolveByDeviceKey($AppliedJson, $CustomJson, $Key, $WorkArea)
}

function Get-Zones($Result) { ($Result.Zones | ForEach-Object { $_.ToString() }) -join ' ' }
function Get-Warnings($Result) { ($Result.Warnings | ForEach-Object { $_.ToString() }) -join ',' }

function Get-InnerException($ErrorRecord) {
    $e = $ErrorRecord.Exception
    while ($e.InnerException -ne $null) { $e = $e.InnerException }
    $e
}

Describe 'AC-007 Composition into resolution' {
    # Entry 4 (two-tier fix) is a grid template, 3 zones, show-spacing, spacing 16; the zone
    # expectations are the SPEC-LAYOUT-002 grid n=3 pins (Templates.Tests.ps1 AC-004) on the
    # same work area, not values re-derived from this selector change.
    It 'the composed key resolves Supported, grid, 3 zones, effective spacing 16, no warnings, z0 615x1120@16,16, z1 624x1120@647,16 and z2 617x1120@1287,16' {
        $identity = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL
        $wa = New-WorkArea 0 0 1920 1152 96
        $monitor = New-MonitorInfo $MON14_PATH $MON14_ID $MON14_INSTANCE $MON14_SERIAL $MON14_NUMBER $wa
        $selected = Select-Entry (Read-Applied $realAppliedJson) $identity $DESKTOP_1
        $context = Build-Context $selected $monitor
        $r = Resolve-Key $realAppliedJson $realCustomJson $context.Key $wa
        $r.Kind.ToString() | Should BeExactly 'Supported'
        $r.LayoutType | Should BeExactly 'grid'
        $r.LayoutName | Should BeNullOrEmpty
        $r.EffectiveSpacing | Should Be 16
        Get-Warnings $r | Should BeExactly ''
        Get-Zones $r | Should BeExactly 'z0:615x1120@16,16 z1:624x1120@647,16 z2:617x1120@1287,16'
    }

    It 'equals the resolution with a hand-built exact DeviceKey carrying entry 4 five fields (direct construction, not the selector)' {
        $identity = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL
        $wa = New-WorkArea 0 0 1920 1152 96
        $monitor = New-MonitorInfo $MON14_PATH $MON14_ID $MON14_INSTANCE $MON14_SERIAL $MON14_NUMBER $wa
        $selected = Select-Entry (Read-Applied $realAppliedJson) $identity $DESKTOP_1
        $context = Build-Context $selected $monitor
        $handKey = New-DeviceKey $MON14_ID $MON14_INSTANCE 2 'YMYH14BB0D6S' $DESKTOP_1
        $r1 = Resolve-Key $realAppliedJson $realCustomJson $context.Key $wa
        $r2 = Resolve-Key $realAppliedJson $realCustomJson $handKey $wa
        ('{0}|{1}|{2}|{3}|{4}' -f $r1.Kind, $r1.LayoutType, $r1.LayoutName, $r1.EffectiveSpacing, (Get-Zones $r1)) |
            Should BeExactly ('{0}|{1}|{2}|{3}|{4}' -f $r2.Kind, $r2.LayoutType, $r2.LayoutName, $r2.EffectiveSpacing, (Get-Zones $r2))
    }

    It 'the context carries the entry device fields and the monitor work area, without copying the entry' {
        $identity = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL
        $wa = New-WorkArea 0 0 1920 1152 96
        $monitor = New-MonitorInfo $MON14_PATH $MON14_ID $MON14_INSTANCE $MON14_SERIAL $MON14_NUMBER $wa
        $selected = Select-Entry (Read-Applied $realAppliedJson) $identity $DESKTOP_1
        $context = Build-Context $selected $monitor
        $context.Key.Monitor | Should BeExactly $MON14_ID
        $context.Key.MonitorInstance | Should BeExactly $MON14_INSTANCE
        $context.Key.MonitorNumber | Should Be 2
        $context.Key.SerialNumber | Should BeExactly 'YMYH14BB0D6S'
        $context.Key.VirtualDesktop | Should BeExactly $DESKTOP_1
        ([object]::ReferenceEquals($context.Key, $selected.Device)) | Should Be $true
        $context.WorkArea.Width | Should Be 1920
        $context.WorkArea.Dpi | Should Be 96
    }

    It 'a null monitor argument throws ArgumentNullException' {
        $identity = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL
        $selected = Select-Entry (Read-Applied $realAppliedJson) $identity $DESKTOP_1
        $caught = $null
        try { Build-Context $selected $null } catch { $caught = $_ }
        $caught | Should Not BeNullOrEmpty
        (Get-InnerException $caught).GetType().FullName | Should BeExactly 'System.ArgumentNullException'
    }
}

Describe 'AC-008 Wrapper surface and degradation' {
    It 'an EDID registry read of a non-existent key yields no bytes and an empty serial, without throwing' {
        $bytes = [TerminalOrganizer.Core.Monitors.Win32MonitorProvider]::ReadEdidFromRegistry('NO_SUCH_ID', 'NO_SUCH_INSTANCE')
        $bytes | Should BeNullOrEmpty
        Parse-Serial $bytes | Should BeExactly ''
    }

    It 'the VirtualDesktopReader registry miss returns Absent rather than throwing' {
        $reader = New-Object TerminalOrganizer.Core.Monitors.VirtualDesktopReader
        $result = $reader.TryReadWin10SessionValue()
        $result | Should Not BeNullOrEmpty
        $result.Status.ToString() | Should BeExactly 'Absent'
    }

    It 'the wrappers construct with no ambient configuration and sit behind IMonitorProvider/IDesktopSource' {
        $provider = New-Object TerminalOrganizer.Core.Monitors.Win32MonitorProvider
        ($provider -is [TerminalOrganizer.Core.Monitors.IMonitorProvider]) | Should Be $true
        $reader = New-Object TerminalOrganizer.Core.Monitors.VirtualDesktopReader
        ($reader -is [TerminalOrganizer.Core.Monitors.IDesktopSource]) | Should Be $true
    }

    It 'a fake is not required: a hand-built MonitorInfo feeds the selector through ToIdentity' {
        $wa = New-WorkArea 0 0 1920 1152 96
        $monitor = New-MonitorInfo $MON14_PATH $MON14_ID $MON14_INSTANCE $MON14_SERIAL $MON14_NUMBER $wa
        $selected = Select-Entry (Read-Applied $realAppliedJson) ($monitor.ToIdentity()) $DESKTOP_1
        $selected | Should Not BeNullOrEmpty
        $selected.Position | Should Be 4
    }
}

Describe 'AC-009 Dump tool self-test' {
    It 'tools/dump-monitors.ps1 -SelfTest exits 0 with one PASS/FAIL line per check and zero FAILs' {
        $ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $dump = Join-Path $repoRoot 'tools\dump-monitors.ps1'
        $output = & $ps51 -NoProfile -ExecutionPolicy Bypass -File $dump -SelfTest
        $code = $LASTEXITCODE
        $lines = @($output | Where-Object { $_ -cmatch '^(PASS|FAIL):' })
        $lines.Count | Should BeGreaterThan 0
        @($lines | Where-Object { $_ -cmatch '^FAIL:' }).Count | Should Be 0
        $code | Should Be 0
    }
}

function New-A4Monitor([int]$Number = 1, [int]$WorkTop = 0, [int]$Dpi = 96, [string]$Serial = 'SER', [int]$Left = 0) {
    $wa = [TerminalOrganizer.Core.Geometry.WorkArea]::new($Left, $WorkTop, 1920, 1040, $Dpi)
    [TerminalOrganizer.Core.Monitors.MonitorInfo]::new(('DISPLAY' + $Serial), ('PATH' + $Serial), 'MODEL', 'INSTANCE', $Serial, $Number, $true, $Left, 0, 1920, 1080, $wa)
}
Describe 'A4 monitor identity and exact signatures' {
    It 'stable keys ignore numbering and resolve menu payloads after renumbering' {
        $before = New-A4Monitor 2
        $after = New-A4Monitor 1
        $before.StableKey.EqualsKey($after.StableKey) | Should Be $true
        [TerminalOrganizer.Core.Monitors.MonitorSelector]::ResolveUnique(@($after), $before.StableKey).Number | Should Be 1
        [TerminalOrganizer.Core.Monitors.MonitorSelector]::ResolveUnique(@($after, $after), $before.StableKey) | Should BeNullOrEmpty
    }
    It 'topology changes on work area DPI identity or full rectangle, but ignores order and numbering' {
        $original = [TerminalOrganizer.Core.Monitors.MonitorTopologySignature]::Capture(@((New-A4Monitor)))
        foreach ($changed in @((New-A4Monitor -WorkTop 1), (New-A4Monitor -Dpi 120), (New-A4Monitor -Serial 'OTHER'), (New-A4Monitor -Left 1920))) {
            $original.EqualsSignature([TerminalOrganizer.Core.Monitors.MonitorTopologySignature]::Capture(@($changed))) | Should Be $false
        }
        $original.EqualsSignature([TerminalOrganizer.Core.Monitors.MonitorTopologySignature]::Capture(@((New-A4Monitor 2)))) | Should Be $true
        $left = New-A4Monitor
        $right = New-A4Monitor -Serial 'OTHER' -Left 1920
        ([TerminalOrganizer.Core.Monitors.MonitorTopologySignature]::Capture(@($left, $right))).EqualsSignature([TerminalOrganizer.Core.Monitors.MonitorTopologySignature]::Capture(@($right, $left))) | Should Be $true
    }
    It 'one-pixel layout edit invalidates commit' {
        $kind = [TerminalOrganizer.Core.Layouts.LayoutResultKind]::Supported
        $a = [TerminalOrganizer.Core.Monitors.LayoutSignature]::new('key', 'desktop', $kind, 'grid', @([TerminalOrganizer.Core.Geometry.Zone]::new(0,0,0,400,300)))
        $b = [TerminalOrganizer.Core.Monitors.LayoutSignature]::new('key', 'desktop', $kind, 'grid', @([TerminalOrganizer.Core.Geometry.Zone]::new(0,1,0,400,300)))
        $a.EqualsSignature($b) | Should Be $false
    }
    It 'HMONITOR device attribution picks right even when outer left is 1912 and unknown devices fail closed' {
        $left = New-A4Monitor
        $right = New-A4Monitor -Serial 'RIGHT' -Left 1920
        $resolved = [TerminalOrganizer.Core.Windows.Win32WindowEnumerator]::ResolveMonitorByDeviceName('DISPLAYRIGHT', @($left, $right))
        $resolved.MonitorLeft | Should Be 1920
        [TerminalOrganizer.Core.Windows.Win32WindowEnumerator]::ResolveMonitorByDeviceName('missing', @($left, $right)) | Should BeNullOrEmpty
    }
}

Describe 'AC-010 Dump tool live mode exists' {
    It 'with -AppliedLayouts it enumerates real monitors, prints the per-monitor report and exits 0 (informational, not a live-correctness claim)' {
        $ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $dump = Join-Path $repoRoot 'tools\dump-monitors.ps1'
        $output = & $ps51 -NoProfile -ExecutionPolicy Bypass -File $dump -AppliedLayouts (Join-Path $fixtures 'applied-layouts.json')
        $code = $LASTEXITCODE
        $code | Should Be 0
        (@($output).Length) | Should BeGreaterThan 0
    }
}
