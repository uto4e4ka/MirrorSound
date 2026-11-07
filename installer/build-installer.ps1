# Builds bin\MirrorSound-Setup.exe: Android APK -> self-contained PC exe (APK embedded) -> Inno Setup installer.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Push-Location "$root\android"
try {
    if ($IsWindows -or $env:OS -eq 'Windows_NT') { .\gradlew.bat assembleDebug --console=plain -q } else { ./gradlew assembleDebug -q }
    if ($LASTEXITCODE -ne 0) { throw 'Android build failed' }
} finally { Pop-Location }

dotnet publish "$root\pc\MirrorSound.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o "$root\bin\pc-release"
if ($LASTEXITCODE -ne 0) { throw 'PC build failed' }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found: winget install JRSoftware.InnoSetup' }
& $iscc /Q "$PSScriptRoot\MirrorSound.iss"
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }

Get-Item "$root\bin\MirrorSound-Setup.exe"
