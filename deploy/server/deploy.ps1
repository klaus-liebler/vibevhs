<#
.SYNOPSIS
    VibeVHS auf liebler.iui.hs-osnabrueck.de ausrollen: self-contained Linux-Build, Kit und Archiv hochladen,
    auf dem Server mit sudo einspielen (vorige Version bleibt als Rückweg, Datenbank-Sicherung vorher, Gesundheitsprüfung danach).
.EXAMPLE
    .\deploy\server\deploy.ps1                # bauen + hochladen + einspielen (fragt nach sudo-Passwort)
.EXAMPLE
    .\deploy\server\deploy.ps1 -DbNeu        # wie oben, Datenbank aber beiseitelegen und neu aus Start-/Demodaten aufbauen
                                              # (nötig bei Schemaänderung, solange es keine EF-Migrationen gibt)
.EXAMPLE
    .\deploy\server\deploy.ps1 -NurKit        # nur das Kit nach /tmp/vibevhs-kit hochladen (für die Schritt-Skripte)
.EXAMPLE
    .\deploy\server\deploy.ps1 -NurBauen      # nur bauen (deploy\ausgabe\vibevhs.tar.gz)
#>
param(
    [string] $Ziel = "srvadmin@liebler.iui.hs-osnabrueck.de",
    [string] $Schluessel = "$env:USERPROFILE\.ssh\id_ed25519_liebler_iui_hs_osnabrueck",
    [switch] $NurKit,
    [switch] $NurBauen,
    # Datenbank auf dem Server vor dem Start nach /var/lib/vibevhs/beiseite/ verschieben (nicht löschen); die Anwendung
    # legt sie neu an. Scheitert der Start, kommen vorige Version und vorige Datenbank automatisch zurück.
    [switch] $DbNeu,
    # Rückfrage bei -DbNeu überspringen
    [switch] $OhneRueckfrage
)

$ErrorActionPreference = "Stop"
$wurzel = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$ausgabe = Join-Path $wurzel "deploy\ausgabe"
$sshArgs = @()
if (Test-Path $Schluessel) { $sshArgs = @("-i", $Schluessel) }

function Kit-Hochladen {
    Write-Host "» Kit nach ${Ziel}:/tmp/vibevhs-kit" -ForegroundColor Cyan
    $kit = Join-Path $ausgabe "vibevhs-kit.tar.gz"
    New-Item -ItemType Directory -Force $ausgabe | Out-Null
    # tar statt scp -r: Zielordner wird jedes Mal sauber ersetzt; Zeilenenden bleiben LF (.gitattributes)
    tar -czf $kit -C (Join-Path $wurzel "deploy\server") .
    scp @sshArgs $kit "${Ziel}:/tmp/vibevhs-kit.tar.gz"
    ssh @sshArgs $Ziel "rm -rf /tmp/vibevhs-kit && mkdir -p /tmp/vibevhs-kit && tar -xzf /tmp/vibevhs-kit.tar.gz -C /tmp/vibevhs-kit && rm /tmp/vibevhs-kit.tar.gz && chmod +x /tmp/vibevhs-kit/schritte/*.sh /tmp/vibevhs-kit/werkzeuge/*.sh"
}

if ($DbNeu -and ($NurKit -or $NurBauen)) { throw "-DbNeu wirkt nur beim Einspielen, nicht zusammen mit -NurKit oder -NurBauen." }
if ($DbNeu -and -not $OhneRueckfrage) {
    Write-Host "! -DbNeu: Die Datenbank auf dem Testsystem wird beiseitegelegt und neu aus Start- und Demodaten aufgebaut." -ForegroundColor Yellow
    Write-Host "  Alle dort erfassten Daten sind danach nicht mehr in der Anwendung (Datei bleibt unter /var/lib/vibevhs/beiseite/)." -ForegroundColor Yellow
    if ((Read-Host "  Fortfahren? (j/N)") -notmatch '^(j|ja|y|yes)$') { Write-Host "Abgebrochen – nichts geändert."; return }
}

if ($NurKit) { Kit-Hochladen; return }

Write-Host "» dotnet publish (linux-x64, self-contained)" -ForegroundColor Cyan
$build = Join-Path $ausgabe "vibevhs"
if (Test-Path $build) { Remove-Item $build -Recurse -Force }
dotnet publish (Join-Path $wurzel "src\VibeVHS.Web") -c Release -r linux-x64 --self-contained true -o $build -p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen." }
Remove-Item (Join-Path $build "appsettings.Development.json") -ErrorAction SilentlyContinue
$archiv = Join-Path $ausgabe "vibevhs.tar.gz"
tar -czf $archiv -C $ausgabe vibevhs
Write-Host ("✓ gebaut: {0} ({1:N1} MB)" -f $archiv, ((Get-Item $archiv).Length / 1MB)) -ForegroundColor Green
if ($NurBauen) { return }

Kit-Hochladen
Write-Host "» Archiv hochladen" -ForegroundColor Cyan
scp @sshArgs $archiv "${Ziel}:/tmp/vibevhs.tar.gz"
Write-Host "» einspielen (sudo fragt nach dem Passwort von srvadmin)" -ForegroundColor Cyan
$dbNeuArg = if ($DbNeu) { "--db-neu " } else { "" }
ssh @sshArgs -t $Ziel "sudo bash /tmp/vibevhs-kit/schritte/ausrollen.sh $dbNeuArg/tmp/vibevhs.tar.gz"
if ($LASTEXITCODE -ne 0) { throw "Einspielen fehlgeschlagen – Meldung oben (eine vorhandene Version läuft unverändert weiter)." }
Write-Host "✓ ausgerollt: https://liebler.iui.hs-osnabrueck.de/vibevhs/" -ForegroundColor Green
