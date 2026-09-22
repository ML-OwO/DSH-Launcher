$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish src/DSHLauncher.Linux/DSHLauncher.Linux.csproj -c Release -r linux-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o releases/linux-x64
    if ($LASTEXITCODE -ne 0) { throw 'Linux publish failed.' }
    $checksum = (Get-FileHash -LiteralPath releases/linux-x64/DSH-Launcher -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath releases/linux-x64/SHA256SUMS.txt -Value "$checksum  DSH-Launcher" -Encoding ascii
    Write-Host 'Output: releases/linux-x64/DSH-Launcher'
} finally { Pop-Location }
