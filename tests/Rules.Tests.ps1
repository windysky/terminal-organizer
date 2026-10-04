# SPEC-RULES-008 acceptance suite (Pester 3.4.0, Windows PowerShell 5.1).
# Run through test.ps1, which builds first and starts a fresh powershell.exe.
# Expected values come from acceptance.md and plan.md section J, never from the code under test.
# Pure ASCII: non-ASCII fixtures are built from [char] codes.

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here
$dllPath = Join-Path $repoRoot 'bin\TerminalOrganizer.Core.dll'

# Byte-load keeps the DLL file unlocked so build.ps1 can rebuild while this session runs.
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dllPath))

$EM_DASH = [string][char]0x2014
$STAR = [string][char]0x2733

# Pinned command lines (plan.md J.3).
$CMD_YODA9 = 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA9'
$CMD_LOCAL = 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA1 20'
$CMD_REMOTE = 'wsl.exe -d Ubuntu --exec /tmp/rdl_attach.sh user@host YODA2'
$CMD_NATIVE = 'powershell.exe -NoProfile -Command "& { $env:LAUNCHER_SESSION=''OPS1''; Get-Date }"'

$RULE3_REF = 'rule 3: regex:^(?<rank>[0-9]{1,4})-(?<name>.+)$'

# Rule list U (plan.md J.3), positions as written. A fresh array per call.
function New-RuleListU {
    [object[]]@(
        @{ match = 'PowerShell*'; rank = 200 },
        @{ match = 'regex:^ssh (?<name>[^ ]+)'; rank = 400 },
        @{ match = 'regex:^(?<rank>[0-9]{1,4})-(?<name>.+)$' },
        @{ marker = '#' },
        @{ match = '*--attach YODA*'; on = 'commandline'; rank = 250 }
    )
}

function Build-Rules($Definitions, [bool]$Preset = $false) {
    [TerminalOrganizer.Core.Rules.RuleSetBuilder]::Build($Definitions, $Preset)
}

function Eval-Tab($RuleSet, $Title, $CommandLine = $null) {
    [TerminalOrganizer.Core.Rules.RuleEvaluator]::EvaluateTab($RuleSet, $Title, $CommandLine)
}

Describe 'AC-001 Glob semantics' {
    It 'rule 1 of U matches powershell and PowerShell 7.4 with rank 200, and not Windows PowerShell' {
        $u = Build-Rules (New-RuleListU)
        foreach ($title in @('powershell', 'PowerShell 7.4')) {
            $r = Eval-Tab $u $title
            $r.Matched | Should Be $true
            $r.Rank | Should Be 200
            $r.Reference | Should BeExactly 'rule 1: PowerShell*'
        }
        $miss = Eval-Tab $u 'Windows PowerShell'
        $miss.Matched | Should Be $false
        $miss.Rank | Should BeNullOrEmpty
    }

    It 'glob a?c matches abc and ABC but not ac or abbc' {
        $rs = Build-Rules ([object[]]@(@{ match = 'a?c' }))
        (Eval-Tab $rs 'abc').Matched | Should Be $true
        (Eval-Tab $rs 'ABC').Matched | Should Be $true
        (Eval-Tab $rs 'ac').Matched | Should Be $false
        (Eval-Tab $rs 'abbc').Matched | Should Be $false
    }

    It 'a glob containing [x] matches the literal text [x] only' {
        $rs = Build-Rules ([object[]]@(@{ match = '[x]' }))
        (Eval-Tab $rs '[x]').Matched | Should Be $true
        (Eval-Tab $rs 'x').Matched | Should Be $false
    }

    It 'under the tr-TR culture the glob *file* still matches FILE' {
        $thread = [Threading.Thread]::CurrentThread
        $saved = $thread.CurrentCulture
        try {
            $thread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('tr-TR')
            $rs = Build-Rules ([object[]]@(@{ match = '*file*' }))
            (Eval-Tab $rs 'FILE').Matched | Should Be $true
        }
        finally {
            $thread.CurrentCulture = $saved
        }
    }
}

Describe 'AC-002 First match wins, preset first, name and rank sourcing' {
    It 'rule 1 decides for PowerShell: identity PowerShell and no rule rank' {
        $rs = Build-Rules ([object[]]@(@{ match = '*' }, @{ match = 'PowerShell*'; rank = 200 }))
        $r = Eval-Tab $rs 'PowerShell'
        $r.Name | Should BeExactly 'PowerShell'
        $r.Rank | Should BeNullOrEmpty
        $r.Reference | Should BeExactly 'rule 1: *'
    }

    It 'a fixed name and rank apply when no capture supplies them' {
        $rs = Build-Rules ([object[]]@(@{ match = '*vim*'; name = 'editor'; rank = 150 }))
        $r = Eval-Tab $rs 'nvim ~/x'
        $r.Name | Should BeExactly 'editor'
        $r.Rank | Should Be 150
    }

    It 'a name capture beats the fixed name' {
        $rs = Build-Rules ([object[]]@(@{ match = 'regex:^ssh (?<name>[^ ]+)'; name = 'remote'; rank = 400 }))
        $r = Eval-Tab $rs 'ssh db1'
        $r.Name | Should BeExactly 'db1'
        $r.Rank | Should Be 400
    }

    It 'a blank name capture counts as absent so the trimmed title is used' {
        $rs = Build-Rules ([object[]]@(@{ match = 'regex:^x(?<name> *)$' }))
        $r = Eval-Tab $rs 'x  '
        $r.Matched | Should Be $true
        $r.Name | Should BeExactly 'x'
    }

    It 'with the preset off, the same title gets identity x and rank 1 from user rule 1' {
        $rs = Build-Rules ([object[]]@(@{ match = 'OC_*'; name = 'x'; rank = 1 })) $false
        $r = Eval-Tab $rs 'OC_YODA1'
        $r.Name | Should BeExactly 'x'
        $r.Rank | Should Be 1
        $r.Reference | Should BeExactly 'rule 1: OC_*'
    }
}

