<?php

declare(strict_types=1);

namespace Vhs\VibevhsShop\Service;

use TYPO3\CMS\Core\Database\ConnectionPool;

/** Speichert in tx_vibevhsshop_domain_model_kurs (pid aus VIBEVHS_SPEICHER_PID, siehe Configuration/Services.yaml). */
final class DatenbankKursSpeicher implements KursSpeicher
{
    private const KURSE = 'tx_vibevhsshop_domain_model_kurs';
    private const STAND = 'tx_vibevhsshop_abgleich';

    public function __construct(private readonly ConnectionPool $verbindungen, private readonly int $pid)
    {
    }

    public function letzterStand(): ?string
    {
        $stand = $this->verbindungen->getQueryBuilderForTable(self::STAND)
            ->select('stand')->from(self::STAND)->orderBy('uid', 'DESC')->setMaxResults(1)
            ->executeQuery()->fetchOne();
        return $stand === false || $stand === '' ? null : (string)$stand;
    }

    public function speichern(array $zeile): void
    {
        $verbindung = $this->verbindungen->getConnectionForTable(self::KURSE);
        $uid = $verbindung->select(['uid'], self::KURSE, ['nummer' => $zeile['nummer']])->fetchOne();
        $zeile['tstamp'] = time();
        if ($uid === false) {
            $verbindung->insert(self::KURSE, $zeile + ['pid' => $this->pid, 'crdate' => time()]);
        } else {
            $verbindung->update(self::KURSE, $zeile, ['uid' => (int)$uid]);
        }
    }

    public function entfernen(string $nummer): void
    {
        $this->verbindungen->getConnectionForTable(self::KURSE)->delete(self::KURSE, ['nummer' => $nummer]);
    }

    public function standMerken(string $stand, int $geaendert, int $entfernt, int $zeitpunkt): void
    {
        $this->verbindungen->getConnectionForTable(self::STAND)->insert(
            self::STAND,
            ['stand' => $stand, 'geaendert' => $geaendert, 'entfernt' => $entfernt, 'zeitpunkt' => $zeitpunkt]
        );
    }
}
