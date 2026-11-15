<#
.SYNOPSIS
    Baut VibeVHS für Linux (self-contained, kein .NET auf dem Server nötig) und kopiert es per SSH auf den Server.
.EXAMPLE
    .\deploy\veroeffentlichen.ps1 -Ziel klaus@liebler.iui.hs-osnabrueck.de
.EXAMPLE
    .\deploy\veroeffentlichen.ps1 -NurBauen
#>
param(
    [string] $Ziel = "",
    [string] $Laufzeit = "linux-x64",
    [switch] $NurBauen
)

$ErrorActionPreference = "Stop"
$wurzel = Split-Path $PSScriptRoot -Parent
$ausgabe = Join-Path $wurzel "deploy\ausgabe\vibevhs"

if (Test-Path $ausgabe) { Remove-Item $ausgabe -Recurse -Force }
dotnet publish (Join-Path $wurzel "src\VibeVHS.Web") -c Release -r $Laufzeit --self-contained true -o $ausgabe
if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen." }

# Entwicklungskonfiguration gehört nicht auf den Server.
Remove-Item (Join-Path $ausgabe "appsettings.Development.json") -ErrorAction SilentlyContinue
Write-Host "Gebaut nach $ausgabe"

if ($NurBauen -or [string]::IsNullOrWhiteSpace($Ziel)) { return }

$archiv = Join-Path $wurzel "deploy\ausgabe\vibevhs.tar.gz"
tar -czf $archiv -C (Split-Path $ausgabe -Parent) vibevhs
scp $archiv "${Ziel}:/tmp/vibevhs.tar.gz"
ssh $Ziel "set -e; sudo systemctl stop vibevhs || true; sudo rm -rf /opt/vibevhs; sudo tar -xzf /tmp/vibevhs.tar.gz -C /opt; sudo chown -R root:root /opt/vibevhs; sudo chmod +x /opt/vibevhs/VibeVHS.Web; sudo systemctl start vibevhs; systemctl --no-pager status vibevhs | head -5"
Write-Host "Ausgerollt auf $Ziel"
