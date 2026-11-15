<?php

declare(strict_types=1);

namespace Vhs\VibevhsShop\Tests\Unit\Service;

use PHPUnit\Framework\Attributes\Test;
use PHPUnit\Framework\TestCase;
use Vhs\VibevhsShop\Service\SchemaOrg;

final class SchemaOrgTest extends TestCase
{
    private static function detail(): array
    {
        return json_decode((string)file_get_contents(__DIR__ . '/../../Fixtures/kurs-26H-501.json'), true, 64, JSON_THROW_ON_ERROR);
    }

    #[Test]
    public function kursMitInstanzOrtUndAngebot(): void
    {
        $d = SchemaOrg::kurs(self::detail(), 'vhs Emsdetten-Greven-Saerbeck', 'https://www.vhs.example/kurse/26h-501-englisch-a2-konversation');

        self::assertSame('Course', $d['@type']);
        self::assertSame('26H-501', $d['courseCode']);
        self::assertSame('EducationalOrganization', $d['provider']['@type']);
        self::assertSame('98.00', $d['offers']['price']);
        self::assertSame('EUR', $d['offers']['priceCurrency']);
        $instanz = $d['hasCourseInstance'][0];
        self::assertSame('onsite', $instanz['courseMode']);
        self::assertSame('2026-09-29', $instanz['startDate']);
        self::assertSame('2027-01-12', $instanz['endDate']);
        self::assertSame('PostalAddress', $instanz['location']['address']['@type']);
        self::assertSame('48282', $instanz['location']['address']['postalCode']);
        self::assertContains('Barrierefreier Zugang (DIN 18040)', array_column($instanz['location']['amenityFeature'], 'name'));
        self::assertSame(12, $instanz['courseSchedule']['repeatCount']);
    }

    #[Test]
    public function ausgefalleneTermineZaehlenNicht(): void
    {
        $detail = self::detail();
        $detail['termine'][0]['ausgefallen'] = true;
        $d = SchemaOrg::kurs($detail, 'vhs', 'https://x');
        self::assertSame(11, $d['hasCourseInstance'][0]['courseSchedule']['repeatCount']);
        self::assertSame('2026-10-06', $d['hasCourseInstance'][0]['startDate']);
    }

    #[Test]
    public function keinePersonendatenUndSicheresJson(): void
    {
        $detail = self::detail();
        $detail['beschreibung'] = 'Achtung </script><script>alert(1)</script>';
        $json = SchemaOrg::alsJson(SchemaOrg::kurs($detail, 'vhs', 'https://x'));

        self::assertStringNotContainsString('</script>', $json);
        self::assertStringNotContainsStringIgnoringCase('dozent', $json);
        self::assertStringNotContainsStringIgnoringCase('instructor', $json);
    }

    #[Test]
    public function kostenloserKurs(): void
    {
        $detail = self::detail();
        $detail['kurs']['entgeltCent'] = 0;
        self::assertSame('Free', SchemaOrg::kurs($detail, 'vhs', 'https://x')['offers']['category']);
    }
}
