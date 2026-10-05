$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools\Toolchain.ps1')
$toolchain = Get-BuildToolchain
$references = @(Get-FrameworkReferences $toolchain)
$binaryRoot = Join-Path $PSScriptRoot 'build\bin'
$testRoot = Join-Path $PSScriptRoot 'build\tests'
$bindings = Join-Path $binaryRoot 'LearningUia.dll'
if (!(Test-Path -LiteralPath $bindings)) { throw 'Run .\build.ps1 first.' }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
Copy-Item -LiteralPath $bindings -Destination (Join-Path $testRoot 'LearningUia.dll') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'config\defaults.json') -Destination (Join-Path $testRoot 'settings.json') -Force
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName) +
    @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object FullName)
$suites = @('LearningIme.PortableTests', 'LearningIme.CompositionPipelineTests', 'LearningIme.RuntimeRegression')
foreach ($suite in $suites) {
    $target = Join-Path $testRoot ($suite.Split('.')[-1] + '.exe')
    & $toolchain.Csc /nologo /target:exe /platform:x64 /langversion:latest /define:PORTABLE /nostdlib+ $references "/r:$bindings" "/main:$suite" "/out:$target" $sources
    if ($LASTEXITCODE -ne 0) { throw "Test compilation failed: $suite" }
    & $target
    if ($LASTEXITCODE -ne 0) { throw "Test process failed: $suite" }
}
$reports = @('portable', 'composition-pipeline', 'bounded-range', 'input-session', 'submission', 'controller', 'settings')
$checks = 0
foreach ($name in $reports) {
    $report = Get-Content -LiteralPath (Join-Path $testRoot "$name-test-results.json") -Raw | ConvertFrom-Json
    if (!$report.passed) { throw "$name failed: $($report.error)" }
    $checks += @($report.checks).Count
    Write-Output "$name passed ($(@($report.checks).Count) checks)"
}
Write-Output "All $checks checks passed. No translation API was called."
