-- Nur öffentliche Kursdaten, keine Personendaten (auch keine Namen von Kursleitungen).
CREATE TABLE tx_vibevhsshop_domain_model_kurs (
    nummer varchar(40) DEFAULT '' NOT NULL,
    titel varchar(255) DEFAULT '' NOT NULL,
    slug varchar(255) DEFAULT '' NOT NULL,
    beschreibung text,
    fachbereich int(11) DEFAULT '0' NOT NULL,
    fachbereich_name varchar(255) DEFAULT '' NOT NULL,
    kategorie varchar(255) DEFAULT '' NOT NULL,
    semester varchar(100) DEFAULT '' NOT NULL,
    beginn int(11) DEFAULT '0' NOT NULL,
    ende int(11) DEFAULT '0' NOT NULL,
    termine int(11) DEFAULT '0' NOT NULL,
    zeiten varchar(255) DEFAULT '' NOT NULL,
    ort varchar(255) DEFAULT '' NOT NULL,
    entgelt_cent int(11) DEFAULT NULL,
    ermaessigt_cent int(11) DEFAULT NULL,
    barrierefrei tinyint(1) unsigned DEFAULT '0' NOT NULL,
    online_buchbar tinyint(1) unsigned DEFAULT '0' NOT NULL,
    geaendert_am varchar(40) DEFAULT '' NOT NULL,
    -- Vollständige Detaildaten der API (Termine, Orte mit Wegbeschreibung, Merkmale) als JSON
    detail_json mediumtext,
    UNIQUE KEY nummer (nummer),
    KEY kategorie (kategorie),
    KEY slug (slug)
);

CREATE TABLE tx_vibevhsshop_abgleich (
    uid int(11) unsigned NOT NULL auto_increment,
    stand varchar(40) DEFAULT '' NOT NULL,
    zeitpunkt int(11) DEFAULT '0' NOT NULL,
    geaendert int(11) DEFAULT '0' NOT NULL,
    entfernt int(11) DEFAULT '0' NOT NULL,
    PRIMARY KEY (uid)
);
