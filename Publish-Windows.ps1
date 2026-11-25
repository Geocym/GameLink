# Run on Windows with the .NET 10 SDK or newer compatible SDK.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'GameLink\GameLink.csproj'
[xml]$xml = Get-Content -LiteralPath $project
$version = $xml.Project.PropertyGroup.Version
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist "GameLink-v$version-win-x64"
if (Test-Path $stage) { throw "Publish directory already exists: $stage. Rename or remove it after backing up any data." }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
& dotnet publish $project -c Release -p:PublishProfile=WindowsX64 -o $stage
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
if (!(Test-Path (Join-Path $stage 'GameLink.exe'))) { throw 'GameLink.exe was not produced.' }
Copy-Item (Join-Path $root 'README.md') $stage
Copy-Item (Join-Path $root 'DISTRIBUTION.md') $stage
$zip = Join-Path $dist "GameLink-v$version-win-x64.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Write-Host "Created $zip"
Get-FileHash -Algorithm SHA256 $zip
