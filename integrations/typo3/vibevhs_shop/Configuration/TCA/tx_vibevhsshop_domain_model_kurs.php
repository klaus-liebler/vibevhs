<?php

// Kurse kommen ausschließlich aus dem Abgleich mit VibeVHS; im Backend nur lesbar.
return [
    'ctrl' => [
        'title' => 'LLL:EXT:vibevhs_shop/Resources/Private/Language/locallang_db.xlf:kurs',
        'label' => 'titel',
        'label_alt' => 'nummer',
        'label_alt_force' => true,
        'tstamp' => 'tstamp',
        'crdate' => 'crdate',
        'readOnly' => true,
        'default_sortby' => 'beginn',
        'searchFields' => 'nummer,titel,beschreibung,ort',
        'iconfile' => 'EXT:vibevhs_shop/Resources/Public/Icons/Extension.svg',
    ],
    'columns' => [
        'nummer' => ['label' => 'Kursnummer', 'config' => ['type' => 'input', 'readOnly' => true]],
        'titel' => ['label' => 'Titel', 'config' => ['type' => 'input', 'readOnly' => true]],
        'slug' => ['label' => 'URL-Segment', 'config' => ['type' => 'input', 'readOnly' => true]],
        'beschreibung' => ['label' => 'Beschreibung', 'config' => ['type' => 'text', 'readOnly' => true]],
        'fachbereich' => ['label' => 'Fachbereich', 'config' => ['type' => 'number', 'readOnly' => true]],
        'fachbereich_name' => ['label' => 'Fachbereich (Name)', 'config' => ['type' => 'input', 'readOnly' => true]],
        'kategorie' => ['label' => 'Kategorie (URL)', 'config' => ['type' => 'input', 'readOnly' => true]],
        'semester' => ['label' => 'Semester', 'config' => ['type' => 'input', 'readOnly' => true]],
        'beginn' => ['label' => 'Beginn', 'config' => ['type' => 'datetime', 'readOnly' => true]],
        'ende' => ['label' => 'Ende', 'config' => ['type' => 'datetime', 'readOnly' => true]],
        'termine' => ['label' => 'Termine', 'config' => ['type' => 'number', 'readOnly' => true]],
        'zeiten' => ['label' => 'Zeiten', 'config' => ['type' => 'input', 'readOnly' => true]],
        'ort' => ['label' => 'Ort', 'config' => ['type' => 'input', 'readOnly' => true]],
        'entgelt_cent' => ['label' => 'Entgelt (Cent)', 'config' => ['type' => 'number', 'readOnly' => true, 'nullable' => true]],
        'ermaessigt_cent' => ['label' => 'Ermäßigt (Cent)', 'config' => ['type' => 'number', 'readOnly' => true, 'nullable' => true]],
        'barrierefrei' => ['label' => 'Barrierefrei', 'config' => ['type' => 'check', 'readOnly' => true]],
        'online_buchbar' => ['label' => 'Online buchbar', 'config' => ['type' => 'check', 'readOnly' => true]],
        'geaendert_am' => ['label' => 'Geändert in VibeVHS', 'config' => ['type' => 'input', 'readOnly' => true]],
        'detail_json' => ['label' => 'Detaildaten (JSON)', 'config' => ['type' => 'text', 'readOnly' => true]],
    ],
    'types' => [
        '0' => ['showitem' => 'nummer, titel, slug, fachbereich_name, kategorie, semester, beginn, ende, termine, zeiten, ort, entgelt_cent, ermaessigt_cent, barrierefrei, online_buchbar, beschreibung, geaendert_am, detail_json'],
    ],
];
