<?php

declare(strict_types=1);

namespace Vhs\VibevhsShop\Tests\Unit\Service;

use PHPUnit\Framework\Attributes\Test;
use PHPUnit\Framework\TestCase;
use Vhs\VibevhsShop\Service\Abgleich;
use Vhs\VibevhsShop\Service\KursQuelle;
use Vhs\VibevhsShop\Service\KursSpeicher;

/** Delta-Abgleich gegen echte API-Antworten (Fixtures aus VibeVHS, I9). */
final class AbgleichTest extends TestCase
{
    private static function fixture(string $name): array
    {
        return json_decode((string)file_get_contents(__DIR__ . '/../../Fixtures/' . $name), true, 64, JSON_THROW_ON_ERROR);
    }

    private static function quelle(array $liste, ?\Throwable $fehler = null): KursQuelle
    {
        return new class ($liste, $fehler) implements KursQuelle {
            public array $anfragen = [];

            public function __construct(private array $liste, private ?\Throwable $fehler)
            {
            }

            public function kurse(?string $geaendertSeit): array
            {
                $this->anfragen[] = $geaendertSeit;
                return $this->liste;
            }

            public function kurs(string $nummer): array
            {
                if ($this->fehler !== null) {
                    throw $this->fehler;
                }

                return AbgleichTest::detail($nummer);
            }
        };
    }

    public static function detail(string $nummer): array
    {
        $d = self::fixture('kurs-26H-501.json');
        $d['kurs']['nummer'] = $nummer;
        return $d;
    }

    private static function speicher(?string $stand = null): KursSpeicher
    {
        return new class ($stand) implements KursSpeicher {
            public array $kurse = [];
            public array $staende = [];

            public function __construct(private ?string $stand)
            {
            }

            public function letzterStand(): ?string
            {
                return $this->stand;
            }

            public function speichern(array $zeile): void
            {
                $this->kurse[$zeile['nummer']] = $zeile;
            }

            public function entfernen(string $nummer): void
            {
                unset($this->kurse[$nummer]);
            }

            public function standMerken(string $stand, int $geaendert, int $entfernt, int $zeitpunkt): void
            {
                $this->stand = $stand;
                $this->staende[] = [$stand, $geaendert, $entfernt, $zeitpunkt];
            }
        };
    }

    #[Test]
    public function ersterLaufHoltAllesUndMerktDenStand(): void
    {
        $liste = self::fixture('kurse.json');
        $quelle = self::quelle($liste);
        $speicher = self::speicher();

        $ergebnis = (new Abgleich($quelle, $speicher))->ausfuehren(1_731_660_000);

        self::assertSame([null], $quelle->anfragen);
        self::assertSame(1, $ergebnis['geaendert']);
        $zeile = $speicher->kurse['26H-501'];
        self::assertSame('26h-501-englisch-a2-konversation', $zeile['slug']);
        self::assertSame('fremdsprachen', $zeile['kategorie']);
        self::assertSame(9800, $zeile['entgelt_cent']);
        self::assertSame(1, $zeile['barrierefrei']);
        // Ortszeit der API → Unix-Zeit: 29.09.2026 18:00 MESZ = 16:00 UTC
        self::assertSame(gmmktime(16, 0, 0, 9, 29, 2026), $zeile['beginn']);
        $detail = json_decode($zeile['detail_json'], true);
        self::assertSame('A 1.12 Kursraum', $detail['orte'][0]['name']);
        self::assertStringContainsString('Bildungsweg 1', $detail['orte'][0]['wegbeschreibung']);
        self::assertSame([[$liste['stand'], 1, 0, 1_731_660_000]], $speicher->staende);
    }

    #[Test]
    public function folgelaufFragtDeltaUndLoeschtEntfernteKurse(): void
    {
        $speicher = self::speicher('2026-11-15T10:00:00+00:00');
        $speicher->kurse['26H-999'] = ['nummer' => '26H-999'];
        $quelle = self::quelle(['stand' => '2026-11-15T11:00:00+00:00', 'kurse' => [], 'entfernt' => ['26H-999']]);

        $ergebnis = (new Abgleich($quelle, $speicher))->ausfuehren(1);

        self::assertSame(['2026-11-15T10:00:00+00:00'], $quelle->anfragen);
        self::assertSame(['geaendert' => 0, 'entfernt' => 1, 'stand' => '2026-11-15T11:00:00+00:00'], $ergebnis);
        self::assertArrayNotHasKey('26H-999', $speicher->kurse);
    }

    #[Test]
    public function abgebrochenerLaufMerktKeinenNeuenStand(): void
    {
        $speicher = self::speicher('2026-11-15T10:00:00+00:00');
        $quelle = self::quelle(self::fixture('kurse.json'), new \RuntimeException('API nicht erreichbar'));

        try {
            (new Abgleich($quelle, $speicher))->ausfuehren(1);
            self::fail('Ausnahme erwartet');
        } catch (\RuntimeException) {
        }

        self::assertSame([], $speicher->staende);
        self::assertSame('2026-11-15T10:00:00+00:00', $speicher->letzterStand());
    }

    #[Test]
    public function vollerLaufIgnoriertDenStand(): void
    {
        $quelle = self::quelle(['stand' => 'x', 'kurse' => [], 'entfernt' => []]);
        (new Abgleich($quelle, self::speicher('2026-11-15T10:00:00+00:00')))->ausfuehren(1, true);
        self::assertSame([null], $quelle->anfragen);
    }
}
