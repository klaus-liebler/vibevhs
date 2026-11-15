using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Protokoll;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Startdaten;
using Radzen;
using Serilog;
using VibeVHS.Adapters.Mocks;
using VibeVHS.Application;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Infrastructure;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Web.Components;
using VibeVHS.Web.Entwicklung;
using VibeVHS.Web.Sicherheit;

var deutsch = CultureInfo.GetCultureInfo("de-DE");
CultureInfo.DefaultThreadCurrentCulture = deutsch;
CultureInfo.DefaultThreadCurrentUICulture = deutsch;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((dienste, log) => log
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(dienste)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: deutsch)
    .WriteTo.File(Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Speicher:Logs"] ?? "logs", "vibevhs-.log"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30, formatProvider: deutsch));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddRadzenComponents();

// Anmeldung: vorerst Mock (Konto + Rolle wählen). Später kommunale Anmeldung per OIDC/SAML.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/anmelden";
        o.AccessDeniedPath = "/anmelden";
        o.Cookie.Name = "vibevhs.anmeldung";
        o.ExpireTimeSpan = TimeSpan.FromHours(10);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<BenutzerKontext>();
builder.Services.AddScoped<IAktuellerBenutzer, AktuellerBenutzer>();
builder.Services.AddScoped<Vorgang>();

var oeffentlicheAdresse = new Uri(builder.Configuration["OeffentlicheAdresse"] ?? "http://localhost:5189/");
builder.Services
    .AddVibeVhsAnwendung(oeffentlicheAdresse)
    .AddVibeVhsInfrastruktur(builder.Configuration, builder.Environment.ContentRootPath)
    .AddVibeVhsMocks(builder.Configuration);

// Schlüssel für Anmelde-Cookies dauerhaft im Datenverzeichnis halten, damit ein Neustart niemanden abmeldet.
builder.Services.AddDataProtection()
    .SetApplicationName("VibeVHS")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath,
        builder.Configuration["Speicher:Schluessel"] ?? "data/schluessel")));

// Betrieb hinter einem Reverse Proxy (nginx/Apache), der TLS übernimmt.
var hinterProxy = builder.Configuration.GetValue("ReverseProxy:Aktiv", false);
if (hinterProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost);
}

var app = builder.Build();

await DatenbankInitialisierung.InitialisierenAsync(app.Services);

// Startdaten der VHS (Briefkopf, Fachbereiche, Benutzerkonten). Idempotent: lädt nur, was noch fehlt.
var startdaten = app.Configuration["Startdaten:Datei"] ?? Path.Combine("Startdaten", "testbenutzer-vhs-egs.json");
startdaten = Path.IsPathRooted(startdaten) ? startdaten : Path.Combine(AppContext.BaseDirectory, startdaten);
if (File.Exists(startdaten))
{
    await using var bereich = app.Services.CreateAsyncScope();
    await bereich.ServiceProvider.GetRequiredService<StartdatenLader>().AusDateiLadenAsync(startdaten);
}
else
{
    app.Logger.LogWarning("Startdaten {Datei} nicht gefunden; es gibt keine Benutzerkonten", startdaten);
}

// Aufbewahrungsfristen (Vorschläge) anlegen, falls die Tabelle leer ist.
await using (var bereich = app.Services.CreateAsyncScope())
{
    await bereich.ServiceProvider.GetRequiredService<VibeVHS.Application.Datenschutz.DatenschutzDienst>().GrundeinstellungenAsync();
}

if (app.Configuration.GetValue("Demodaten:Anlegen", false))
{
    await Demodaten.AnlegenAsync(app.Services);
    await Demodaten.MassendatenAnlegenAsync(app.Services, app.Configuration.GetValue("Demodaten:Massendaten", 0));
    await ProgrammDemodaten.AnlegenAsync(app.Services);
}

if (hinterProxy)
{
    app.UseForwardedHeaders();
}

if (app.Configuration["PathBase"] is { Length: > 1 } pfad)
{
    app.UsePathBase(pfad);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseSerilogRequestLogging();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseRequestLocalization("de-DE");
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapPost("/abmelden", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("~/anmelden");
});
app.MapPost("/rolle-wechseln", async (HttpContext http, [FromForm] string rolle, [FromForm] string? zurueck,
    IAuditProtokoll protokoll, VibeVhsDbContext db) =>
{
    if (!Enum.TryParse<Rolle>(rolle, out var neueRolle)
        || MockAnmeldung.RolleWechseln(http.User, neueRolle, CookieAuthenticationDefaults.AuthenticationScheme) is not { } neu)
    {
        return Results.Forbid();
    }

    await protokoll.ProtokollierenAsync("Anmeldung.RolleGewechselt", $"Aktive Rolle gewechselt zu {neueRolle.Anzeigename()}");
    await db.SaveChangesAsync();
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, neu);

    var ziel = zurueck ?? string.Empty;
    var lokal = !ziel.Contains("//", StringComparison.Ordinal) && !ziel.Contains('\\') && !ziel.Contains(':');
    return Results.LocalRedirect("~/" + (lokal ? ziel.TrimStart('/') : string.Empty));
}).RequireAuthorization();
// Auskunft nach Art. 15 DSGVO als JSON oder PDF (jede Auskunft wird protokolliert).
app.MapGet("/personen/{id:guid}/auskunft.{format}", async (Guid id, string format, HttpContext http,
    VibeVHS.Application.Datenschutz.DatenschutzDienst datenschutz, VibeVHS.Application.Datenschutz.IAuskunftPdf pdf) =>
{
    var rolle = MockAnmeldung.AlsBenutzer(http.User)?.Rolle;
    if (rolle is null or Rolle.Fachbereichsleitung)
    {
        return Results.Forbid();
    }

    var auskunft = await datenschutz.AuskunftAsync(id);
    var datei = $"auskunft-{VibeVHS.Domain.Datenschutz.Pseudonym.Kurz(id)}-{DateTime.Today:yyyy-MM-dd}";
    return format switch
    {
        "pdf" => Results.File(pdf.Erzeugen(auskunft), "application/pdf", datei + ".pdf"),
        "json" => Results.File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(auskunft, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        }), "application/json", datei + ".json"),
        _ => Results.NotFound(),
    };
}).RequireAuthorization();

// Excel-Export der Personenliste mit den aktuellen Filtern. Jeder Export wird protokolliert (DSGVO).
app.MapGet("/personen/export", async (string? suche, string? rolle, string? status, int? fb, bool? dubletten,
    IPersonenExport export, IAuditProtokoll protokoll, VibeVhsDbContext db, TimeProvider uhr) =>
{
    var filter = new PersonenFilter(
        suche,
        Enum.TryParse<Personenrolle>(rolle, out var r) ? r : null,
        Enum.TryParse<Personenstatus>(status, out var s) ? s : null,
        fb,
        dubletten == true);
    var datei = await export.ExcelAsync(filter);
    await protokoll.ProtokollierenAsync("Personen.Export",
        $"Personenliste als Excel exportiert (Suche: {suche ?? "–"}, Rolle: {rolle ?? "–"}, Status: {status ?? "–"}, Fachbereich: {fb?.ToString() ?? "–"})");
    await db.SaveChangesAsync();
    return Results.File(datei, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"personen-{uhr.GetLocalNow():yyyy-MM-dd-HHmm}.xlsx");
}).RequireAuthorization();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
