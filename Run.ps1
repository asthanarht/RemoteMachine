param(
    [switch]$DesignPreview,
    [switch]$FocusPreview
)
$ErrorActionPreference = 'Stop'
$runtime = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -eq 'Arm64') { 'win-arm64' } else { 'win-x64' }
$exe = Join-Path $PSScriptRoot "artifacts\$runtime\RemoteMachine.exe"
if (-not (Test-Path $exe)) { throw 'Build the application first: .\Build.ps1' }
$options = @()
if ($DesignPreview -or $FocusPreview) { $options += '--design-preview' }
if ($FocusPreview) { $options += '--focus-preview' }
& $exe @options
