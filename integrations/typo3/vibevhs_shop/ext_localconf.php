<?php

declare(strict_types=1);

use TYPO3\CMS\Extbase\Utility\ExtensionUtility;
use Vhs\VibevhsShop\Controller\KursController;

defined('TYPO3') or die();

// Kursliste mit Suche und Kategorie-Filter, serverseitig gerendert (Daten aus dem lokalen Abgleich, keine API-Abfrage je Seitenaufruf).
ExtensionUtility::configurePlugin('VibevhsShop', 'Kursliste', [KursController::class => 'list'], [], ExtensionUtility::PLUGIN_TYPE_CONTENT_ELEMENT);
// Kursdetail mit schema.org/Course; die Buchungsbox (Verfügbarkeit, Warenkorb) lädt die Web-Komponente direkt von VibeVHS.
ExtensionUtility::configurePlugin('VibevhsShop', 'Kursdetail', [KursController::class => 'show'], [], ExtensionUtility::PLUGIN_TYPE_CONTENT_ELEMENT);
// Warenkorb und Kasse: Web-Komponente gegen die VibeVHS-API (Kasse auf Wunsch auf der VibeVHS-Kassenseite).
ExtensionUtility::configurePlugin('VibevhsShop', 'Warenkorb', [KursController::class => 'warenkorb'], [KursController::class => 'warenkorb'], ExtensionUtility::PLUGIN_TYPE_CONTENT_ELEMENT);