Describe 'AC-003 Regex captures' {
    It 'ssh server1 gets identity server1, rank 400 and the rule 2 reference' {
        $r = Eval-Tab (Build-Rules (New-RuleListU)) 'ssh server1'
        $r.Name | Should BeExactly 'server1'
        $r.Rank | Should Be 400
        $r.Reference | Should BeExactly 'rule 2: regex:^ssh (?<name>[^ ]+)'
    }

    It 'SSH server1 matches no rule because regex is case-sensitive by default' {
        $r = Eval-Tab (Build-Rules (New-RuleListU)) 'SSH server1'
        $r.Matched | Should Be $false
        $r.Name | Should BeExactly 'SSH server1'
        $r.Rank | Should BeNullOrEmpty
    }

    It '2-build gets identity build and rank 2 from the rank capture' {
        $r = Eval-Tab (Build-Rules (New-RuleListU)) '2-build'
        $r.Name | Should BeExactly 'build'
        $r.Rank | Should Be 2
        $r.Reference | Should BeExactly $RULE3_REF
    }

    It '1000-big matches rule 3 with identity big and ignores the out-of-range rank capture' {
        $r = Eval-Tab (Build-Rules (New-RuleListU)) '1000-big'
        $r.Matched | Should Be $true
        $r.Name | Should BeExactly 'big'
        $r.Rank | Should BeNullOrEmpty
        $r.Reference | Should BeExactly $RULE3_REF
    }
}

Describe 'AC-004 Malformed, runaway and hostile input' {
    It 'an invalid regex yields exactly one diagnostic at position 1 and the next rule decides' {
        $rs = Build-Rules ([object[]]@(@{ match = 'regex:(unclosed' }, @{ match = 'PowerShell*'; rank = 200 }))
        @($rs.Diagnostics).Count | Should Be 1
        $rs.Diagnostics[0].Position | Should Be 1
        $rs.Diagnostics[0].Reason | Should Match 'invalid regex'
        $r = Eval-Tab $rs 'powershell'
        $r.Rank | Should Be 200
        $r.Reference | Should BeExactly 'rule 2: PowerShell*'
    }

    It 'each of the fourteen malformed definitions yields exactly one diagnostic naming its position, and the valid fifteenth rule matches abc' {
        $defs = [object[]]@(
            @{},
            @{ match = 'a'; marker = '#' },
            @{ match = '' },
            @{ marker = '' },
            @{ match = 'a'; rank = 1000 },
            @{ match = 'a'; rank = -1 },
            @{ match = 'a'; on = 'window' },
            @{ marker = '#'; on = 'commandline' },
            @{ match = 'a'; rank = 'high' },
            @{ match = 'a'; rank = 3.0 },
            @{ match = 5 },
            @{ marker = '# ' },
            @{ marker = 'p1' },
            'just text',
            @{ match = 'a*'; rank = 10 }
        )
        $rs = Build-Rules $defs
        $diags = @($rs.Diagnostics)
        $diags.Count | Should Be 14
        for ($i = 0; $i -lt 14; $i++) {
            ('position {0}' -f $diags[$i].Position) | Should BeExactly ('position {0}' -f ($i + 1))
            [string]::IsNullOrWhiteSpace($diags[$i].Reason) | Should Be $false
        }
        $r = Eval-Tab $rs 'abc'
        $r.Matched | Should Be $true
        $r.Reference | Should BeExactly 'rule 15: a*'
        $r.Rank | Should Be 10
    }

    It 'the runaway rule times out within the bound plus one second, reports one diagnostic and lets the next rule match' {
        $rs = Build-Rules ([object[]]@(@{ match = 'regex:^(a+)+$' }, @{ match = 'a*'; rank = 10 }))
        @($rs.Diagnostics).Count | Should Be 0
        $title = ('a' * 30) + 'b'
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $r = Eval-Tab $rs $title
        $sw.Stop()
        $sw.ElapsedMilliseconds | Should BeLessThan 1100
        @($r.Diagnostics).Count | Should Be 1
        $r.Diagnostics[0].Position | Should Be 1
        $r.Diagnostics[0].Reason | Should Match 'timeout'
        $r.Matched | Should Be $true
        $r.Reference | Should BeExactly 'rule 2: a*'
    }

    It 'building and evaluating with null and empty inputs never throws' {
        $caught = $null
        try {
            [void](Build-Rules $null)
            [void](Build-Rules $null $true)
            [void](Build-Rules ([object[]]@($null)))
            $rs = Build-Rules (New-RuleListU)
            [void](Eval-Tab $null 'x')
            [void](Eval-Tab $rs $null)
            [void](Eval-Tab $rs '')
            [void](Eval-Tab $rs 'x' $null)
            [void](Eval-Tab $rs 'x' '')
        }
        catch {
            $caught = $_
        }
        $caught | Should BeNullOrEmpty
        $nullDef = Build-Rules ([object[]]@($null))
        @($nullDef.Diagnostics).Count | Should Be 1
        $nullDef.Diagnostics[0].Position | Should Be 1
    }
}

