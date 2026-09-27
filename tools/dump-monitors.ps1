# SPEC-MON-003 dump tool (REQ-CMP-002).
# -SelfTest : exercises the pure components (path parse, EDID parse, matcher truth table,
#             desktop precedence) against the pinned values; prints one PASS/FAIL line per
#             check and exits 0 only when every check passes.
# No switch  : enumerates the real monitors and prints, per monitor: parsed identity, serial,
#             work area, DPI, the resolved desktop GUID source, and (with -AppliedLayouts
#             <path>) the fuzzy-match result. Live output is informational (morning
#             checklist), NOT an acceptance claim about live correctness.
param(
    [switch]$SelfTest,
    [string]$AppliedLayouts
)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here
$dllPath = Join-Path $repoRoot 'bin\TerminalOrganizer.Core.dll'

if (-not (Test-Path $dllPath)) {
    Write-Output ('FAIL: build output missing: ' + $dllPath + ' (run build.ps1 first)')
    exit 1
}
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dllPath))

# Pinned values (acceptance.md Conventions; plan.md J.1/J.2) — never re-derived from code under test.
$MON14_PATH = '\\?\DISPLAY#DELA07B#5&1b98c55a&0&UID4354#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}'
$MON14_ID = 'DELA07B'
$MON14_INSTANCE = '5&1b98c55a&0&UID4354'
$MON14_NUMBER = 3
$MON14_SERIAL = 'YMYH14CO3VKS'
$DESKTOP_1 = '{A4E51EC3-B149-4070-A47D-1902CA048C5B}'
$DESKTOP_2 = '{E66ED430-0000-0000-0000-000000000000}'

$script:FailCount = 0
$script:CheckCount = 0

function Assert-Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $script:CheckCount++
    if ($Ok) {
        Write-Output ('PASS: ' + $Name)
    }
    else {
        $script:FailCount++
        Write-Output ('FAIL: ' + $Name + ' — expected ' + $Detail)
    }
}

function New-MonitorIdentity([string]$MonitorId, [string]$Instance, [int]$Number, [string]$Serial) {
    New-Object TerminalOrganizer.Core.Monitors.MonitorIdentity -ArgumentList $MonitorId, $Instance, $Number, $Serial
}

function New-DeviceKey([string]$Monitor, [string]$Instance, [int]$Number, [string]$Serial, [string]$Desktop) {
    New-Object TerminalOrganizer.Core.Layouts.DeviceKey -ArgumentList $Monitor, $Instance, $Number, $Serial, $Desktop
}

# Builds a plan.md J.2 synthetic EDID: descriptor at offset 54, FF 00 00 00, then the text bytes.
function New-SyntheticEdid([byte[]]$TextBytes) {
    $edid = New-Object byte[] 128
    $edid[54] = 0xFF
    $edid[55] = 0x00
    $edid[56] = 0x00
    $edid[57] = 0x00
    for ($i = 0; $i -lt $TextBytes.Length; $i++) { $edid[58 + $i] = $TextBytes[$i] }
    ,$edid
}

