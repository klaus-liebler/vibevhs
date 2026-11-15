<?php

declare(strict_types=1);

namespace Vhs\VibevhsShop\Domain\Repository;

use TYPO3\CMS\Extbase\Persistence\Generic\Typo3QuerySettings;
use TYPO3\CMS\Extbase\Persistence\QueryInterface;
use TYPO3\CMS\Extbase\Persistence\QueryResultInterface;
use TYPO3\CMS\Extbase\Persistence\Repository;
use Vhs\VibevhsShop\Domain\Model\Kurs;

/** @extends Repository<Kurs> */
class KursRepository extends Repository
{
    protected $defaultOrderings = ['beginn' => QueryInterface::ORDER_ASCENDING, 'nummer' => QueryInterface::ORDER_ASCENDING];

    public function initializeObject(): void
    {
        // Die Kurse liegen in einem Ordner; Plugins dürfen auf beliebigen Seiten stehen.
        $einstellungen = new Typo3QuerySettings(
            \TYPO3\CMS\Core\Utility\GeneralUtility::makeInstance(\TYPO3\CMS\Core\Context\Context::class),
            \TYPO3\CMS\Core\Utility\GeneralUtility::makeInstance(\TYPO3\CMS\Extbase\Configuration\ConfigurationManagerInterface::class)
        );
        $einstellungen->setRespectStoragePage(false);
        $this->setDefaultQuerySettings($einstellungen);
    }

    /** Suche in Titel, Nummer und Beschreibung (alle Wörter), optional nur eine Kategorie (Fachbereich). */
    public function suchen(?string $kategorie, ?string $suche): QueryResultInterface
    {
        $query = $this->createQuery();
        $bedingungen = [];
        if ($kategorie !== null && $kategorie !== '') {
            $bedingungen[] = $query->equals('kategorie', $kategorie);
        }

        foreach (preg_split('/\s+/', trim((string)$suche), -1, PREG_SPLIT_NO_EMPTY) ?: [] as $wort) {
            $muster = '%' . addcslashes($wort, '%_') . '%';
            $bedingungen[] = $query->logicalOr(
                $query->like('titel', $muster),
                $query->like('nummer', $muster),
                $query->like('beschreibung', $muster),
                $query->like('ort', $muster)
            );
        }

        if ($bedingungen !== []) {
            $query->matching($query->logicalAnd(...$bedingungen));
        }

        return $query->execute();
    }

    /** @return list<array{kategorie: string, name: string}> */
    public function kategorien(): array
    {
        $liste = [];
        foreach ($this->findAll() as $kurs) {
            $liste[$kurs->getKategorie()] = ['kategorie' => $kurs->getKategorie(), 'name' => $kurs->getFachbereichName()];
        }

        ksort($liste);
        return array_values($liste);
    }
}
