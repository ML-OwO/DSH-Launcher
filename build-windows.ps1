$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish src/DSHLauncher.Windows/DSHTray.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o publish/win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    $checksum = (Get-FileHash -LiteralPath 'publish/win-x64/DSH Launcher.exe' -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath publish/win-x64/SHA256SUMS.txt -Value "$checksum  DSH Launcher.exe" -Encoding ascii
    Write-Host 'Output: publish/win-x64/DSH Launcher.exe'
} finally { Pop-Location }