Describe 'AC-005 Marker rule' {
    It 'a marker title gives the identity without the token and the rank of the digits' {
        $u = Build-Rules (New-RuleListU)
        $r = Eval-Tab $u ('~/proj #3 ' + $EM_DASH + ' zsh')
        $r.Name | Should BeExactly ('~/proj ' + $EM_DASH + ' zsh')
        $r.Rank | Should Be 3
        $r.Reference | Should BeExactly 'rule 4: marker #'
    }

    It 'only the first qualifying token is taken: a #5 b #7 gives a b #7 and rank 5' {
        $r = Eval-Tab (Build-Rules (New-RuleListU)) 'a #5 b #7'
        $r.Name | Should BeExactly 'a b #7'
        $r.Rank | Should Be 5
    }

    It 'a title that is only the token keeps the trimmed title as the identity' {
        $r = Eval-Tab (Build-Rules (New-RuleListU)) '#12'
        $r.Name | Should BeExactly '#12'
        $r.Rank | Should Be 12
        $r.Reference | Should BeExactly 'rule 4: marker #'
    }

    It 'C#3 notes and #1234 x do not match the marker rule' {
        $u = Build-Rules (New-RuleListU)
        foreach ($title in @('C#3 notes', '#1234 x')) {
            $r = Eval-Tab $u $title
            $r.Matched | Should Be $false
            $r.Rank | Should BeNullOrEmpty
        }
    }
}

Describe 'AC-006 Command-line subject (engine rows)' {
    It 'rule 5 matches the command line of the bound session and supplies rank 250' {
        $u = Build-Rules (New-RuleListU)
        $r = Eval-Tab $u 'YODA9' $CMD_YODA9
        $r.Matched | Should Be $true
        $r.Rank | Should Be 250
        $r.Reference | Should BeExactly 'rule 5: *--attach YODA*'
    }

    It 'a commandline rule does not match when the tab is bound to no session' {
        $u = Build-Rules (New-RuleListU)
        $r = Eval-Tab $u 'YODA9' $null
        $r.Matched | Should Be $false
        $r.Rank | Should BeNullOrEmpty
    }

    It 'a rule with on=title behaves exactly like the same rule without on' {
        $plain = Build-Rules ([object[]]@(@{ match = 'PowerShell*'; rank = 200 }))
        $onTitle = Build-Rules ([object[]]@(@{ match = 'PowerShell*'; rank = 200; on = 'title' }))
        foreach ($title in @('powershell', 'Windows PowerShell')) {
            $a = Eval-Tab $plain $title
            $b = Eval-Tab $onTitle $title
            $b.Matched | Should Be $a.Matched
            $b.Name | Should BeExactly $a.Name
            $b.Rank | Should Be $a.Rank
        }
        @($onTitle.Diagnostics).Count | Should Be 0
    }
}

# --- M2: launcher-prefix preset and grammar delegation ---

# plan.md J.2 parity table. Rank null means "no declared rank". Blank rows are normalizer-only.
function Get-ParityRows {
    $rows = New-Object System.Collections.ArrayList
    function Add-Row($Title, $Session, [bool]$Present, $Rank) {
        [void]$rows.Add([pscustomobject]@{ Title = $Title; Session = $Session; Present = $Present; Rank = $Rank })
    }
    Add-Row 'OC_YODA1' 'YODA1' $true $null
    Add-Row 'WD_AB_X' 'AB_X' $true $null
    Add-Row 'NG_AB_X' 'AB_X' $true $null
    Add-Row 'HC_YODA2' 'YODA2' $true $null
    Add-Row 'NC_OPS1' 'OPS1' $true $null
    Add-Row 'HG2_YODA1' 'YODA1' $true 2
    Add-Row 'HG2_OC_YODA1' 'OC_YODA1' $true 2
    Add-Row 'Plain Title' 'Plain Title' $false $null
    Add-Row 'oc_yoda1' 'oc_yoda1' $false $null
    Add-Row 'XY_YODA1' 'XY_YODA1' $false $null
    Add-Row 'hg2_' 'hg2_' $false $null
    Add-Row 'HG10_' 'HG10_' $false $null
    Add-Row 'HGx_' 'HGx_' $false $null
    Add-Row 'HG2' 'HG2' $false $null
    Add-Row 'HG2_' 'HG2_' $false $null
    Add-Row 'OC_' 'OC_' $false $null
    Add-Row ($STAR + ' Claude Code') ($STAR + ' Claude Code') $false $null
    Add-Row 'NC0_BUILD' 'BUILD' $true 0
    Add-Row 'WD9_SCRATCH' 'SCRATCH' $true 9
    Add-Row 'OC_EDITOR' 'EDITOR' $true $null
    Add-Row 'OC__' '_' $true $null
    Add-Row 'OC_ YODA' ' YODA' $true $null
    $rows.ToArray()
}

Describe 'M2 Preset behaviour' {
    It 'with the preset on, the preset decides OC_YODA1 and the user rule is not consulted' {
        $rs = Build-Rules ([object[]]@(@{ match = 'OC_*'; name = 'x'; rank = 1 })) $true
        $r = Eval-Tab $rs 'OC_YODA1'
        $r.Name | Should BeExactly 'YODA1'
        $r.Rank | Should BeNullOrEmpty
        $r.Reference | Should BeExactly 'preset launcher-prefix'
    }

    It 'the preset is the first rule, carries position 0 and does not shift user positions' {
        $rs = Build-Rules ([object[]]@(@{ match = '' }, @{ match = 'a*' })) $true
        $rs.PresetEnabled | Should Be $true
        $rules = @($rs.Rules)
        $rules.Count | Should Be 2
        $rules[0].IsPreset | Should Be $true
        $rules[0].Position | Should Be 0
        $rules[1].Reference | Should BeExactly 'rule 2: a*'
        @($rs.Diagnostics).Count | Should Be 1
        $rs.Diagnostics[0].Position | Should Be 1
    }

    It 'the preset has no match timeout while a user regex keeps the 100 ms bound' {
        $infinite = [Text.RegularExpressions.Regex]::InfiniteMatchTimeout
        ([TerminalOrganizer.Core.Rules.LauncherPrefixPreset]::MatchTimeout -eq $infinite) | Should Be $true
        $rs = Build-Rules ([object[]]@(@{ match = 'regex:^a' })) $true
        $rules = @($rs.Rules)
        ($rules[0].Regex.MatchTimeout -eq $infinite) | Should Be $true
        $rules[1].Regex.MatchTimeout.TotalMilliseconds | Should Be 100
    }

    It 'a preset-off rule set holds no preset rule' {
        $rs = Build-Rules ([object[]]@(@{ match = 'a' })) $false
        $rs.PresetEnabled | Should Be $false
        @($rs.Rules).Count | Should Be 1
        @($rs.Rules)[0].IsPreset | Should Be $false
    }
}