if ($SelfTest) {
    # --- Path parsing (AC-001) ---
    $parts = [TerminalOrganizer.Core.Monitors.InterfacePathParser]::Parse($MON14_PATH)
    Assert-Check 'path/monitor-id' ($parts.MonitorId -ceq $MON14_ID) ($MON14_ID + ' got [' + $parts.MonitorId + ']')
    Assert-Check 'path/instance' ($parts.Instance -ceq $MON14_INSTANCE) ($MON14_INSTANCE + ' got [' + $parts.Instance + ']')

    $badAllEmpty = $true
    foreach ($bad in @('garbage', '\\?\DISPLAY#ONLYONE', '', $null)) {
        $p = [TerminalOrganizer.Core.Monitors.InterfacePathParser]::Parse($bad)
        if ($p.MonitorId -ne '' -or $p.Instance -ne '') { $badAllEmpty = $false }
    }
    Assert-Check 'path/unparseable-empty' $badAllEmpty 'both components empty for every unparseable input'

    $lower = [TerminalOrganizer.Core.Monitors.InterfacePathParser]::Parse('\\?\DISPLAY#dela07b#5&1b98c55a&0&UID4354#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}')
    Assert-Check 'path/ordinal' ($lower.MonitorId -ceq 'dela07b' -and $lower.MonitorId -cne 'DELA07B') 'lower-case id stays lower-case'

    # --- EDID parsing (AC-002) ---
    $textA = [System.Text.Encoding]::ASCII.GetBytes('YMYH14CO3VKS')
    $bytesA = New-Object byte[] ($textA.Length + 2)
    [Array]::Copy($textA, 0, $bytesA, 0, $textA.Length)
    $bytesA[$textA.Length] = 0x0A
    $bytesA[$textA.Length + 1] = 0x20
    $serialA = [TerminalOrganizer.Core.Monitors.EdidParser]::ParseSerial((New-SyntheticEdid $bytesA))
    Assert-Check 'edid/case-A' ($serialA -ceq 'YMYH14CO3VKS') ('YMYH14CO3VKS got [' + $serialA + ']')

    $textB = [System.Text.Encoding]::ASCII.GetBytes(' SN-001')
    $bytesB = New-Object byte[] ($textB.Length + 1)
    [Array]::Copy($textB, 0, $bytesB, 0, $textB.Length)
    $bytesB[$textB.Length] = 0x0A
    $serialB = [TerminalOrganizer.Core.Monitors.EdidParser]::ParseSerial((New-SyntheticEdid $bytesB))
    Assert-Check 'edid/case-B' ($serialB -ceq 'SN-001') ('SN-001 got [' + $serialB + ']')

    $serialC = [TerminalOrganizer.Core.Monitors.EdidParser]::ParseSerial((New-Object byte[] 128))
    Assert-Check 'edid/case-C' ($serialC -ceq '') ('empty string got [' + $serialC + ']')

    $serialD = [TerminalOrganizer.Core.Monitors.EdidParser]::ParseSerial((New-Object byte[] 10))
    Assert-Check 'edid/case-D' ($serialD -ceq '') ('empty string got [' + $serialD + ']')

    # Real-hardware monitor-descriptor form (live probe 2026-09-25: 00 00 00 FF header, tag at
    # byte 3, NUL pad at byte 4, text from byte 5), same pinned serial value — proves the
    # registry EDID shape parses too.
    $edidReal = New-Object byte[] 128
    $edidReal[54] = 0x00
    $edidReal[55] = 0x00
    $edidReal[56] = 0x00
    $edidReal[57] = 0xFF
    $edidReal[58] = 0x00
    $textReal = [System.Text.Encoding]::ASCII.GetBytes('YMYH14CO3VKS')
    [Array]::Copy($textReal, 0, $edidReal, 59, $textReal.Length)
    $edidReal[71] = 0x0A
    $serialReal = [TerminalOrganizer.Core.Monitors.EdidParser]::ParseSerial($edidReal)
    Assert-Check 'edid/real-hardware-form' ($serialReal -ceq 'YMYH14CO3VKS') ('YMYH14CO3VKS got [' + $serialReal + ']')

    # --- Matcher truth table (AC-003) ---
    $identity = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL
    $rows = @(
        @( (New-DeviceKey $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL $DESKTOP_1), $true,  'exact-MON14' ),
        @( (New-DeviceKey $MON14_ID 'UID9999' $MON14_NUMBER $MON14_SERIAL $DESKTOP_1),      $true,  'number-fallback' ),
        @( (New-DeviceKey $MON14_ID 'UID9999' 7 $MON14_SERIAL $DESKTOP_1),                   $false, 'instance-and-number-changed' ),
        @( (New-DeviceKey $MON14_ID $MON14_INSTANCE $MON14_NUMBER '' $DESKTOP_1),            $true,  'entry-serial-empty' ),
        @( (New-DeviceKey $MON14_ID $MON14_INSTANCE $MON14_NUMBER 'OTHER-SERIAL' $DESKTOP_1), $false, 'serial-both-nonempty-differ' ),
        @( (New-DeviceKey $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL $DESKTOP_2), $false, 'desktop-differs' )
    )
    foreach ($row in $rows) {
        $actual = [TerminalOrganizer.Core.Monitors.AppliedEntrySelector]::Match($row[0], $identity, $DESKTOP_1)
        Assert-Check ('matcher/' + $row[2]) ($actual -eq $row[1]) ($row[1].ToString() + ' got ' + $actual.ToString())
    }

    $identityEmptySerial = New-MonitorIdentity $MON14_ID $MON14_INSTANCE $MON14_NUMBER ''
    $entryFullSerial = New-DeviceKey $MON14_ID $MON14_INSTANCE $MON14_NUMBER $MON14_SERIAL $DESKTOP_1
    $actualQ = [TerminalOrganizer.Core.Monitors.AppliedEntrySelector]::Match($entryFullSerial, $identityEmptySerial, $DESKTOP_1)
    Assert-Check 'matcher/query-serial-empty' ($actualQ -eq $true) 'True got ' + $actualQ.ToString()

    $identityLegacy = New-MonitorIdentity '\\.\DISPLAY1' 'X' 3 ''
    $entryLegacy = New-DeviceKey '\\.\DISPLAY1' '' 3 '' $DESKTOP_1
    $actualL = [TerminalOrganizer.Core.Monitors.AppliedEntrySelector]::Match($entryLegacy, $identityLegacy, $DESKTOP_1)
    Assert-Check 'matcher/legacy-empty-instance' ($actualL -eq $true) 'True got ' + $actualL.ToString()

    $firstEntry = '{"device":{"monitor":"' + $MON14_ID + '","monitor-instance":"' + $MON14_INSTANCE + '","monitor-number":' + $MON14_NUMBER +
        ',"serial-number":"","virtual-desktop":"' + $DESKTOP_1 +
        '"},"applied-layout":{"uuid":"{00000000-0000-0000-0000-000000000001}","type":"custom","show-spacing":true,"spacing":0,"zone-count":2}}'
    $secondEntry = '{"device":{"monitor":"' + $MON14_ID + '","monitor-instance":"' + $MON14_INSTANCE + '","monitor-number":' + $MON14_NUMBER +
        ',"serial-number":"' + $MON14_SERIAL + '","virtual-desktop":"' + $DESKTOP_1 +
        '"},"applied-layout":{"uuid":"{00000000-0000-0000-0000-000000000001}","type":"custom","show-spacing":true,"spacing":0,"zone-count":2}}'
    $twoDoc = [TerminalOrganizer.Core.Layouts.FancyZonesJsonReader]::ParseAppliedLayouts(('{"applied-layouts":[' + $firstEntry + ',' + $secondEntry + ']}'))
    $firstSelected = [TerminalOrganizer.Core.Monitors.AppliedEntrySelector]::Select($twoDoc, $identity, $DESKTOP_1)
    Assert-Check 'matcher/first-wins' ($firstSelected -ne $null -and $firstSelected.Position -eq 0) 'position 0 (first in document order)'

    # --- Desktop precedence (AC-004) ---
    $gB = [guid]'{BBBBBBBB-0000-0000-0000-000000000001}'
    $gC = [guid]'{CCCCCCCC-0000-0000-0000-000000000002}'
    $gD = [guid]'{DDDDDDDD-0000-0000-0000-000000000003}'
    $precRows = @(
        @('{BBBBBBBB-0000-0000-0000-000000000001}', '{CCCCCCCC-0000-0000-0000-000000000002}', '{DDDDDDDD-0000-0000-0000-000000000003}', $gB, 'win10-wins'),
        @($null, '{CCCCCCCC-0000-0000-0000-000000000002}', '{DDDDDDDD-0000-0000-0000-000000000003}', $gC, 'win11-after-null'),
        @('not-a-guid', '{CCCCCCCC-0000-0000-0000-000000000002}', '{DDDDDDDD-0000-0000-0000-000000000003}', $gC, 'win11-after-bad-guid'),
        @($null, $null, '{DDDDDDDD-0000-0000-0000-000000000003}', $gD, 'window-fallback'),
        @($null, $null, $null, $null, 'all-null')
    )
    foreach ($row in $precRows) {
        $resolved = [TerminalOrganizer.Core.Monitors.DesktopIdResolver]::Resolve($row[0], $row[1], $row[2])
        if ($null -eq $row[3]) {
            Assert-Check ('precedence/' + $row[4]) ($null -eq $resolved) 'null got ' + $(if ($null -eq $resolved) { 'null' } else { $resolved.ToString() })
        }
        else {
            Assert-Check ('precedence/' + $row[4]) ($resolved -eq $row[3]) ($row[3].ToString() + ' got ' + $(if ($null -eq $resolved) { 'null' } else { $resolved.ToString() }))
        }
    }

    $resolvedLower = [TerminalOrganizer.Core.Monitors.DesktopIdResolver]::Resolve($DESKTOP_1.ToLowerInvariant(), $null, $null)
    Assert-Check 'precedence/guid-by-value' ($resolvedLower -eq [guid]$DESKTOP_1) 'value-equal GUID for lower-case text'

    Write-Output ('SelfTest summary: ' + $script:CheckCount + ' checks, ' + $script:FailCount + ' failed')
    if ($script:FailCount -gt 0) { exit 1 }
    exit 0
}

