param(
    [ValidateSet('win-arm64', 'win-x64')]
    [string]$Runtime = $(if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -eq 'Arm64') { 'win-arm64' } else { 'win-x64' }),
    [switch]$SkipChecks
)
$ErrorActionPreference = 'Stop'
$dotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$project = Join-Path $PSScriptRoot 'src\RemoteHub\RemoteHub.csproj'
if (-not (Test-Path (Join-Path $PSScriptRoot 'lib\Interop.MSTSCLib.dll'))) {
    & (Join-Path $PSScriptRoot 'tools\Generate-RdpInterop.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Microsoft RDP bindings could not be generated.' }
}
if (-not $SkipChecks) {
    & $dotnet run --project (Join-Path $PSScriptRoot 'tests\RemoteHub.Tests\RemoteHub.Tests.csproj') --configuration Release --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'The local checks failed.' }
}
$output = Join-Path $PSScriptRoot "artifacts\$Runtime"
& $dotnet publish $project --configuration Release --runtime $Runtime --self-contained false --output $output --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }
Write-Output "Ready: $(Join-Path $output 'RemoteMachine.exe')"
