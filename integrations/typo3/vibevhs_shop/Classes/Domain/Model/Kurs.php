<?php

declare(strict_types=1);

namespace Vhs\VibevhsShop\Domain\Model;

use TYPO3\CMS\Extbase\DomainObject\AbstractEntity;

/** Öffentlicher Kurs aus VibeVHS (nur lesend; gepflegt wird in VibeVHS). */
class Kurs extends AbstractEntity
{
    protected string $nummer = '';
    protected string $titel = '';
    protected string $slug = '';
    protected string $beschreibung = '';
    protected int $fachbereich = 0;
    protected string $fachbereichName = '';
    protected string $kategorie = '';
    protected string $semester = '';
    protected int $beginn = 0;
    protected int $ende = 0;
    protected int $termine = 0;
    protected string $zeiten = '';
    protected string $ort = '';
    protected ?int $entgeltCent = null;
    protected ?int $ermaessigtCent = null;
    protected bool $barrierefrei = false;
    protected bool $onlineBuchbar = false;
    protected string $detailJson = '';

    public function getNummer(): string { return $this->nummer; }
    public function getTitel(): string { return $this->titel; }
    public function getSlug(): string { return $this->slug; }
    public function getBeschreibung(): string { return $this->beschreibung; }
    public function getFachbereich(): int { return $this->fachbereich; }
    public function getFachbereichName(): string { return $this->fachbereichName; }
    public function getKategorie(): string { return $this->kategorie; }
    public function getSemester(): string { return $this->semester; }
    public function getBeginn(): ?\DateTimeImmutable { return $this->beginn > 0 ? (new \DateTimeImmutable('@' . $this->beginn))->setTimezone(new \DateTimeZone('Europe/Berlin')) : null; }
    public function getEnde(): ?\DateTimeImmutable { return $this->ende > 0 ? (new \DateTimeImmutable('@' . $this->ende))->setTimezone(new \DateTimeZone('Europe/Berlin')) : null; }
    public function getTermine(): int { return $this->termine; }
    public function getZeiten(): string { return $this->zeiten; }
    public function getOrt(): string { return $this->ort; }
    public function getEntgelt(): ?float { return $this->entgeltCent === null ? null : $this->entgeltCent / 100; }
    public function getErmaessigt(): ?float { return $this->ermaessigtCent === null ? null : $this->ermaessigtCent / 100; }
    public function isBarrierefrei(): bool { return $this->barrierefrei; }
    public function isOnlineBuchbar(): bool { return $this->onlineBuchbar; }

    /** @return array<string, mixed> Detail der API (Termine, Orte mit Wegbeschreibung und Merkmalen) */
    public function getDetail(): array
    {
        return $this->detailJson === '' ? [] : (json_decode($this->detailJson, true) ?: []);
    }
}
