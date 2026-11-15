using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
    .AddVibeVhsMocks();

var app = builder.Build();

await DatenbankInitialisierung.InitialisierenAsync(app.Services);
if (app.Environment.IsDevelopment())
{
    await Demodaten.AnlegenAsync(app.Services);
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
    return Results.LocalRedirect("/anmelden");
});
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
