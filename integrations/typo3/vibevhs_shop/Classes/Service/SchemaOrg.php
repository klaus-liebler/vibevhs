<?php

declare(strict_types=1);

namespace Vhs\VibevhsShop\Service;

/**
 * Strukturierte Daten nach schema.org/Course mit CourseInstance (Termine, Ort, Angebot) für Suchmaschinen.
 * Enthält keine Personendaten (keine Kursleitung).
 */
final class SchemaOrg
{
    /**
     * @param array<string, mixed> $detail Detail der API
     * @return array<string, mixed>
     */
    public static function kurs(array $detail, string $anbieter, string $url): array
    {
        $kurs = $detail['kurs'];
        $orte = $detail['orte'] ?? [];
        $termine = array_values(array_filter($detail['termine'] ?? [], static fn (array $t): bool => empty($t['ausgefallen'])));
        $instanz = [
            '@type' => 'CourseInstance',
            'courseMode' => str_starts_with((string)($kurs['ort'] ?? ''), 'Online') ? 'online' : 'onsite',
            'startDate' => self::datum($termine[0]['beginn'] ?? $kurs['beginn'] ?? null),
            'endDate' => self::datum($termine === [] ? ($kurs['ende'] ?? null) : $termine[count($termine) - 1]['ende']),
        ];
        if ($orte !== []) {
            $ort = $orte[0];
            $instanz['location'] = array_filter([
                '@type' => 'Place',
                'name' => $ort['name'],
                'address' => ($ort['strasse'] ?? null) === null ? null : [
                    '@type' => 'PostalAddress',
                    'streetAddress' => $ort['strasse'],
                    'postalCode' => $ort['postleitzahl'],
                    'addressLocality' => $ort['stadt'],
                    'addressCountry' => 'DE',
                ],
                'geo' => ($ort['breitengrad'] ?? null) === null
                    ? null
                    : ['@type' => 'GeoCoordinates', 'latitude' => $ort['breitengrad'], 'longitude' => $ort['laengengrad']],
                'amenityFeature' => array_map(
                    static fn (string $m): array => ['@type' => 'LocationFeatureSpecification', 'name' => $m, 'value' => true],
                    $ort['merkmale'] ?? []
                ),
            ], static fn ($v): bool => $v !== null && $v !== []);
        }

        if ($termine !== []) {
            $instanz['courseSchedule'] = array_filter([
                '@type' => 'Schedule',
                'repeatCount' => count($termine),
                'repeatFrequency' => count($termine) > 1 ? 'P1W' : null,
            ], static fn ($v): bool => $v !== null);
        }

        $daten = [
            '@context' => 'https://schema.org',
            '@type' => 'Course',
            'name' => $kurs['titel'],
            'courseCode' => $kurs['nummer'],
            'description' => ($detail['beschreibung'] ?? '') !== '' ? $detail['beschreibung'] : $kurs['titel'],
            'url' => $url,
            'inLanguage' => 'de',
            'provider' => ['@type' => 'EducationalOrganization', 'name' => $anbieter],
            'hasCourseInstance' => [$instanz],
        ];
        if (isset($kurs['entgeltCent'])) {
            $daten['offers'] = [
                '@type' => 'Offer',
                'category' => $kurs['entgeltCent'] === 0 ? 'Free' : 'Paid',
                'price' => number_format($kurs['entgeltCent'] / 100, 2, '.', ''),
                'priceCurrency' => 'EUR',
                'url' => $url,
            ];
        }

        return $daten;
    }

    /** @param array<string, mixed> $daten */
    public static function alsJson(array $daten): string
    {
        // JSON_HEX_TAG verhindert ein vorzeitiges </script> in Texten.
        return json_encode($daten, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_HEX_TAG);
    }

    private static function datum(?string $iso): ?string
    {
        return $iso === null || $iso === '' ? null : substr($iso, 0, 10);
    }
}
