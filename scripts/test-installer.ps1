$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'SoftwareGamerInstallerTest'
$evidence = Join-Path $PWD 'artifacts/installer-test'
New-Item -ItemType Directory -Force $installDir,$evidence | Out-Null

# The original executable blocks administrative tokens, even in recovery mode.
# Seed it at the target location so setup must exercise the upgrade path.
Invoke-WebRequest 'https://github.com/pedroisf/software-gamer/releases/download/v0.1.0/SoftwareGamer.exe' -OutFile "$installDir/SoftwareGamer.exe"
$legacy = (Get-FileHash "$installDir/SoftwareGamer.exe" -Algorithm SHA256).Hash.ToLower()
if ($legacy -ne '5705ce97668de367a5efa1833acdc61ee99178299724e30c4d1d6e13934b6287') { throw 'Unexpected legacy fixture' }

function Run-Checked([string]$exe, [string[]]$arguments) {
    $p = Start-Process $exe -ArgumentList $arguments -PassThru
    if (-not $p.WaitForExit(90000)) { $p.Kill(); throw "Timed out: $exe" }
    if ($p.ExitCode -ne 0) { throw "Failed with exit code $($p.ExitCode): $exe" }
}

Run-Checked 'artifacts/SoftwareGamer-Setup-x64.exe' @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="' + $installDir + '"'),('/LOG="' + $evidence + '/setup.log"'))
$expected = (Get-FileHash artifacts/portable/SoftwareGamer.exe -Algorithm SHA256).Hash
$installed = (Get-FileHash "$installDir/SoftwareGamer.exe" -Algorithm SHA256).Hash
if ($expected -ne $installed) { throw 'Installer did not replace the legacy binary' }
Run-Checked "$installDir/SoftwareGamer.exe" @('--startup-check',('"' + $evidence + '"'))
$startup = Get-Content "$evidence/startup-result.json" | ConvertFrom-Json
if (-not $startup.visible -or -not $startup.administratorToken) { throw 'Installed app did not open with inherited admin token' }
Run-Checked "$installDir/unins000.exe" @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + $evidence + '/uninstall.log"'))
if (Test-Path "$installDir/SoftwareGamer.exe") { throw 'Uninstall did not remove executable' }
@{ upgradeFrom='0.1.0'; binaryMatches=$true; started=$true; uninstalled=$true } | ConvertTo-Json | Set-Content "$evidence/installer-result.json"
