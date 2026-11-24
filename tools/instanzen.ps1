<#
.SYNOPSIS
    Schulungsbetrieb (QA-68): bis zu 20 VibeVHS-Instanzen mit eigener Datenbank und eigener Spielzeit für Schulungen.

.DESCRIPTION
    Jede Instanz ist ein eigener Prozess mit eigener PostgreSQL-Datenbank (Kopie einer Vorlage, in Sekunden). Die Kursleitung steuert alle
    gemeinsam: springen (Datenstand einer Sprungmarke), Tempo, anhalten, weiter, Steuerung der Teilnehmenden sperren.
    Voraussetzung: PostgreSQL-Container läuft (docker compose -f deploy/docker-compose.yml up -d).

    Befehle:
      vorlagen                       Vorlagen aller Sprungmarken bauen (einmal je Softwarestand, einige Minuten)
      starten  [-Anzahl 12] [-Marke ausgangslage] [-Rechner <Name/IP>]   Instanzen i01 … starten (Ports ab 5301)
      liste                          Adressen und Spielzeit aller Instanzen
      springen <marke>               alle (oder -Instanzen i01,i02) zu einer Sprungmarke
      tempo <wert>                   z. B. 1, 0.1 (Zeitlupe), 60 (Zeitraffer)
      anhalten | weiter              Spielzeit anhalten bzw. fortsetzen
      sperren | freigeben            Teilnehmende dürfen die Spielzeit (nicht) selbst steuern
      stoppen                        Prozesse beenden (Datenbanken bleiben)
      entfernen                      Prozesse beenden und Instanzdatenbanken löschen (nur gekennzeichnete)

    Sprungmarken: ausgangslage, mo-1800, di-0800, fr-1300, mo2-0730 (siehe docs/schulungsbetrieb.md).

.EXAMPLE
    .\tools\instanzen.ps1 vorlagen
    .\tools\instanzen.ps1 starten -Anzahl 12
    .\tools\instanzen.ps1 springen di-0800
#>
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('vorlagen', 'starten', 'liste', 'springen', 'tempo', 'anhalten', 'weiter', 'sperren', 'freigeben', 'stoppen', 'entfernen')]
    [string] $Befehl,
    [Parameter(Position = 1)] [string] $Wert,
    [ValidateRange(1, 20)] [int] $Anzahl = 5,
    [string] $Marke = 'ausgangslage',
    [string[]] $Instanzen,
    [string] $Rechner = 'localhost',
    [int] $Basisport = 5301,
    [string] $Container = 'vibevhs-db-1',
    [switch] $NeuVeroeffentlichen
)

$ErrorActionPreference = 'Stop'
$Repo = Split-Path -Parent $PSScriptRoot
$Arbeit = Join-Path $env:TEMP 'vibevhs-schulung'
$Ausgabe = Join-Path $Arbeit 'anwendung'
$Zustand = Join-Path $Arbeit 'instanzen.json'
$Schluesseldatei = Join-Path $Arbeit 'leitstand.txt'
$Basis = if ($env:VIBEVHS_SCHULUNG_DB) { $env:VIBEVHS_SCHULUNG_DB } else { 'Host=127.0.0.1;Port=5441;Username=vibevhs;Password=entwicklung' }
New-Item -ItemType Directory -Force $Arbeit | Out-Null

function Get-Leitstandschluessel {
    if (-not (Test-Path $Schluesseldatei)) {
        $bytes = New-Object byte[] 24; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') | Set-Content -Path $Schluesseldatei -Encoding ascii
    }
    (Get-Content $Schluesseldatei -Raw).Trim()
}

function Invoke-Veroeffentlichen {
    if ($NeuVeroeffentlichen -or -not (Test-Path (Join-Path $Ausgabe 'VibeVHS.Web.dll'))) {
        Write-Host '» Anwendung veröffentlichen …'
        dotnet publish (Join-Path $Repo 'src/VibeVHS.Web/VibeVHS.Web.csproj') -c Release -o $Ausgabe --nologo -v quiet | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'dotnet publish fehlgeschlagen' }
    }
}

