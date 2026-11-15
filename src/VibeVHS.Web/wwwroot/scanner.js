// QR-Scanner mit html5-qrcode (Kamera des Handys oder Laptops). Meldet jeden erkannten Code an die Blazor-Komponente.
window.vhsScanner = {
    start: async function (elementId, dotnet) {
        await window.vhsScanner.stop();
        const scanner = new Html5Qrcode(elementId);
        window._vhsScanner = scanner;
        let letzter = '';
        let zeit = 0;
        await scanner.start({ facingMode: 'environment' }, { fps: 10, qrbox: 240 }, async function (text) {
            const jetzt = Date.now();
            if (text === letzter && jetzt - zeit < 4000) { return; }
            letzter = text;
            zeit = jetzt;
            await dotnet.invokeMethodAsync('Gescannt', text);
        }, function () { });
    },
    stop: async function () {
        const s = window._vhsScanner;
        window._vhsScanner = null;
        if (s) {
            try { await s.stop(); } catch (e) { }
            try { s.clear(); } catch (e) { }
        }
    }
};

// Selbst-Check-in auf der persönlichen Seite: Kamera öffnen, Termin-QR lesen, Formular mit dem Code absenden.
window.vhsSelbstCheckin = async function (leserId, feldId, formularId, knopfId) {
    const knopf = document.getElementById(knopfId);
    if (knopf) { knopf.disabled = true; knopf.textContent = 'Kamera wird geöffnet …'; }
    try {
        const scanner = new Html5Qrcode(leserId);
        await scanner.start({ facingMode: 'environment' }, { fps: 10, qrbox: 240 }, async function (text) {
            try { await scanner.stop(); } catch (e) { }
            document.getElementById(feldId).value = text;
            document.getElementById(formularId).submit();
        }, function () { });
        if (knopf) { knopf.textContent = 'QR-Code am Aushang ins Bild halten …'; }
    } catch (e) {
        if (knopf) { knopf.disabled = false; knopf.textContent = '📷 Einchecken / Check in'; }
        alert('Die Kamera konnte nicht geöffnet werden. Bitte Kamerazugriff erlauben oder bei der Kursleitung melden.\n(' + e + ')');
    }
};