param([switch]$SkipTests, [switch]$Installer)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$localSdk = Join-Path $PSScriptRoot 'work\tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path -Parent $dotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Force outputs,outputs\packages | Out-Null
if (-not $SkipTests) {
    & $dotnet run --project tests\PCConnect.Tests -c Release -- --report outputs\test-results.txt
    if ($LASTEXITCODE -ne 0) { throw 'Automated tests failed' }
    & $dotnet run --project tests\PCConnect.UiTests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Pairing UI regression failed' }
}
$artifacts = @()
foreach ($rid in @('win-x64','win-arm64')) {
    $destination = Join-Path $PSScriptRoot "outputs\packages\PCConnect-$rid"
    & $dotnet publish src\PCConnect.App\PCConnect.App.csproj -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o $destination --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $rid" }
    Copy-Item -LiteralPath docs\SETUP.md,docs\LIMITATIONS.md,docs\VALIDATION.md,docs\TEST-REPORT.md,LICENSE -Destination $destination -Force
    Copy-Item -LiteralPath licenses -Destination $destination -Recurse -Force
    Copy-Item -LiteralPath THIRD-PARTY-NOTICES.md -Destination $destination -Force
    if (Test-Path outputs\test-results.txt) { Copy-Item outputs\test-results.txt $destination -Force }
    $portable = "outputs\PCConnect-Portable-$rid.zip"
    Compress-Archive -Path "$destination\*" -DestinationPath $portable -Force
    $artifacts += $portable
}
if ($Installer) {
    $compiler = & "$PSScriptRoot\scripts\bootstrap-inno.ps1"
    [xml]$project = Get-Content src\PCConnect.App\PCConnect.App.csproj
    $version = $project.Project.PropertyGroup.Version
    foreach ($rid in @('win-x64','win-arm64')) {
        & $compiler "/DRid=$rid" "/DAppVersion=$version" installer\PCConnect.iss
        if ($LASTEXITCODE -ne 0) { throw "Installer build failed: $rid" }
        $setup = "outputs\PCConnect-Setup-$rid.exe"
        $zip = "outputs\PCConnect-Setup-$rid.zip"
        Compress-Archive -LiteralPath $setup -DestinationPath $zip -Force
        $artifacts += $setup, $zip
    }
}
$artifacts | ForEach-Object { $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256; "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($hash.Path))" } | Set-Content outputs\SHA256SUMS.txt
Write-Output 'Packages and SHA256SUMS.txt are ready in outputs.'
