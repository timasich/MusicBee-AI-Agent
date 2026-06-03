$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dll = Join-Path $root "bin\x86\Release\MB_AI_Agent.dll"
$testSource = Join-Path $root "tests\LocalHarness.cs"
$testExe = Join-Path $root "tests\LocalHarness.exe"
$testDll = Join-Path $root "tests\MB_AI_Agent.dll"

if (-not (Test-Path $dll)) {
    throw "Build the plugin first. Missing: $dll"
}

& C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /nologo /platform:x86 /target:exe /out:$testExe /reference:$dll $testSource
Copy-Item -LiteralPath $dll -Destination $testDll -Force
& $testExe
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
