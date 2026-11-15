<?php

declare(strict_types=1);

namespace Vhs\VibevhsShop\Service;

/**
 * Delta-Abgleich: holt nur die seit dem letzten Stand geänderten Kurse (beim ersten Lauf alle), speichert Liste und Detail
 * in eigenen Tabellen und löscht Kurse, die VibeVHS als „entfernt“ meldet. Der neue Stand wird erst nach Erfolg gemerkt,
 * ein abgebrochener Lauf wiederholt sich also beim nächsten Mal.
 */
final class Abgleich
{
    public function __construct(private readonly KursQuelle $quelle, private readonly KursSpeicher $speicher)
    {
    }

    /** @return array{geaendert: int, entfernt: int, stand: string} */
    public function ausfuehren(?int $jetzt = null, bool $voll = false): array
    {
        $seit = $voll ? null : $this->speicher->letzterStand();
        $liste = $this->quelle->kurse($seit);
        $geaendert = 0;
        foreach ($liste['kurse'] as $kurz) {
            $detail = $this->quelle->kurs((string)$kurz['nummer']);
            $this->speicher->speichern(self::zeile($kurz, $detail));
            $geaendert++;
        }

        foreach ($liste['entfernt'] as $nummer) {
            $this->speicher->entfernen((string)$nummer);
        }

        $this->speicher->standMerken((string)$liste['stand'], $geaendert, count($liste['entfernt']), $jetzt ?? time());
        return ['geaendert' => $geaendert, 'entfernt' => count($liste['entfernt']), 'stand' => (string)$liste['stand']];
    }

    /**
     * Spaltenwerte für tx_vibevhsshop_domain_model_kurs. Zeitangaben der API sind Ortszeit (Europe/Berlin).
     *
     * @param array<string, mixed> $kurz
     * @param array<string, mixed> $detail
     * @return array<string, mixed>
     */
    public static function zeile(array $kurz, array $detail): array
    {
        $zeit = static fn (?string $iso): int => $iso === null || $iso === ''
            ? 0
            : (new \DateTimeImmutable($iso, new \DateTimeZone('Europe/Berlin')))->getTimestamp();
        return [
            'nummer' => (string)$kurz['nummer'],
            'titel' => (string)$kurz['titel'],
            'slug' => (string)$kurz['slug'],
            'beschreibung' => (string)($detail['beschreibung'] ?? ''),
            'fachbereich' => (int)$kurz['fachbereich'],
            'fachbereich_name' => (string)$kurz['fachbereichName'],
            'kategorie' => (string)$kurz['fachbereichSlug'],
            'semester' => (string)($kurz['semester'] ?? ''),
            'beginn' => $zeit($kurz['beginn'] ?? null),
            'ende' => $zeit($kurz['ende'] ?? null),
            'termine' => (int)($kurz['termine'] ?? 0),
            'zeiten' => (string)($kurz['zeiten'] ?? ''),
            'ort' => (string)($kurz['ort'] ?? ''),
            'entgelt_cent' => isset($kurz['entgeltCent']) ? (int)$kurz['entgeltCent'] : null,
            'ermaessigt_cent' => isset($kurz['ermaessigtCent']) ? (int)$kurz['ermaessigtCent'] : null,
            'barrierefrei' => !empty($kurz['barrierefrei']) ? 1 : 0,
            'online_buchbar' => !empty($kurz['onlineBuchbar']) ? 1 : 0,
            'geaendert_am' => (string)($kurz['geaendertAm'] ?? ''),
            'detail_json' => json_encode($detail, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE),
        ];
    }
}
