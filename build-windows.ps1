$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    [xml]$project = Get-Content -LiteralPath 'src/DSHLauncher.Windows/DSHTray.csproj' -Raw
    $version = [string]$project.Project.PropertyGroup.InformationalVersion
    if ($version -notmatch '^\d+\.\d+(?:\.\d+)?$') { throw 'Invalid Windows release version.' }
    $fileName = "DSH Launcher v$version.exe"
    $outputDirectory = Join-Path $PSScriptRoot 'releases/win-x64'
    $stagingDirectory = Join-Path $PSScriptRoot 'src/DSHLauncher.Windows/obj/publish-staging'
    dotnet publish src/DSHLauncher.Windows/DSHTray.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $stagingDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $executable = Join-Path $outputDirectory $fileName
    Copy-Item -LiteralPath (Join-Path $stagingDirectory 'DSH Launcher.exe') -Destination $executable -Force
    $checksum = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumPath = Join-Path $outputDirectory "SHA256SUMS-win-x64-v$version.txt"
    Set-Content -LiteralPath $checksumPath -Value "$checksum  $fileName" -Encoding ascii
    Write-Host "Output: $executable"
    Write-Host "Checksum: $checksumPath"
} finally { Pop-Location }