Describe 'AC-008 PRESET-PARITY' {
    It 'every row of the parity table agrees across the normalizer, its prefix strip and the rule engine' {
        $preset = Build-Rules $null $true
        $failures = New-Object System.Collections.ArrayList
        foreach ($row in (Get-ParityRows)) {
            $parsed = [TerminalOrganizer.Core.Windows.TitleNormalizer]::Parse($row.Title)
            $stripped = [TerminalOrganizer.Core.Windows.TitleNormalizer]::StripPrefix($row.Title)
            $engine = Eval-Tab $preset $row.Title
            $label = '[' + $row.Title + ']'
            if ($parsed.SessionTitle -cne $row.Session) { [void]$failures.Add($label + ' Parse.SessionTitle=' + $parsed.SessionTitle) }
            if ($parsed.PrefixPresent -ne $row.Present) { [void]$failures.Add($label + ' Parse.PrefixPresent=' + $parsed.PrefixPresent) }
            if ($parsed.DeclaredRank -ne $row.Rank) { [void]$failures.Add($label + ' Parse.DeclaredRank=' + $parsed.DeclaredRank) }
            if ($parsed.Original -cne $row.Title) { [void]$failures.Add($label + ' Parse.Original=' + $parsed.Original) }
            if ($stripped -cne $row.Session) { [void]$failures.Add($label + ' StripPrefix=' + $stripped) }
            if ($engine.Name -cne $row.Session) { [void]$failures.Add($label + ' engine.Name=' + $engine.Name) }
            if (($engine.Reference -eq 'preset launcher-prefix') -ne $row.Present) { [void]$failures.Add($label + ' engine.Reference=' + $engine.Reference) }
            if ($engine.Rank -ne $row.Rank) { [void]$failures.Add($label + ' engine.Rank=' + $engine.Rank) }
        }
        ($failures -join '; ') | Should BeExactly ''
    }

    It 'the empty-string and null rows: normalizer paths unchanged, engine path gives no identity name and no rank' {
        $preset = Build-Rules $null $true
        $empty = [TerminalOrganizer.Core.Windows.TitleNormalizer]::Parse([string]::Empty)
        $empty.SessionTitle | Should BeExactly ''
        ($null -ne $empty.SessionTitle) | Should Be $true
        $empty.PrefixPresent | Should Be $false
        $empty.DeclaredRank | Should BeNullOrEmpty
        $nul = [TerminalOrganizer.Core.Windows.TitleNormalizer]::Parse([NullString]::Value)
        ($null -eq $nul.SessionTitle) | Should Be $true
        ($null -eq $nul.Original) | Should Be $true
        $nul.PrefixPresent | Should Be $false
        ($null -eq [TerminalOrganizer.Core.Windows.TitleNormalizer]::StripPrefix([NullString]::Value)) | Should Be $true
        foreach ($title in @([string]::Empty, [NullString]::Value)) {
            $r = Eval-Tab $preset $title
            ($null -eq $r.Name) | Should Be $true
            $r.Rank | Should BeNullOrEmpty
            $r.Matched | Should Be $false
        }
    }
}

# --- M3: composition and priority inputs ---

function New-Session([string]$Name, [string]$Kind, [string]$CommandLine) {
    New-Object TerminalOrganizer.Core.Windows.SessionRecord -ArgumentList $Name, ([TerminalOrganizer.Core.Windows.SessionKind]$Kind), $CommandLine
}

function New-Acq([int]$Handle, $TabResult) {
    $identity = [TerminalOrganizer.Core.Windows.WindowIdentity]::new([IntPtr]$Handle, 42, 12345, 'CASCADIA_HOSTING_WINDOW_CLASS', 'fixture')
    New-Object TerminalOrganizer.Core.Windows.AcquiredWindow -ArgumentList $identity, $TabResult, $null, $null
}

function New-TabOk([string[]]$Titles) {
    [TerminalOrganizer.Core.Windows.TabTitleResult]::Ok($Titles)
}

# Composes one window (tab titles) through the three-argument overload; returns its snapshot.
function Compose-One($Titles, $Sessions, $RuleSet) {
    $acq = [TerminalOrganizer.Core.Windows.AcquiredWindow[]]@((New-Acq 101 (New-TabOk ([string[]]$Titles))))
    $snaps = [TerminalOrganizer.Core.Windows.WindowSnapshotBuilder]::Compose($acq, ([TerminalOrganizer.Core.Windows.SessionRecord[]]@($Sessions)), $RuleSet)
    @($snaps)[0]
}

# Composes one window through the two-argument overload (no rule set).
function Compose-NoRules($Titles, $Sessions) {
    $acq = [TerminalOrganizer.Core.Windows.AcquiredWindow[]]@((New-Acq 102 (New-TabOk ([string[]]$Titles))))
    $snaps = [TerminalOrganizer.Core.Windows.WindowSnapshotBuilder]::Compose($acq, ([TerminalOrganizer.Core.Windows.SessionRecord[]]@($Sessions)))
    @($snaps)[0]
}

function Get-ClassName($Session) {
    if ($null -eq $Session) { return 'Unidentified' }
    switch ($Session.Kind.ToString()) {
        'Local' { 'LocalSession' }
        'Remote' { 'RemoteSession' }
        'WindowsNative' { 'WindowsNative' }
    }
}