# --- Live mode (morning checklist; informational only) ---
# Exit codes (B5): 0 success or valid no-monitors; 1 invalid args / missing required
# file; 2 acquisition exception/failure. Every catch writes ERROR to stderr.
function Fail-Mon([string]$Message, [int]$Code) {
    [Console]::Error.WriteLine('ERROR: ' + $Message)
    exit $Code
}

if (-not [string]::IsNullOrEmpty($AppliedLayouts) -and -not (Test-Path -LiteralPath $AppliedLayouts)) {
    Fail-Mon ('required applied-layouts file not found: ' + $AppliedLayouts) 1
}

try {
    $provider = New-Object TerminalOrganizer.Core.Monitors.Win32MonitorProvider
    $reader = New-Object TerminalOrganizer.Core.Monitors.VirtualDesktopReader

    $desktopText = $null
    $desktopSource = 'none (registry steps absent; step 3 needs a known window — tray app / SPEC-WIN-004)'
    $win10 = $reader.TryReadWin10SessionValue()
    if ($win10.Status.ToString() -eq 'Present') {
        $desktopText = $win10.Value
        $desktopSource = 'win10-session-registry'
    }
    else {
        $win11 = $reader.TryReadWin11Value()
        if ($win11.Status.ToString() -eq 'Present') {
            $desktopText = $win11.Value
            $desktopSource = 'win11-registry'
        }
        else {
            Write-Output ('note: win10-session read = ' + $win10.Status.ToString() + ', win11 read = ' + $win11.Status.ToString())
        }
    }

    $appliedDoc = $null
    if (-not [string]::IsNullOrEmpty($AppliedLayouts)) {
        $resolvedPath = (Resolve-Path -LiteralPath $AppliedLayouts).ProviderPath
        $appliedDoc = [TerminalOrganizer.Core.Layouts.FancyZonesJsonReader]::ParseAppliedLayouts([IO.File]::ReadAllText($resolvedPath))
        if (-not $appliedDoc.IsValid) {
            Write-Output ('WARN: applied-layouts document invalid: ' + $appliedDoc.Detail)
            $appliedDoc = $null
        }
    }

    $monitors = $provider.GetMonitors()
    Write-Output ('live dump: ' + $monitors.Length + ' monitor(s); desktop source: ' + $desktopSource +
        '; desktop: ' + $(if ($null -ne $desktopText) { $desktopText } else { '<null>' }))
    foreach ($m in $monitors) {
        Write-Output ('monitor #' + $m.Number + ': id=' + $m.MonitorId + ' instance=[' + $m.Instance + '] serial=[' + $m.Serial + ']')
        Write-Output ('  monitor-rect=' + $m.MonitorLeft + ',' + $m.MonitorTop + ' ' + $m.MonitorWidth + 'x' + $m.MonitorHeight +
            ' work-area=' + $m.WorkArea.Left + ',' + $m.WorkArea.Top + ' ' + $m.WorkArea.Width + 'x' + $m.WorkArea.Height + ' dpi=' + $m.WorkArea.Dpi)
        Write-Output ('  interface-path=' + $m.InterfacePath)
        if ($null -ne $appliedDoc) {
            $selected = [TerminalOrganizer.Core.Monitors.AppliedEntrySelector]::Select($appliedDoc, $m.ToIdentity(), $desktopText)
            if ($null -eq $selected) {
                Write-Output '  fuzzy-match: no-match'
            }
            else {
                Write-Output ('  fuzzy-match: entry ' + $selected.Position + ' type=' + $selected.Type + ' uuid=' + $selected.Uuid)
            }
        }
    }
    if ($monitors.Length -eq 0) {
        Write-Output 'live dump: no monitors reported (valid empty result; acquisition succeeded)'
    }
}
catch {
    Fail-Mon ('acquisition failed: ' + $_.Exception.Message) 2
}
Write-Output 'live dump is informational (morning checklist); it is NOT an acceptance claim'
exit 0
