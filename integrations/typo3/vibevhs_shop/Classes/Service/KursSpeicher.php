<?php

declare(strict_types=1);

namespace Vhs\VibevhsShop\Service;

/** Lokale Ablage der Kurse und des Abgleichstands (Produktion: Datenbank; Tests: Speicher im Arbeitsspeicher). */
interface KursSpeicher
{
    public function letzterStand(): ?string;

    /** @param array<string, mixed> $zeile Spaltenwerte von tx_vibevhsshop_domain_model_kurs */
    public function speichern(array $zeile): void;

    public function entfernen(string $nummer): void;

    public function standMerken(string $stand, int $geaendert, int $entfernt, int $zeitpunkt): void;
}
