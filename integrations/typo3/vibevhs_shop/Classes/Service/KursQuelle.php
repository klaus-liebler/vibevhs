<?php

declare(strict_types=1);

namespace Vhs\VibevhsShop\Service;

/** Liefert öffentliche Kursdaten (Produktion: VibeVHS-Buchungs-API; Tests: Attrappe). */
interface KursQuelle
{
    /**
     * @return array{stand: string, kurse: list<array<string, mixed>>, entfernt: list<string>}
     */
    public function kurse(?string $geaendertSeit): array;

    /** @return array<string, mixed> Detail mit „kurs“, „termine“, „orte“ … */
    public function kurs(string $nummer): array;
}
