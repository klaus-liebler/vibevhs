<?php

$EM_CONF[$_EXTKEY] = [
    'title' => 'VibeVHS Kursprogramm und Buchung',
    'description' => 'Kursliste, Suche und Detail (schema.org/Course) aus VibeVHS; Warenkorb und Kasse über die VibeVHS-Buchungs-API.',
    'category' => 'plugin',
    'state' => 'beta',
    'version' => '0.9.0',
    'constraints' => [
        'depends' => ['typo3' => '13.4.0-13.4.99', 'scheduler' => '13.4.0-13.4.99'],
        'conflicts' => [],
        'suggests' => [],
    ],
];
