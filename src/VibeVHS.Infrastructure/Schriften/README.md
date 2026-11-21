# Eingebettete Schriften (PDF)

Alle PDFs (Briefe, Belege, Auskünfte, Kurs-QR-Bögen) verwenden diese Schriften über `Dokumente/Schriften.cs`
(eigener `IFontResolver` für PDFsharp/MigraDoc). Sie sind als Ressource in `VibeVHS.Infrastructure` eingebettet, damit
die PDFs auf jedem System gleich aussehen und unter Linux ohne installierte Schriften funktionieren.

| Schrift | ersetzt (maßgleich) | Schnitte |
|---|---|---|
| Liberation Sans | Arial, Helvetica (und unbekannte Namen) | Regular, Bold, Italic, BoldItalic |
| Liberation Serif | Times New Roman | Regular, Bold, Italic, BoldItalic |
| Liberation Mono | Courier New (u. a. MigraDocs Fehlerschrift) | Regular, Bold, Italic, BoldItalic |

- **Version:** Liberation Fonts 2.1.5
- **Quelle:** https://github.com/liberationfonts/liberation-fonts/releases/tag/2.1.5,
  Archiv `liberation-fonts-ttf-2.1.5.tar.gz` (https://github.com/liberationfonts/liberation-fonts/files/7261482/liberation-fonts-ttf-2.1.5.tar.gz)
- **SHA-256 des Archivs:** `7191c669bf38899f73a2094ed00f7b800553364f90e2637010a69c0e268f25d0`
- **Lizenz:** SIL Open Font License 1.1 (`LICENSE`, unverändert aus dem Archiv). Einbetten und Weitergeben erlaubt;
  die Schriften werden unverändert verwendet und nicht einzeln verkauft.
- Übernommen am 16.11.2026: nur die 12 TTF-Dateien und `LICENSE`.
