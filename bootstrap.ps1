$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$sdkVersion = '10.0.403'
$toolsDir = Join-Path $PSScriptRoot 'work\tools'
New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
$metadataPath = Join-Path $toolsDir 'releases.json'
Invoke-WebRequest 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json' -OutFile $metadataPath
$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$sdk = @($metadata.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $sdkVersion })[0]
if (-not $sdk) { throw "SDK $sdkVersion was not found in Microsoft's release metadata." }
$download = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
if (-not $download -or ([uri]$download.url).Host -notin @('builds.dotnet.microsoft.com','download.visualstudio.microsoft.com')) { throw 'Unexpected SDK source' }
$archive = Join-Path $toolsDir 'sdk.zip'
$download | ConvertTo-Json | Set-Content (Join-Path $toolsDir 'sdk-download.json')
if (-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $download.hash) {
    Invoke-WebRequest -Uri $download.url -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $download.hash) { throw 'Checksum mismatch: the archive will not be executed or extracted.' }
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $toolsDir 'dotnet') -Force
& (Join-Path $toolsDir 'dotnet\dotnet.exe') --version
if ($LASTEXITCODE -ne 0) { throw 'SDK verification failed' }
