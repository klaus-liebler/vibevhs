// Node-RED hinter nginx unter /nodered – diese Einträge in settings.js setzen (Schritt 04 macht das mit Sicherung).
// settings.js liegt meist in ~/.node-red/settings.js des Benutzers, unter dem Node-RED läuft.
module.exports = {
    // Nur lokal lauschen: Port 1880 ist dann von außen nicht mehr erreichbar, nginx leitet /nodered/ weiter.
    uiHost: "127.0.0.1",
    uiPort: process.env.PORT || 1880,

    // Editor und HTTP-Endpunkte unter /nodered (passt zu location /nodered/ in nginx)
    httpAdminRoot: "/nodered",
    httpNodeRoot: "/nodered",

    // Dringend empfohlen, sobald der Editor über das Internet erreichbar ist: Anmeldung für den Editor.
    // Passwort-Hash erzeugen mit:  node-red admin hash-pw
    // adminAuth: {
    //     type: "credentials",
    //     users: [{ username: "srvadmin", password: "<bcrypt-Hash>", permissions: "*" }]
    // },
};
