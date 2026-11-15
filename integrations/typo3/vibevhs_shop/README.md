# vibevhs_shop – Kursprogramm und Buchung aus VibeVHS für TYPO3 13 LTS

Composer-Paket `vibevhs/vibevhs-shop` (Extbase/Fluid). Die Extension zeigt das öffentliche Kursprogramm aus VibeVHS
auf der VHS-Website und bindet Warenkorb und Kasse über die VibeVHS-Buchungs-API ein.

**Datenschutz:** In TYPO3 liegen nur öffentliche Kursdaten (keine Personen, auch keine Kursleitungen). Personendaten der
Kasse gehen im Browser direkt an VibeVHS; „Mein Konto“ ist nur ein Link zu VibeVHS.

## Bausteine

| Baustein | Umsetzung |
|---|---|
| Delta-Abgleich | Konsolenbefehl `vibevhs:abgleich` (`Classes/Command/AbgleichCommand.php`), im Scheduler als „Execute console commands“ planbar (z. B. alle 15 Min.; nachts `--voll`). Holt `GET /kurse?geaendertSeit=<letzter Stand>`, je geändertem Kurs das Detail, löscht gemeldete `entfernt`; neuer Stand wird erst nach Erfolg gemerkt. Tabellen `tx_vibevhsshop_domain_model_kurs`, `tx_vibevhsshop_abgleich` |
| Plugin „Kursliste“ | Suche (Titel, Nummer, Beschreibung, Ort) und Fachbereich, serverseitig aus der lokalen Tabelle |
| Plugin „Kursdetail“ | Termine, Orte mit Wegbeschreibung und Merkmalen; JSON-LD nach schema.org/Course mit CourseInstance, Place/PostalAddress, Offer; Buchungsbox `<vhs-kurse ansicht="buchen">` (live: Verfügbarkeit, In den Warenkorb) |
| Plugin „Warenkorb“ | `<vhs-kurse ansicht="kasse">` gegen die API; alternativ Kasse auf der VibeVHS-Kassenseite (`kasse="extern"`) |
| Routing | `Configuration/Routes/Default.yaml`: `/kurse/{kategorie}` (Fachbereich, z. B. `/kurse/fremdsprachen`) und `/kurse/{nummer}-{titel}` (z. B. `/kurse/26h-501-englisch-a2-konversation`) |

## Einrichtung

1. `composer require vibevhs/vibevhs-shop` (Pfad-Repository oder eigenes Paketarchiv), `vendor/bin/typo3 extension:setup`.
2. Site-Set „VibeVHS Kursprogramm und Buchung“ (`vibevhs/shop`) in die Site aufnehmen; Einstellungen:
   `vibevhsShop.vibevhsUrl`, `vibevhsShop.apiUrl`, `vibevhsShop.kontoUrl`, `vibevhsShop.anbieter`.
3. Umgebung für den Abgleich: `VIBEVHS_API_URL=https://…/vibevhs/api/v1/shop`, `VIBEVHS_SPEICHER_PID=<Ordner-ID>`.
4. Routing: in `config/sites/<site>/config.yaml` `imports: - { resource: "EXT:vibevhs_shop/Configuration/Routes/Default.yaml" }`
   und bei Bedarf `limitToPages` auf die Seiten der Plugins setzen.
5. In VibeVHS die Website als Origin erlauben: `Shop:ErlaubteOrigins` (sonst blockiert der Browser die API-Aufrufe der Komponente).
6. Scheduler-Task anlegen: „Execute console commands“ → `vibevhs:abgleich`, Frequenz `*/15 * * * *`.

## Tests

```bash
composer install
composer test
```

Unit-Tests (`Tests/Unit`): Delta-Abgleich gegen echte API-Antworten aus VibeVHS (`Tests/Fixtures`, Stand I9) – erster Lauf,
Folgelauf mit Delta und Löschung, kein neuer Stand nach Abbruch, voller Lauf; schema.org-Daten – Instanz, Ort, Angebot,
ausgefallene Termine, keine Personendaten, sicheres JSON (`</script>`).

> **Status:** Die PHPUnit-Tests sind vorbereitet, aber **nicht ausgeführt** – auf dem Entwicklungs-PC sind weder PHP noch
> Composer installiert (bewusst nichts nachinstalliert). Ebenso ungeprüft: Laufzeit in einer echten TYPO3-13-Instanz
> (TCA, Site-Set, Route-Enhancer, Fluid-Templates).
