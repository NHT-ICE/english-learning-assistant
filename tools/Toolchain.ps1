$ErrorActionPreference = 'Stop'

function Get-BuildToolchain {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (!(Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio 2022 Build Tools with Desktop development with C++ and .NET Framework 4.8 targeting tools.' }
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (!$installation) { throw 'Visual Studio C++ x64 tools not found.' }
    $compiler = Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
    $msvc = Get-ChildItem -LiteralPath (Join-Path $installation 'VC\Tools\MSVC') -Directory |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    $sdkCandidates = @((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' -ErrorAction SilentlyContinue).KitsRoot10,
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'))
    $sdkRoot = $sdkCandidates | Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $_ 'Include')) } | Select-Object -First 1
    if (!$sdkRoot) { throw 'Windows 10/11 SDK not found.' }
    $sdk = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory |
        Where-Object { (Test-Path -LiteralPath (Join-Path $_.FullName 'ucrt')) -and (Test-Path -LiteralPath (Join-Path $sdkRoot "Lib\$($_.Name)\um\x64")) } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    $framework = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
    if (!(Test-Path -LiteralPath (Join-Path $framework 'mscorlib.dll'))) {
        # Existing Windows machines may have the runtime but no separate targeting pack.
        $runtime = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
        if ($runtime -ge 528040) { $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319' }
    }
    if (!$sdk -or !$msvc -or !(Test-Path -LiteralPath $compiler) -or !(Test-Path -LiteralPath (Join-Path $framework 'mscorlib.dll'))) {
        throw 'Missing Windows SDK, Roslyn or .NET Framework 4.8 targeting pack.'
    }
    [pscustomobject]@{ Csc = $compiler; Msvc = $msvc.FullName; SdkRoot = $sdkRoot; SdkVersion = $sdk.Name; Framework = $framework }
}

function Get-FrameworkReferences($Toolchain) {
    @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll',
      'System.Security.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll') |
        ForEach-Object { '/r:' + (Join-Path $Toolchain.Framework $_) }
}