function Get-Instanzen {
    if (Test-Path $Zustand) { @((Get-Content $Zustand -Raw | ConvertFrom-Json) | ForEach-Object { $_ }) } else { @() }
}

function Save-Instanzen($liste) { ConvertTo-Json @($liste) -Depth 3 | Set-Content -Path $Zustand -Encoding utf8 }

function Invoke-Psql([string] $sql) {
    docker exec $Container psql -U vibevhs -d postgres -v ON_ERROR_STOP=1 -tAc $sql
    if ($LASTEXITCODE -ne 0) { throw "psql: $sql" }
}

function Start-Anwendung([hashtable] $umgebung, [string] $log, [switch] $Warten) {
    $vorher = @{}
    foreach ($k in $umgebung.Keys) { $vorher[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $umgebung[$k]) }
    try {
        $p = Start-Process dotnet -ArgumentList (Join-Path $Ausgabe 'VibeVHS.Web.dll') -WorkingDirectory $Ausgabe -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $log -RedirectStandardError "$log.err" -Wait:$Warten
    } finally {
        foreach ($k in $vorher.Keys) { [Environment]::SetEnvironmentVariable($k, $vorher[$k]) }
    }
    $p
}

function Get-Gemeinsam([string] $name) {
    $verz = Join-Path $Arbeit $name
    New-Item -ItemType Directory -Force $verz | Out-Null
    @{
        ASPNETCORE_ENVIRONMENT = 'Development'
        Sicherung__Aktiv = 'false'
        Speicher__Dokumente = (Join-Path $verz 'dokumente')
        Speicher__Schluessel = (Join-Path $Arbeit 'schluessel')     # gemeinsam: Cookies je Instanz getrennt (Cookie-Name), Schlüssel gleich
        Speicher__Logs = (Join-Path $verz 'logs')
        Datenbank__BeiAbweichung = 'Abbrechen'
        ConnectionStrings__Kommentare = "$Basis;Database=vibevhs_schulung_kommentare"
        Schulung__Aktiv = 'true'
        Schulung__Schluessel = (Get-Leitstandschluessel)
    }
}

function Invoke-Steuerung([string] $pfad) {
    $schluessel = Get-Leitstandschluessel
    foreach ($i in (Get-Instanzen | Where-Object { -not $Instanzen -or $Instanzen -contains $_.Name })) {
        try {
            $stand = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$($i.Port)/schulung/$pfad" -Headers @{ 'X-Schulung-Schluessel' = $schluessel } -TimeoutSec 120
            '{0}  {1:ddd dd.MM. HH:mm}  Tempo {2}  Marke {3}' -f $i.Name, ([DateTimeOffset]$stand.spielzeit).ToLocalTime(), $stand.tempo, $stand.marke
        } catch {
            Write-Warning "$($i.Name): $($_.Exception.Message)"
        }
    }
}