# Resolves a composed snapshot through the new priority-input constructor.
function Resolve-Snapshot($Snapshot, $Session, $Default = 500, $Manual = $null) {
    $class = [TerminalOrganizer.Core.Overflow.DerivedPriorityClass]::Parse([TerminalOrganizer.Core.Overflow.DerivedPriorityClass], (Get-ClassName $Session))
    $in = [TerminalOrganizer.Core.Overflow.PriorityInput]::new('w1', $Manual, $Snapshot.RuleRank, $Snapshot.RuleReference, $Default,
        $class, $false, $false, $false, 0, 'M1', 0, 0)
    [TerminalOrganizer.Core.Overflow.PriorityResolver]::Resolve($in)
}

Describe 'AC-003 Resolved rank and provenance under rule list U' {
    It 'every standing title resolves to the pinned identity, rule rank, rank, source and untruncated provenance' {
        $yoda9 = New-Session 'YODA9' 'Local' $CMD_YODA9
        $rows = @(
            @('powershell', $null, 'powershell', 200, 200, 'rule 1: PowerShell*', 'Declared'),
            @('PowerShell 7.4', $null, 'PowerShell 7.4', 200, 200, 'rule 1: PowerShell*', 'Declared'),
            @('Windows PowerShell', $null, 'Windows PowerShell', $null, 500, 'default', 'Derived'),
            @('ssh server1', $null, 'server1', 400, 400, 'rule 2: regex:^ssh (?<name>[^ ]+)', 'Declared'),
            @('SSH server1', $null, 'SSH server1', $null, 500, 'default', 'Derived'),
            @('2-build', $null, 'build', 2, 2, $RULE3_REF, 'Declared'),
            @('1000-big', $null, 'big', $null, 500, ('default (matched ' + $RULE3_REF + ')'), 'Derived'),
            @(('~/proj #3 ' + $EM_DASH + ' zsh'), $null, ('~/proj ' + $EM_DASH + ' zsh'), 3, 3, 'rule 4: marker #', 'Declared'),
            @('a #5 b #7', $null, 'a b #7', 5, 5, 'rule 4: marker #', 'Declared'),
            @('#12', $null, '#12', 12, 12, 'rule 4: marker #', 'Declared'),
            @('C#3 notes', $null, 'C#3 notes', $null, 500, 'default', 'Derived'),
            @('#1234 x', $null, '#1234 x', $null, 500, 'default', 'Derived'),
            @('YODA9', $yoda9, 'YODA9', 250, 250, 'rule 5: *--attach YODA*', 'Declared'),
            @('YODA9', $null, 'YODA9', $null, 500, 'default', 'Derived'),
            @('Untitled tab', $null, 'Untitled tab', $null, 500, 'default', 'Derived'),
            @('  spaced  ', $null, 'spaced', $null, 500, 'default', 'Derived'),
            @('   ', $null, $null, $null, 500, 'default', 'Derived')
        )
        $u = Build-Rules (New-RuleListU)
        $failures = New-Object System.Collections.ArrayList
        foreach ($row in $rows) {
            $sessions = if ($null -ne $row[1]) { @($row[1]) } else { @() }
            $snap = Compose-One @($row[0]) $sessions $u
            $res = Resolve-Snapshot $snap $row[1]
            $label = '[' + $row[0] + ']'
            if ($null -eq $row[2]) { if ($null -ne $snap.IdentityName) { [void]$failures.Add($label + ' identity=' + $snap.IdentityName) } }
            elseif ($snap.IdentityName -cne $row[2]) { [void]$failures.Add($label + ' identity=' + $snap.IdentityName) }
            if ($snap.RuleRank -ne $row[3]) { [void]$failures.Add($label + ' ruleRank=' + $snap.RuleRank) }
            if ($res.Rank -ne $row[4]) { [void]$failures.Add($label + ' rank=' + $res.Rank) }
            if ($res.Provenance -cne $row[5]) { [void]$failures.Add($label + ' provenance=' + $res.Provenance) }
            if ($res.Source.ToString() -ne $row[6]) { [void]$failures.Add($label + ' source=' + $res.Source) }
        }
        ($failures -join '; ') | Should BeExactly ''
    }

    It 'the 1000-big provenance is exactly the 65-character untruncated text' {
        $u = Build-Rules (New-RuleListU)
        $snap = Compose-One @('1000-big') @() $u
        $res = Resolve-Snapshot $snap $null
        $res.Provenance.Length | Should Be 65
        $res.Provenance | Should BeExactly 'default (matched rule 3: regex:^(?<rank>[0-9]{1,4})-(?<name>.+)$)'
    }
}

Describe 'AC-006 Fixed binding' {
    It 'the tab YODA9 binds to its Local session and the outcome carries rank 250 from rule 5' {
        $u = Build-Rules (New-RuleListU)
        $snap = Compose-One @('YODA9') @((New-Session 'YODA9' 'Local' $CMD_YODA9)) $u
        $snap.Tabs[0].Session.Name | Should BeExactly 'YODA9'
        $snap.Identified | Should Be $true
        $snap.RuleRank | Should Be 250
        $snap.RuleReference | Should BeExactly 'rule 5: *--attach YODA*'
    }

    It 'the same title composed with no open session matches no rule' {
        $u = Build-Rules (New-RuleListU)
        $snap = Compose-One @('YODA9') @() $u
        $snap.Tabs[0].Session | Should BeNullOrEmpty
        $snap.RuleRank | Should BeNullOrEmpty
        $snap.RuleReference | Should BeNullOrEmpty
    }

    It 'a catch-all fixed name cannot bind a session: scratch stays unbound while YODA3 binds as today' {
        $rs = Build-Rules ([object[]]@(@{ match = '*'; name = 'YODA3' }))
        $session = New-Session 'YODA3' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach YODA3'
        $scratch = Compose-One @('scratch') @($session) $rs
        $scratch.Tabs[0].Session | Should BeNullOrEmpty
        $scratch.Identified | Should Be $false
        $scratch.Mergeable | Should Be $false
        $bound = Compose-One @('YODA3') @($session) $rs
        $bound.Tabs[0].Session.Name | Should BeExactly 'YODA3'
        $bound.Identified | Should Be $true
        $bound.Mergeable | Should Be $true
    }
}

