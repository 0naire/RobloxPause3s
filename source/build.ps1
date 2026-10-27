$ErrorActionPreference = 'Stop'
$robloxBuildRoot = Split-Path -Parent $PSScriptRoot
$robloxOutput = Join-Path $robloxBuildRoot 'dist'
$robloxCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $robloxCompiler)) { throw '64-bit .NET Framework C# compiler was not found.' }
New-Item -ItemType Directory -Path $robloxOutput -Force | Out-Null
& $robloxCompiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /win32manifest:"$PSScriptRoot\app.manifest" /out:"$robloxOutput\RobloxPause3s.exe" "$PSScriptRoot\Wfp.cs" "$PSScriptRoot\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output (Join-Path $robloxOutput 'RobloxPause3s.exe')