switch ($Befehl) {
    'vorlagen' {
        Invoke-Veroeffentlichen
        Write-Host '» Vorlagen bauen (Demodaten, dann je Sprungmarke Spielzeit vorlaufen lassen) …'
        $u = Get-Gemeinsam 'vorlagenbau'
        $u.ConnectionStrings__VibeVHS = "$Basis;Database=vibevhs_vorlage_bau"
        $u.Schulung__VorlagenBauen = 'true'
        $u.Demodaten__Anlegen = 'true'
        $u.Demodaten__Massendaten = '500'
        $u.ASPNETCORE_URLS = 'http://127.0.0.1:0'
        $log = Join-Path $Arbeit 'vorlagenbau.log'
        $p = Start-Anwendung $u $log -Warten
        if ($p.ExitCode -ne 0) { Get-Content "$log.err" -Tail 20; throw "Vorlagenbau fehlgeschlagen (Log: $log)" }
        Invoke-Psql "SELECT datname || '  ' || shobj_description(oid, 'pg_database') FROM pg_database WHERE datname LIKE 'vibevhs\_vorlage\_%' ORDER BY datname"
    }
    'starten' {
        Invoke-Veroeffentlichen
        $liste = [System.Collections.ArrayList]@(Get-Instanzen)
        for ($n = 1; $n -le $Anzahl; $n++) {
            $name = 'i{0:00}' -f $n
            if ($liste | Where-Object { $_.Name -eq $name -and (Get-Process -Id $_.Pid -ErrorAction SilentlyContinue) }) { continue }
            $port = $Basisport + $n - 1
            $u = Get-Gemeinsam $name
            $u.ConnectionStrings__VibeVHS = "$Basis;Database=vibevhs_$name"
            $u.Schulung__Instanz = $name
            $u.Schulung__Startmarke = $Marke
            $u.Demodaten__Anlegen = 'false'
            $u.OeffentlicheAdresse = "http://${Rechner}:$port/"
            $u.ASPNETCORE_URLS = if ($Rechner -eq 'localhost') { "http://localhost:$port" } else { "http://0.0.0.0:$port" }
            $p = Start-Anwendung $u (Join-Path $Arbeit "$name.log")
            $liste = [System.Collections.ArrayList]@($liste | Where-Object { $_.Name -ne $name })
            [void]$liste.Add([pscustomobject]@{ Name = $name; Port = $port; Pid = $p.Id; Adresse = "http://${Rechner}:$port/" })
            Write-Host "✓ $name gestartet: http://${Rechner}:$port/"
        }
        Save-Instanzen $liste
    }
    'liste' {
        foreach ($i in Get-Instanzen) {
            $laeuft = [bool](Get-Process -Id $i.Pid -ErrorAction SilentlyContinue)
            '{0}  {1}  {2}' -f $i.Name, $i.Adresse, ($(if ($laeuft) { 'läuft' } else { 'gestoppt' }))
        }
        $schluessel = Get-Leitstandschluessel
        foreach ($i in Get-Instanzen) {
            try {
                $stand = Invoke-RestMethod -Uri "http://127.0.0.1:$($i.Port)/schulung/stand" -Headers @{ 'X-Schulung-Schluessel' = $schluessel } -TimeoutSec 10
                '  {0}: {1:ddd dd.MM. HH:mm}, Tempo {2}, Marke {3}, Teilnehmende steuern: {4}' -f $i.Name, ([DateTimeOffset]$stand.spielzeit).ToLocalTime(), $stand.tempo, $stand.marke, $stand.teilnehmerSteuern
            } catch { }
        }
    }
    'springen' { if (-not $Wert) { throw 'Bitte die Sprungmarke angeben, z. B. di-0800' }; Invoke-Steuerung "springen/$Wert" }
    'tempo' { if (-not $Wert) { throw 'Bitte das Tempo angeben, z. B. 1, 0.1 oder 60' }; Invoke-Steuerung "tempo/$Wert" }
    'anhalten' { Invoke-Steuerung 'anhalten' }
    'weiter' { Invoke-Steuerung 'weiter' }
    'sperren' { Invoke-Steuerung 'sperren' }
    'freigeben' { Invoke-Steuerung 'freigeben' }
    { $_ -in 'stoppen', 'entfernen' } {
        foreach ($i in Get-Instanzen) { Stop-Process -Id $i.Pid -Force -ErrorAction SilentlyContinue; Write-Host "■ $($i.Name) gestoppt" }
        if ($Befehl -eq 'entfernen') {
            $namen = Invoke-Psql "SELECT datname FROM pg_database WHERE shobj_description(oid, 'pg_database') LIKE 'VibeVHS-Schulungsinstanz%'"
            foreach ($db in ($namen -split "`n" | Where-Object { $_.Trim() })) {
                Invoke-Psql "DROP DATABASE IF EXISTS `"$($db.Trim())`" WITH (FORCE)" | Out-Null
                Write-Host "✗ Datenbank $($db.Trim()) entfernt"
            }
            Remove-Item $Zustand -ErrorAction SilentlyContinue
        }
    }
}