Describe 'AC-007 Composition outcome' {
    It 'single-tab windows get the pinned identity and rule rank under U' {
        $u = Build-Rules (New-RuleListU)
        $a = Compose-One @('Untitled tab') @() $u
        $a.IdentityName | Should BeExactly 'Untitled tab'
        $a.RuleRank | Should BeNullOrEmpty
        (Compose-One @('  spaced  ') @() $u).IdentityName | Should BeExactly 'spaced'
        ($null -eq (Compose-One @('   ') @() $u).IdentityName) | Should Be $true
    }

    It 'a window whose tab read failed uses the trimmed fallback title as identity' {
        $u = Build-Rules (New-RuleListU)
        $failed = [TerminalOrganizer.Core.Windows.TabTitleResult]::Failure('  fallback  ', 'uia failed')
        $acq = [TerminalOrganizer.Core.Windows.AcquiredWindow[]]@((New-Acq 103 $failed))
        $snap = @([TerminalOrganizer.Core.Windows.WindowSnapshotBuilder]::Compose($acq, [TerminalOrganizer.Core.Windows.SessionRecord[]]@(), $u))[0]
        $snap.IdentityName | Should BeExactly 'fallback'
        $snap.HasRuleOutcome | Should Be $true
    }

    It 'a window with no readable tab takes its trimmed raw window title' {
        $u = Build-Rules (New-RuleListU)
        $identity = [TerminalOrganizer.Core.Windows.WindowIdentity]::new([IntPtr]104, 42, 12345, 'CASCADIA_HOSTING_WINDOW_CLASS', '  raw title  ')
        $none = New-Object TerminalOrganizer.Core.Windows.AcquiredWindow -ArgumentList $identity, $null, $null, $null
        $snap = @([TerminalOrganizer.Core.Windows.WindowSnapshotBuilder]::Compose([TerminalOrganizer.Core.Windows.AcquiredWindow[]]@($none), [TerminalOrganizer.Core.Windows.SessionRecord[]]@(), $u))[0]
        $snap.Tabs.Length | Should Be 0
        $snap.IdentityName | Should BeExactly 'raw title'
        $snap.RuleRank | Should BeNullOrEmpty
    }

    It 'a two-tab window takes the first tab identity and the rank and reference of the rank-supplying second tab' {
        $u = Build-Rules (New-RuleListU)
        $snap = Compose-One @('Untitled tab', '2-build') @() $u
        $snap.IdentityName | Should BeExactly 'Untitled tab'
        $snap.RuleRank | Should Be 2
        $snap.RuleReference | Should BeExactly $RULE3_REF
        (Resolve-Snapshot $snap $null).Provenance | Should BeExactly $RULE3_REF
    }

    It 'with no rank-supplying tab the reference is the first tab matched rule, and none when the first tab matched no rule' {
        $rs = Build-Rules ([object[]]@(@{ match = 'a*' }, @{ match = 'b*' }))
        $matchedFirst = Compose-One @('b1', 'a1') @() $rs
        $matchedFirst.RuleRank | Should BeNullOrEmpty
        $matchedFirst.RuleReference | Should BeExactly 'rule 2: b*'
        $unmatchedFirst = Compose-One @('zzz', 'b1') @() $rs
        $unmatchedFirst.RuleRank | Should BeNullOrEmpty
        $unmatchedFirst.RuleReference | Should BeNullOrEmpty
    }

    It 'the overload without a rule set equals the overload with a preset-only rule set' {
        $session = New-Session 'YODA1' 'Local' $CMD_LOCAL
        foreach ($title in @('OC_YODA1', 'Plain Title', 'HG2_YODA1')) {
            $a = Compose-NoRules @($title) @($session)
            $b = Compose-One @($title) @($session) ([TerminalOrganizer.Core.Rules.RuleSet]::PresetOnly())
            $a.IdentityName | Should BeExactly $b.IdentityName
            $a.RuleRank | Should Be $b.RuleRank
            $a.RuleReference | Should BeExactly $b.RuleReference
            $a.HasRuleOutcome | Should Be $true
            $b.HasRuleOutcome | Should Be $true
        }
        (Compose-NoRules @('OC_YODA1') @($session)).IdentityName | Should BeExactly 'YODA1'
        (Compose-NoRules @('Plain Title') @($session)).IdentityName | Should BeExactly 'Plain Title'
    }

    It 'today eight-argument constructor derives identity from session evidence alone and carries no rule outcome' {
        $identity = [TerminalOrganizer.Core.Windows.WindowIdentity]::new([IntPtr]105, 42, 12345, 'CASCADIA_HOSTING_WINDOW_CLASS', 'fixture')
        $session = New-Session 'YODA1' 'Local' $CMD_LOCAL
        $tab = New-Object TerminalOrganizer.Core.Windows.TabSnapshot -ArgumentList 'OC_YODA1', 'YODA1', $session
        $trusted = [TerminalOrganizer.Core.Windows.TabReadQuality]::Trusted
        $unidentified = [TerminalOrganizer.Core.Windows.WindowSnapshot]::new($identity, $null, $null, [TerminalOrganizer.Core.Windows.TabSnapshot[]]@($tab), $false, [string[]]@('x'), $false, $trusted)
        ($null -eq $unidentified.IdentityName) | Should Be $true
        $unidentified.HasRuleOutcome | Should Be $false
        $unidentified.RuleRank | Should BeNullOrEmpty
        $identified = [TerminalOrganizer.Core.Windows.WindowSnapshot]::new($identity, $null, $null, [TerminalOrganizer.Core.Windows.TabSnapshot[]]@($tab), $true, [string[]]@(), $true, $trusted)
        $identified.IdentityName | Should BeExactly 'YODA1'
        $identified.HasRuleOutcome | Should Be $false
        $identified.RuleRank | Should BeNullOrEmpty
    }

    It 'every snapshot reports whether it carries a rule outcome, even when no rule matched' {
        $u = Build-Rules (New-RuleListU)
        $a = Compose-One @('Untitled tab') @() $u
        $a.RuleReference | Should BeNullOrEmpty
        $a.HasRuleOutcome | Should Be $true
        (Compose-NoRules @('Plain Title') @()).HasRuleOutcome | Should Be $true
    }

    It 'composing with null windows, null sessions and a null rule set never throws' {
        $caught = $null
        try {
            $r1 = [TerminalOrganizer.Core.Windows.WindowSnapshotBuilder]::Compose($null, $null, $null)
            $acq = [TerminalOrganizer.Core.Windows.AcquiredWindow[]]@((New-Acq 106 (New-TabOk ([string[]]@('x')))))
            $r2 = [TerminalOrganizer.Core.Windows.WindowSnapshotBuilder]::Compose($acq, $null, $null)
            @($r1).Count | Should Be 0
            @($r2).Count | Should Be 1
        }
        catch {
            $caught = $_
        }
        $caught | Should BeNullOrEmpty
    }
}

