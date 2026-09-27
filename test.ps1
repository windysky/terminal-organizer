# Builds, then runs the Pester 3.4.0 suite in a fresh Windows PowerShell 5.1 process.
# Works when started from Windows PowerShell 5.1 or pwsh 7; exits with the child's exit code
# (with -EnableExit that is the number of failed tests).
param(
    [string]$TestPath = (Join-Path $PSScriptRoot 'tests')
)

& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) {
    Write-Output ('build.ps1 failed with exit code ' + $LASTEXITCODE)
    exit $LASTEXITCODE
}

$resolvedPath = (Resolve-Path -LiteralPath $TestPath).ProviderPath
$quotedPath = $resolvedPath.Replace("'", "''")
$ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$command = "Import-Module Pester -RequiredVersion 3.4.0; Invoke-Pester -Path '$quotedPath' -EnableExit"

# pwsh 7 prepends its own module folders to PSModulePath; keep them away from the 5.1 child.
$savedModulePath = $env:PSModulePath
if ($PSVersionTable.PSEdition -eq 'Core') {
    # Drops ...\PowerShell\... folders (pwsh 7) and keeps ...\WindowsPowerShell\... folders (5.1).
    $env:PSModulePath = (($env:PSModulePath -split ';') | Where-Object { $_ -and ($_ -notmatch '(?<!Windows)PowerShell\\') }) -join ';'
}
try {
    & $ps51 -NoProfile -ExecutionPolicy Bypass -Command $command
    $code = $LASTEXITCODE
}
finally {
    $env:PSModulePath = $savedModulePath
}
exit $code
