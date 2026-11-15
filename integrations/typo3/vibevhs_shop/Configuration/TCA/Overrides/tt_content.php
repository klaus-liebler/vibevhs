<?php

declare(strict_types=1);

use TYPO3\CMS\Extbase\Utility\ExtensionUtility;

defined('TYPO3') or die();

foreach (['Kursliste' => 'Kursliste und Suche', 'Kursdetail' => 'Kursdetail', 'Warenkorb' => 'Warenkorb und Kasse'] as $plugin => $titel) {
    ExtensionUtility::registerPlugin('VibevhsShop', $plugin, 'VibeVHS: ' . $titel, 'EXT:vibevhs_shop/Resources/Public/Icons/Extension.svg', 'VibeVHS');
}
