# Builds bin\MirrorSound-linux-x64.tar.gz: a self-contained Linux build with install.sh.
# Needs .NET 10 SDK and Python 3 (for a tarball that keeps the executable bits).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Push-Location "$root\android"
try { & .\gradlew.bat assembleDebug -q; if ($LASTEXITCODE) { throw 'Android build failed' } } finally { Pop-Location }

$out = "$root\bin\linux-x64"
dotnet publish "$root\linux" -c Release -r linux-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $out
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

$py = @'
import sys, tarfile, io, time
out, root, tgz = sys.argv[1], sys.argv[2], sys.argv[3]
files = [(out + '/MirrorSound', 'MirrorSound', 0o755),
         (root + '/linux/install.sh', 'install.sh', 0o755),
         (root + '/linux/uninstall.sh', 'uninstall.sh', 0o755),
         (root + '/linux/Assets/icon.png', 'mirrorsound.png', 0o644)]
with tarfile.open(tgz, 'w:gz') as tar:
    for src, name, mode in files:
        data = open(src, 'rb').read()
        if name.endswith('.sh'):
            data = data.replace(b'\r\n', b'\n')
        info = tarfile.TarInfo('MirrorSound/' + name)
        info.size, info.mode, info.mtime = len(data), mode, int(time.time())
        tar.addfile(info, io.BytesIO(data))
'@
$py | python - $out $root "$root\bin\MirrorSound-linux-x64.tar.gz"
if ($LASTEXITCODE) { throw 'packing failed' }
Get-Item "$root\bin\MirrorSound-linux-x64.tar.gz"
