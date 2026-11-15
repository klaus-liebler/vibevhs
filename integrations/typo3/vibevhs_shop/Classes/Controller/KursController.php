<?php

declare(strict_types=1);

namespace Vhs\VibevhsShop\Controller;

use Psr\Http\Message\ResponseInterface;
use TYPO3\CMS\Core\Page\AssetCollector;
use TYPO3\CMS\Extbase\Mvc\Controller\ActionController;
use Vhs\VibevhsShop\Domain\Model\Kurs;
use Vhs\VibevhsShop\Domain\Repository\KursRepository;
use Vhs\VibevhsShop\Service\SchemaOrg;

/**
 * Kursliste/Suche und Detail werden serverseitig aus den abgeglichenen Daten gerendert (schnell, suchmaschinenfreundlich,
 * ohne JavaScript nutzbar). Verfügbarkeit, Warenkorb und Kasse kommen live von VibeVHS (Web-Komponente vhs-kurse).
 * „Mein Konto“ ist nur ein Link zu VibeVHS – TYPO3 speichert keine Personendaten.
 */
final class KursController extends ActionController
{
    public function __construct(private readonly KursRepository $kurse, private readonly AssetCollector $assets)
    {
    }

    public function listAction(string $kategorie = '', string $suche = ''): ResponseInterface
    {
        $kategorie = $kategorie !== '' ? $kategorie : (string)($this->settings['kategorie'] ?? '');
        $this->view->assignMultiple([
            'kurse' => $this->kurse->suchen($kategorie, $suche),
            'kategorien' => $this->kurse->kategorien(),
            'kategorie' => $kategorie,
            'suche' => $suche,
            'kontoUrl' => $this->einstellung('kontoUrl'),
        ]);
        return $this->htmlResponse();
    }

    public function showAction(?Kurs $kurs = null): ResponseInterface
    {
        if ($kurs === null) {
            return $this->redirect('list');
        }

        $url = $this->uriBuilder->reset()->setCreateAbsoluteUri(true)->uriFor('show', ['kurs' => $kurs]);
        $this->assets->addInlineJavaScript(
            'vibevhs-schema-' . $kurs->getNummer(),
            SchemaOrg::alsJson(SchemaOrg::kurs($kurs->getDetail(), $this->einstellung('anbieter') ?: 'Volkshochschule', $url)),
            ['type' => 'application/ld+json']
        );
        $this->komponenteEinbinden();
        $this->view->assignMultiple([
            'kurs' => $kurs,
            'detail' => $kurs->getDetail(),
            'api' => $this->einstellung('apiUrl'),
            'kontoUrl' => $this->einstellung('kontoUrl'),
        ]);
        return $this->htmlResponse();
    }

    public function warenkorbAction(): ResponseInterface
    {
        $this->komponenteEinbinden();
        $this->view->assignMultiple(['api' => $this->einstellung('apiUrl'), 'kontoUrl' => $this->einstellung('kontoUrl')]);
        return $this->htmlResponse();
    }

    private function komponenteEinbinden(): void
    {
        $basis = rtrim($this->einstellung('vibevhsUrl'), '/');
        if ($basis !== '') {
            $this->assets->addJavaScript('vibevhs-shop', $basis . '/shop/shop.js', ['type' => 'module']);
        }
    }

    private function einstellung(string $name): string
    {
        $site = $this->request->getAttribute('site');
        $wert = $site?->getSettings()->get('vibevhsShop.' . $name);
        return is_string($wert) ? $wert : (string)($this->settings[$name] ?? '');
    }
}
