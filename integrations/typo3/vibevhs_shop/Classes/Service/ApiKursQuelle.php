<?php

declare(strict_types=1);

namespace Vhs\VibevhsShop\Service;

use TYPO3\CMS\Core\Http\RequestFactory;

/**
 * Liest die VibeVHS-Buchungs-API (/api/v1/shop). Nur lesende Aufrufe: keine Personendaten, kein Arbeitsnachweis nötig.
 * Basis-URL aus der Umgebungsvariable VIBEVHS_API_URL (siehe Configuration/Services.yaml).
 */
final class ApiKursQuelle implements KursQuelle
{
    public function __construct(private readonly RequestFactory $anfragen, private readonly string $basis)
    {
    }

    public function kurse(?string $geaendertSeit): array
    {
        $query = $geaendertSeit !== null && $geaendertSeit !== '' ? '?geaendertSeit=' . rawurlencode($geaendertSeit) : '';
        return $this->holen('/kurse' . $query);
    }

    public function kurs(string $nummer): array
    {
        return $this->holen('/kurse/' . rawurlencode($nummer));
    }

    /** @return array<string, mixed> */
    private function holen(string $pfad): array
    {
        $antwort = $this->anfragen->request(rtrim($this->basis, '/') . $pfad, 'GET', [
            'headers' => ['Accept' => 'application/json'],
            'timeout' => 20,
            'http_errors' => false,
        ]);
        $status = $antwort->getStatusCode();
        if ($status !== 200) {
            throw new \RuntimeException(sprintf('VibeVHS-API %s antwortet mit %d', $pfad, $status), 1731660000);
        }

        return json_decode((string)$antwort->getBody(), true, 64, JSON_THROW_ON_ERROR);
    }
}
