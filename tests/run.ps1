param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$robloxRoot = Split-Path -Parent $PSScriptRoot
$robloxTestOutput = Join-Path $PSScriptRoot 'bin'
$robloxCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path $robloxTestOutput -Force | Out-Null
& (Join-Path $robloxRoot 'source\build.ps1')
& $robloxCompiler /nologo /target:exe /platform:x64 /optimize+ /out:"$robloxTestOutput\RobloxPlayerBeta.exe" "$PSScriptRoot\Probe.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test probe build failed.' }
Copy-Item -LiteralPath (Join-Path $robloxTestOutput 'RobloxPlayerBeta.exe') -Destination (Join-Path $robloxTestOutput 'ControlProbe.exe')
& $robloxCompiler /nologo /target:exe /platform:x64 /optimize+ /reference:System.Core.dll /out:"$robloxTestOutput\TestHarness.exe" "$PSScriptRoot\TestHarness.cs" "$robloxRoot\source\Wfp.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test harness build failed.' }
if ($BuildOnly) { return }
$robloxPrincipal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $robloxPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run these integration tests in an administrator PowerShell. They filter only the dedicated localhost probe executable.' }
& (Join-Path $robloxTestOutput 'TestHarness.exe')
$robloxTestExit = $LASTEXITCODE
Get-Content -LiteralPath (Join-Path $robloxTestOutput 'test-results.txt')
if ($robloxTestExit -ne 0) { throw 'Integration tests failed.' }
