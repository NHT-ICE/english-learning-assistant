param([string]$Version = '1.0.0-beta1')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[A-Za-z0-9.-]+$') { throw 'Invalid release version.' }
. (Join-Path $PSScriptRoot 'tools\Toolchain.ps1')
$toolchain = Get-BuildToolchain
$references = @(Get-FrameworkReferences $toolchain)
$binaryRoot = Join-Path $PSScriptRoot 'build\bin'
$nativeRoot = Join-Path $PSScriptRoot 'build\native'
$releaseRoot = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Path $binaryRoot, $nativeRoot, $releaseRoot -Force | Out-Null

# Generate the interop assembly from Windows; no vendor binaries are checked in.
$importer = Join-Path $binaryRoot 'ImportSystemUia.exe'
& $toolchain.Csc /nologo /target:exe /platform:x64 /nostdlib+ $references "/out:$importer" (Join-Path $PSScriptRoot 'tools\ImportSystemUia.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI Automation importer compilation failed.' }
Push-Location $binaryRoot
try {
    & $importer 'LearningUia.dll'
    if ($LASTEXITCODE -ne 0) { throw 'UI Automation import failed.' }
} finally { Pop-Location }

$cpp = Join-Path $toolchain.Msvc 'bin\Hostx64\x64\cl.exe'
$includePaths = @((Join-Path $toolchain.Msvc 'include')) + @('ucrt', 'shared', 'um' | ForEach-Object { Join-Path $toolchain.SdkRoot "Include\$($toolchain.SdkVersion)\$_" })
$includeArgs = @($includePaths | ForEach-Object { "/I$_" })
$libraryPaths = @((Join-Path $toolchain.Msvc 'lib\x64')) + @('ucrt', 'um' | ForEach-Object { Join-Path $toolchain.SdkRoot "Lib\$($toolchain.SdkVersion)\$_\x64" })
$libraryArgs = @($libraryPaths | ForEach-Object { "/LIBPATH:$_" })
Push-Location $nativeRoot
try {
    & $cpp /nologo /LD /MT /EHsc /std:c++17 /utf-8 /O2 /W4 /DUNICODE /D_UNICODE $includeArgs (Join-Path $PSScriptRoot 'native\ImeCommitBridge.cpp') "/Fo$nativeRoot\ImeCommitBridge.obj" /link $libraryArgs user32.lib imm32.lib "/OUT:$binaryRoot\ImeCommitBridge.dll" "/IMPLIB:$nativeRoot\ImeCommitBridge.lib"
    if ($LASTEXITCODE -ne 0) { throw 'Native bridge compilation failed.' }
} finally { Pop-Location }

$bindings = Join-Path $binaryRoot 'LearningUia.dll'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $toolchain.Csc /nologo /target:winexe /platform:x64 /optimize+ /debug- /langversion:latest /define:PORTABLE /nostdlib+ $references "/r:$bindings" "/out:$binaryRoot\EnglishLearningAssistant.exe" "/win32manifest:$PSScriptRoot\src\app.manifest" $sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }

# Package only named files, never a used application's directory or build folder.
$packageName = "EnglishLearningAssistant-$Version-win-x64"
$packageRoot = Join-Path $releaseRoot $packageName
$allowed = @('EnglishLearningAssistant.exe', 'LearningUia.dll', 'ImeCommitBridge.dll', 'settings.json', 'README.md', 'LICENSE')
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
if (@(Get-ChildItem -LiteralPath $packageRoot -Force | Where-Object { $_.PSIsContainer -or $_.Name -notin $allowed }).Count) {
    throw 'Release directory contains extra files. Choose a fresh release version.'
}
foreach ($name in $allowed[0..2]) { Copy-Item -LiteralPath (Join-Path $binaryRoot $name) -Destination (Join-Path $packageRoot $name) -Force }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'config\defaults.json') -Destination (Join-Path $packageRoot 'settings.json') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\USER_GUIDE.md') -Destination (Join-Path $packageRoot 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $packageRoot 'LICENSE') -Force
$files = @($allowed | ForEach-Object { Join-Path $packageRoot $_ })
$zip = Join-Path $releaseRoot "$packageName.zip"
Compress-Archive -LiteralPath $files -DestinationPath $zip -CompressionLevel Optimal -Force
Write-Output "Built $zip ($((Get-Item -LiteralPath $zip).Length) bytes)"
