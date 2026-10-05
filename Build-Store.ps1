param(
    [ValidateSet('win-arm64', 'win-x64')]
    [string[]]$Runtime = @('win-arm64', 'win-x64')
)
$ErrorActionPreference = 'Stop'
$dotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$project = Join-Path $PSScriptRoot 'src\RemoteHub\RemoteHub.csproj'
$toolsProject = Join-Path $PSScriptRoot 'tools\StorePackaging\StorePackaging.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [version]$projectXml.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$packageVersion = '{0}.{1}.{2}.0' -f $version.Major, $version.Minor, $version.Build
$output = Join-Path $PSScriptRoot "artifacts\store\$packageVersion"
if (Test-Path -LiteralPath $output) {
    throw "Store output already exists: $output. Preserve or move it before creating another package with this version."
}
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'lib\Interop.MSTSCLib.dll'))) {
    & (Join-Path $PSScriptRoot 'tools\Generate-RdpInterop.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'RDP binding generation failed.' }
}
& $dotnet restore $toolsProject --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Windows SDK packaging-tools restore failed.' }
$sdk = (& $dotnet msbuild $toolsProject -getProperty:PkgMicrosoft_Windows_SDK_BuildTools).Trim()
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $sdk)) { throw 'The restored Windows SDK path is unavailable.' }
$hostArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$packers = @(Get-ChildItem -LiteralPath $sdk -Filter 'makeappx.exe' -File -Recurse)
$makeappx = $packers | Where-Object { $_.Directory.Name -eq $hostArchitecture } | Select-Object -First 1
if (-not $makeappx -and $hostArchitecture -eq 'arm64') {
    $makeappx = $packers | Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1
}
if (-not $makeappx) { throw 'A compatible MakeAppx tool was not found in the pinned SDK package.' }
$packages = Join-Path $output 'packages'
New-Item -ItemType Directory -Path $packages -Force | Out-Null
$manifest = @()
foreach ($rid in ($Runtime | Select-Object -Unique)) {
    $payload = Join-Path $output $rid
    & $dotnet publish $project --configuration Release --runtime $rid --self-contained true --output $payload `
        -p:StoreBuild=true -p:DebugType=None -p:DebugSymbols=false --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "Self-contained Store publish failed for $rid." }
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile `
        -File (Join-Path $PSScriptRoot 'tools\StorePackaging\New-StoreAssets.ps1') `
        -Source (Join-Path $PSScriptRoot 'src\RemoteHub\Assets\RemoteHub-mark.png') `
        -OutputDirectory (Join-Path $payload 'StoreAssets')
    if ($LASTEXITCODE -ne 0) { throw 'Store image generation failed.' }
    [xml]$appx = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'packaging\AppxManifest.xml') -Raw
    $appx.Package.Identity.Version = $packageVersion
    $appx.Package.Identity.ProcessorArchitecture = $rid.Substring(4)
    $appx.Save((Join-Path $payload 'AppxManifest.xml'))
    if (-not (Test-Path -LiteralPath (Join-Path $payload 'coreclr.dll'))) {
        throw "The $rid package is missing its self-contained .NET runtime."
    }
    $package = Join-Path $packages "RemoteMachine_${packageVersion}_$($rid.Substring(4)).msix"
    & $makeappx.FullName pack /d $payload /p $package /o
    if ($LASTEXITCODE -ne 0) { throw "MSIX validation/packing failed for $rid." }
    $manifest += [pscustomobject]@{ runtime = $rid; package = $package; sha256 = (Get-FileHash -LiteralPath $package).Hash }
}
$bundle = Join-Path $output "RemoteMachine_$packageVersion.msixbundle"
& $makeappx.FullName bundle /d $packages /p $bundle /bv $packageVersion /o
if ($LASTEXITCODE -ne 0) { throw 'MSIX bundle validation/creation failed.' }
[pscustomobject]@{
    product = 'RemoteMachine'
    storeId = '9N55JM8J2XNN'
    version = $packageVersion
    sdkBuildTools = '10.0.26100.9169'
    packages = $manifest
    bundle = $bundle
    sha256 = (Get-FileHash -LiteralPath $bundle).Hash
    signed = $false
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'package-manifest.json') -Encoding utf8
Write-Output "Store upload: $bundle"
Write-Output 'This bundle is unsigned for Microsoft Store submission, not a directly installable signed release.'
