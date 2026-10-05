param(
    [Parameter(Mandatory)][string]$Executable,
    [switch]$Interactive
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
$argument = if ($Interactive) { '--interaction-test' } else { '--self-test' }
if ($Interactive) { Write-Output 'Use an unobstructed desktop. Do not move the pointer or switch windows during this test.' }
$process = Start-Process -FilePath $exe -ArgumentList $argument -PassThru
if (-not $process.WaitForExit(90000)) {
    throw "The check did not finish. Inspect the RemoteMachine window (PID $($process.Id)); it has not been terminated."
}
$report = Join-Path (Split-Path $exe) 'self-test-results.json'
if (-not (Test-Path $report)) { throw "No test report was written. Process exit: $($process.ExitCode)." }
Get-Content -LiteralPath $report
if ($process.ExitCode -ne 0) { throw 'The native smoke check failed. See the report above.' }