Describe 'AC-008 Session binding is unchanged by rules' {
    It 'the session-matcher window cases give the same identified, mergeable and stripped titles through the matcher, the overload without a rule set and the overload with rule list U' {
        $sessions = @(
            (New-Session 'YODA1' 'Local' $CMD_LOCAL),
            (New-Session 'YODA2' 'Remote' $CMD_REMOTE),
            (New-Session 'OPS1' 'WindowsNative' $CMD_NATIVE)
        )
        $cases = @(
            @('OC_YODA1'),
            @('NG_AB_X', 'HC_YODA2'),
            @(($STAR + ' Claude Code')),
            @('NC_OPS1'),
            @('WD_AB_X'),
            @('OC_YODA1', 'HC_YODA2')
        )
        $u = Build-Rules (New-RuleListU)
        $failures = New-Object System.Collections.ArrayList
        foreach ($titles in $cases) {
            $direct = [TerminalOrganizer.Core.Windows.SessionMatcher]::Match([string[]]$titles, [TerminalOrganizer.Core.Windows.SessionRecord[]]$sessions)
            $plain = Compose-NoRules $titles $sessions
            $withU = Compose-One $titles $sessions $u
            $label = '[' + ($titles -join ',') + ']'
            $expectStripped = (@($direct.Tabs | ForEach-Object { $_.StrippedTitle }) -join '|')
            foreach ($pair in @(@('plain', $plain), @('U', $withU))) {
                $snap = $pair[1]
                if ($snap.Identified -ne $direct.Identified) { [void]$failures.Add($label + ' ' + $pair[0] + ' identified') }
                if ($snap.Mergeable -ne $direct.Mergeable) { [void]$failures.Add($label + ' ' + $pair[0] + ' mergeable') }
                $stripped = (@($snap.Tabs | ForEach-Object { $_.StrippedTitle }) -join '|')
                if ($stripped -cne $expectStripped) { [void]$failures.Add($label + ' ' + $pair[0] + ' stripped=' + $stripped) }
            }
        }
        ($failures -join '; ') | Should BeExactly ''
    }
}

