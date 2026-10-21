$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot '../Engine/build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Native engine build failed.' }
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$localSdk = Join-Path $PSScriptRoot '../.tools/dotnet/dotnet.exe'
if ($dotnetCommand) { $sdk = $dotnetCommand.Source } elseif (Test-Path -LiteralPath $localSdk) { $sdk = $localSdk } else { throw 'Install the .NET 8 SDK.' }
& $sdk build (Join-Path $PSScriptRoot 'Gaius.Office.csproj') -c Release -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'WinUI build failed.' }