Describe 'AC-009 Priority inputs, sources and provenance' {
    It 'a manual rank beats the rule rank: powershell with manual 700 resolves to 700, Manual, manual' {
        $u = Build-Rules (New-RuleListU)
        $snap = Compose-One @('powershell') @() $u
        $res = Resolve-Snapshot $snap $null 500 700
        $res.Rank | Should Be 700
        $res.Source.ToString() | Should BeExactly 'Manual'
        $res.Provenance | Should BeExactly 'manual'
        $res.Reason | Should BeExactly 'manual'
    }

    It 'HG2_YODA1 with a Local session resolves to 2, Declared, preset launcher-prefix' {
        $session = New-Session 'YODA1' 'Local' $CMD_LOCAL
        $snap = Compose-One @('HG2_YODA1') @($session) ([TerminalOrganizer.Core.Rules.RuleSet]::PresetOnly())
        $res = Resolve-Snapshot $snap $session
        $res.Rank | Should Be 2
        $res.Source.ToString() | Should BeExactly 'Declared'
        $res.Provenance | Should BeExactly 'preset launcher-prefix'
        $res.Reason | Should BeExactly 'declared'
    }

    It 'OC_EDITOR with a Local session resolves to 300, Derived, session Local (matched preset launcher-prefix)' {
        $session = New-Session 'EDITOR' 'Local' 'wsl.exe -d Ubuntu --exec /home/dev/run_dev_launch.sh --attach EDITOR'
        $snap = Compose-One @('OC_EDITOR') @($session) ([TerminalOrganizer.Core.Rules.RuleSet]::PresetOnly())
        $res = Resolve-Snapshot $snap $session
        $res.Rank | Should Be 300
        $res.Source.ToString() | Should BeExactly 'Derived'
        $res.Provenance | Should BeExactly 'session Local (matched preset launcher-prefix)'
        $res.Reason | Should BeExactly 'derived:LocalSession'
    }

    It 'a Remote-session window with no rule match resolves to 400, Derived, session Remote' {
        $u = Build-Rules (New-RuleListU)
        $session = New-Session 'OPS2' 'Remote' 'wsl.exe -d Ubuntu --exec /tmp/rdl_attach.sh user@host OPS2'
        $snap = Compose-One @('OPS2') @($session) $u
        $res = Resolve-Snapshot $snap $session
        $res.Rank | Should Be 400
        $res.Source.ToString() | Should BeExactly 'Derived'
        $res.Provenance | Should BeExactly 'session Remote'
    }

    It 'a WindowsNative session class gives rank 100 and session WindowsNative' {
        $session = New-Session 'OPS1' 'WindowsNative' $CMD_NATIVE
        $snap = Compose-One @('OPS1') @($session) (Build-Rules $null $false)
        $res = Resolve-Snapshot $snap $session
        $res.Rank | Should Be 100
        $res.Provenance | Should BeExactly 'session WindowsNative'
    }

    It 'Untitled tab with default rank 450 resolves to 450, Derived, default' {
        $u = Build-Rules (New-RuleListU)
        $snap = Compose-One @('Untitled tab') @() $u
        $res = Resolve-Snapshot $snap $null 450
        $res.Rank | Should Be 450
        $res.Source.ToString() | Should BeExactly 'Derived'
        $res.Provenance | Should BeExactly 'default'
        $res.Reason | Should BeExactly 'derived:Unidentified'
    }

    It 'a default rank outside 0..999 falls back to 500' {
        $u = Build-Rules (New-RuleListU)
        $snap = Compose-One @('Untitled tab') @() $u
        (Resolve-Snapshot $snap $null 1000).Rank | Should Be 500
        (Resolve-Snapshot $snap $null -1).Rank | Should Be 500
    }

    It 'manager, full-screen and stable-occupant windows stay immovable with today reasons through the new constructor' {
        $rows = @(
            @('Manager', $true, $false, $false, 'manager'),
            @('FullScreen', $false, $true, $false, 'full-screen'),
            @('StableOccupant', $false, $false, $true, 'stable occupant')
        )
        foreach ($row in $rows) {
            $class = [TerminalOrganizer.Core.Overflow.DerivedPriorityClass]::Parse([TerminalOrganizer.Core.Overflow.DerivedPriorityClass], $row[0])
            $in = [TerminalOrganizer.Core.Overflow.PriorityInput]::new('imm', 700, 200, 'rule 1: x', 450, $class, $row[1], $row[2], $row[3], 0, 'M1', 0, 0)
            $res = [TerminalOrganizer.Core.Overflow.PriorityResolver]::Resolve($in)
            $res.Immovable | Should Be $true
            $res.ImmovableReason | Should BeExactly $row[4]
            $res.Reason | Should BeExactly $row[4]
        }
    }

    It 'the existing priority-input constructor keeps today source, rank and reason, and reports plain provenance' {
        $local = [TerminalOrganizer.Core.Overflow.DerivedPriorityClass]::LocalSession
        $none = [TerminalOrganizer.Core.Overflow.DerivedPriorityClass]::Unidentified
        $a = [TerminalOrganizer.Core.Overflow.PriorityResolver]::Resolve([TerminalOrganizer.Core.Overflow.PriorityInput]::new('a', 700, 1, $local, $false, $false, $false, 0, 'M1', 0, 0))
        $a.Source.ToString() | Should BeExactly 'Manual'
        $a.Rank | Should Be 700
        $a.Reason | Should BeExactly 'manual'
        $a.Provenance | Should BeExactly 'manual'
        $b = [TerminalOrganizer.Core.Overflow.PriorityResolver]::Resolve([TerminalOrganizer.Core.Overflow.PriorityInput]::new('b', $null, 8, $local, $false, $false, $false, 0, 'M1', 0, 0))
        $b.Source.ToString() | Should BeExactly 'Declared'
        $b.Rank | Should Be 8
        $b.Reason | Should BeExactly 'declared'
        $b.Provenance | Should BeExactly 'preset launcher-prefix'
        $c = [TerminalOrganizer.Core.Overflow.PriorityResolver]::Resolve([TerminalOrganizer.Core.Overflow.PriorityInput]::new('c', $null, $null, $none, $false, $false, $false, 0, 'M1', 0, 0))
        $c.Source.ToString() | Should BeExactly 'Derived'
        $c.Rank | Should Be 500
        $c.Reason | Should BeExactly 'derived:Unidentified'
        $c.Provenance | Should BeExactly 'default'
        $d = [TerminalOrganizer.Core.Overflow.PriorityResolver]::Resolve([TerminalOrganizer.Core.Overflow.PriorityInput]::new('d', $null, $null, $local, $false, $false, $false, 0, 'M1', 0, 0))
        $d.Rank | Should Be 300
        $d.Provenance | Should BeExactly 'session Local'
    }
}

Describe 'AC-010 No user-visible change (standing guards)' {
    It 'Core/Rules has no P/Invoke, file, registry, clock or environment access' {
        $rulesDir = Join-Path $repoRoot 'src\TerminalOrganizer.Core\Rules'
        $text = (Get-ChildItem -LiteralPath $rulesDir -Filter '*.cs' | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
        foreach ($forbidden in @('DllImport', 'System.IO', 'Microsoft.Win32', 'DateTime', 'Stopwatch', 'Environment.', 'Process.')) {
            $text.Contains($forbidden) | Should Be $false
        }
    }

    It 'no rule set reaches the session matcher, the merge evidence or the merge planner' {
        foreach ($relative in @('Windows\SessionMatcher.cs', 'Overflow\MergeEvidence.cs', 'Overflow\MergePlanner.cs')) {
            $text = [IO.File]::ReadAllText((Join-Path $repoRoot ('src\TerminalOrganizer.Core\' + $relative)))
            $text.Contains('RuleSet') | Should Be $false
            $text.Contains('RuleEvaluator') | Should Be $false
        }
    }
}
