# Ta bort .NET Framework från lösningen

Umbraco-borttagningen (se [TODO-remove-umbraco.md](TODO-remove-umbraco.md)) är klar. Nästa steg är att
migrera hela lösningen från .NET Framework 4.6.1 (klassisk `System.Web`/IIS-baserad ASP.NET MVC) till
modern .NET.

**Det ursprungliga motivet är plattformsoberoende utveckling:** appen ska kunna byggas, köras och
testas direkt på Linux, så att ingen Windows-VM behövs lokalt och en Linux-devcontainer kan användas
(bl.a. för webbläsartestning, se brytpunkten efter fas 2).

**Driften flyttar med till Linux.** Azure App Service byter från Windows- till Linux-plan. Det var
inget krav, men eftersom allt Windows-låst ändå måste bort för utvecklingsmiljöns skull kostar det
noll extra kodarbete — och det ger dev/drift-paritet, vilket är särskilt värdefullt i just det här
projektet: appen har hittills inte gått att köra alls i utvecklingsmiljön, så allt arbete sedan
Umbraco-borttagningen har verifierats enbart med bygge och testsvit. Med samma OS på båda sidor blir
devcontainern en trogen repetition av produktion.
Som bonus försvinner IIS/ANCM-lagret helt — bara Kestrel bakom App Services front-end.

Kostnaden ligger i infrastrukturen, inte i koden: en befintlig App Service kan inte bytas från Windows
till Linux, så ny plan och ny app måste skapas och inställningar, domän och certifikat flyttas. Se
fas 10.

Det här är ett väsentligt större ingrepp än Umbraco-borttagningen: `System.Web` (HTTP-pipeline, MVC5,
`MembershipProvider`/`RoleProvider`, klassisk SignalR, Razor-vymotorn) finns inte i modern .NET och har
inget drop-in-ersättning — det är en omskrivning av hela webbhosting-lagret, inte bara ett paketbyte.
Många punkter nedan hänger ihop och går inte att göra isolerat (t.ex. går det inte att byta
`Controller`-basklass utan att samtidigt byta hostingmodell).

**Arbetsordning:** allt arbete sker på grenen `remove-dotnet-framework`, där Umbraco-borttagningen redan
ligger (se "Driftläge" i [CLAUDE.md](CLAUDE.md) — `master` är fortfarande den driftsatta
Umbraco-versionen och ska inte röras). Faserna nedan är ordnade så att fas 0a och 0b är fristående och
kan tas en i taget med grönt bygge emellan. Detsamma gäller **fas 1a**, som avsiktligt är utformad för
att sluta i en grön kontrollpunkt. Från **fas 1b** och framåt hänger arbetet ihop och måste byggas upp
i ett svep — räkna inte med att kunna hålla grenen kompilerbar mellan varje enskild punkt där.

Samma testrutin gäller som i Umbraco-listan: lägg till/verifiera characterization-tester för berörd yta
innan den skrivs om, så att tester verifierar beteende (HTTP-anrop/output) snarare än interna
implementationsdetaljer. Notera att stora delar av nuvarande testsvit själv måste skrivas om som en del
av detta arbete — se fas 9.

## Fastställda designbeslut

Dessa är avstämda med användaren 2026-09-09/10 och ska inte omprövas utan ny avstämning:

| Fråga | Beslut |
|---|---|
| Target framework | **`net10.0`** (LTS), men via `net48` som mellanstation — se fas 1a/1b. OBS: bara SDK 9.0.109 finns installerat på maskinen, SDK 10 måste installeras före fas 1b. |
| Lagring | **Ta bort databasen helt.** EF6, SQL Server och de 19 migrationerna ersätts av en fil per order på disk. Motiverat av att datamodellen redan är ett dokumentaggregat och att all sökning sker i Elasticsearch — se fas 7. *(Ersätter tidigare beslut om att behålla EF6; det föll när datamodellen granskades närmare.)* |
| Sökning | **Elasticsearch är oförändrat** och blir efter lagringsbytet den enda vägen för sökning, statistik, bulkdata **och** de få icke-nyckelbaserade uppslagen. |
| Driftmiljö | **Azure App Service för Linux** (webbapp, inte container). Byte från dagens Windows-plan — valt för dev/drift-paritet, se fas 10. Kräver ny App Service-plan; kod­arbetet är identiskt oavsett. |
| Utvecklingsmiljö | **Linux**, direkt och i devcontainer. Samma OS som drift, vilket är hela poängen. |
| Skalning | **Enkelinstans.** Liten app. Ingen SignalR-backplane behövs, inga utskalningsproblem för filbaserad lagring. Dokumenteras som förutsättning så att en framtida utskalning inte tyst bryter funktionalitet. |
| Konfigurationsfiler | `members.json` och `chillinPrevalues.json` läggs upp **manuellt utanför `wwwroot`**, i en katalog som är åtkomlig via Kudu (t.ex. under hemmappen). Sökvägarna görs konfigurerbara. Se fas 10. |
| Mail | **Graph-vägen är den som körs i drift.** EWS (`ExchangeMailWebApi.cs`, `EWS-Api-2.0`) tas bort helt, inte migreras. |
| DbContext-livstid | **Frågan bortfaller** med lagringsbytet ovan. Dagens `Dictionary<threadId, DbContext>` utan låsning försvinner tillsammans med EF6 i stället för att byggas om till scoped DI. |
| Loggning | **log4net behålls**, `Microsoft.Extensions.Logging` väljs bort. Loggfiler på disk, minimal Azure-integration, ingen Application Insights. M.E.L. har ingen inbyggd filprovider, så ett byte hade krävt Serilog/NLog som nytt beroende. Se fas 6. *(2026-09-16)* |
| Isolerad testserver | **Egen Azure App Service på Linux-planet**, helt avskuren: alla integrationer fejkade i processen, även Elasticsearch och Blob Storage. Persistent egen data under `/home/data`. Inga testverktyg i gränssnittet. Se fas 10, "Isolerad testserver". *(2026-09-16)* |

**Arbetsordning härifrån** (avstämd 2026-09-16, ersätter den rena fasföljden):

1. **Fas 6** — hård förutsättning för allt annat: utan `IConfiguration` går appen inte att
   konfigurera i Azure över huvud taget, och lägesomkopplaren nedan går inte att sätta. Se fas 6.
2. **Isolerat läge, steg A** — fejkarna, omkopplaren och dataroten. Oberoende av fas 7. Se den
   punkten vid brytpunkten efter fas 2. Ger klickbar app genom resten av migreringen.
3. **Fas 7** — filbaserad orderlagring.
4. **Isolerat läge, steg B** — sökersättaren och Azure-instansen. Se fas 10.
5. Fas 5 (klientassets), 8, 9, 10 i övrigt, och slutligen 11.

---

## Fas 0a: Latenta defekter upptäckta under granskningen

Dessa är **inte** migreringsarbete — de är befintliga fel i koden, upptäckta när kodbasen inventerades
inför migreringen. Flera är regressioner från Umbraco-borttagningen.

**Ingenting av detta är driftsatt.** Umbraco-borttagningen ligger i sin helhet på grenen
`remove-dotnet-framework`; `master` är fortfarande den driftsatta Umbraco-versionen. Det finns alltså
ingen pågående driftincident här, och punkterna ska **inte** backporteras till `master` — de hör hemma
på migreringsgrenen tillsammans med resten av arbetet.

Att de ändå står först beror på tre saker: de är oberoende av hostingbytet och därför billiga att göra
nu, de är alla sådant som **måste** vara åtgärdat innan driftsättning, och om de ligger kvar när
fas 2–7 påbörjas blir de svåra att skilja från nya fel som migreringen själv introducerar. Flera av dem
är dessutom av samma slag som två fel Umbraco-borttagningen införde och som upptäcktes långt senare
under arbetet — inloggningsskyddet och `~/Views/Partials/`-sökvägen. Båda fångades på grenen, inget
nådde drift, men ingen av dem syntes i bygget eller testsviten: de gick bara att se genom att köra
appen. Det är den egenskapen som gör dem värda att ta tidigt.

- [x] **log4net är helt okonfigurerat sedan Umbraco-borttagningen**
  `Chalmers.ILL/Config/log4net.config` deklarerar sin enda appender som
  `type="Umbraco.Core.Logging.AsynchronousRollingFileAppender, Umbraco.Core"` — en typ i ett paket som
  är borttaget. Dessutom finns **ingen** `[assembly: XmlConfigurator]`-attribut och **inget**
  `XmlConfigurator.Configure()`-anrop någonstans i kodbasen (verifierat: 0 träffar). Under Umbraco var
  det Umbracos boot-sekvens som konfigurerade log4net; den försvann med paketet. Alla
  `log4net.LogManager.GetLogger(...)`-anrop i appen (bl.a. hela felhanteringen i
  `SystemSurfaceController`, `OrderItemsDbContext`, mail-flödena) skriver därför sannolikt ingenstans.
  Åtgärd: byt appender till `log4net.Appender.RollingFileAppender` och lägg till explicit konfiguration
  vid uppstart. Verifiera att en loggfil faktiskt skapas.
  **Prioritera denna högt** — utan fungerande loggning blir resten av migreringen betydligt svårare att
  felsöka, och den avslutande genomtestningen får inga spår att gå på när något beter sig fel.

- [x] **De två maskin-till-maskin-endpointsen blockeras av det globala `[Authorize]`**
  Det globala `AuthorizeAttribute` som lades till i commit 9426bf1 ("Authorization required", 2026-09-01)
  saknar undantag för två controllers som anropas av externa system utan inloggning:
  - `SystemSurfaceController` (`Update`, `SendOutAutomaticMailsThatAreDue`) — anropas av en cron-server.
    Har varken `[Authorize]` eller `[AllowAnonymous]`; dess egen `IsRequestAuthorized()` gör IP-baserad
    kontroll mot `cronServerIpAddress`, men det globala filtret kör *före* och redirectar till login.
    Skulle grenen driftsättas som den är slutar automatiska statusändringar, anonymisering,
    källpollning och utskick av automatmail att köras — tyst, eftersom cron-servern bara får en
    302 till inloggningssidan och inget felmeddelande.
  - `PublicDataSurfaceController` (`GetChillinDataForSierraPatron`) — en dokumenterad publik API
    (se [ILL-status-api.md](ILL-status-api.md)) som sätter `Access-Control-Allow-Origin: *` och anropas
    av bibliotekssystemet. Saknar `[AllowAnonymous]`.

  Åtgärd: `[AllowAnonymous]` på båda, så att deras egna auktoriseringsmekanismer (IP-kontroll respektive
  medveten publik åtkomst) får gälla. Lägg till characterization-tester i `AuthorizationTest.cs` —
  den filen testar idag bara att de fyra *sid*-controllerna saknar `[AllowAnonymous]`, inte att de
  controllers som *ska* vara nåbara faktiskt är det. Ta med båda i den avslutande genomtestningen: de
  är inte klickbara i gränssnittet och missas därför lätt vid manuell avprovning.

- [x] **`PasswordSurfaceController` ignorerar returvärdet från lösenordsbytet**
  `PasswordSurfaceController.cs:43` — `user.ChangePassword(...)` returnerar `bool` som kastas bort, och
  redirect sker till `?success=true` oavsett utfall. Användaren får "lösenordet ändrat" även när det
  inte ändrades. `catch (Exception)` på rad 40-49 sväljer dessutom allt utan loggning.

- [x] **Lösenordsbyte och rollkontroll utgår från en osignerad klientcookie**
  `MemberInfoManager` lagrar login-namnet i cookien `ChalmersILL` utan signering, utan `HttpOnly` och
  utan `Secure`. `PasswordSurfaceController.cs:35` hämtar login-namnet därifrån (inte från
  `User.Identity.Name`) innan lösenordet byts, och vyerna `ChalmersILL.cshtml:32` /
  `ChalmersILLSettingsPage.cshtml:15` gör `Roles.IsUserInRole(Model.CurrentMemberLoginName, ...)` mot
  samma cookievärde. Exploaten är begränsad (lösenordsbytet kräver fortfarande offrets nuvarande
  lösenord via `Membership.ValidateUser`, och `MemberAdminSurfaceController` skyddas av ett riktigt
  `[Authorize(Roles="SuperAdmin")]`), men designen är fel. Byt till `User.Identity.Name`/`User.IsInRole`.
  Görs lämpligen som en del av fas 3, men noteras här eftersom det är ett befintligt fel.

  **Åtgärdat 2026-09-16.** Rollkontrollerna i vyerna använde redan `User.IsInRole` (gjordes som en del
  av fas 3:s `Membership`/`Roles`-migrering). Kvarstod bara `PasswordSurfaceController`, som fortfarande
  hämtade login-namnet via `IMemberInfoManager.GetCurrentMemberLoginName` (den osignerade cookien) i
  stället för `User.Identity.Name`. Fixat, och `IMemberInfoManager`-beroendet togs bort ur controllern
  helt eftersom det inte användes till något annat. Regressionstest tillagt:
  `ChangePassword_UsesAuthenticatedIdentityNotClientCookie_ForLoginName` i
  `PasswordSurfaceControllerTest.cs`, som sätter en avvikande cookie och verifierar att den signerade
  identiteten vinner.

- [x] **`Uri.EscapeUriString` korrumperar cookien vid vissa tecken**
  `MemberInfoManager.cs:53-55` och `:67-69` escapar cookie-subvärden med `Uri.EscapeUriString`. Den
  escapar inte `&`, `=` eller `;` — precis de tecken som avgränsar subvärden i en `System.Web`-cookie.
  Ett `memberText` som innehåller något av dem gör cookien osammanhängande. Metoden är dessutom
  obsolet sedan .NET 5 (`SYSLIB0013`) och kommer att ge byggvarning efter fas 1. Rätt verktyg är
  `Uri.EscapeDataString`. Faller sannolikt bort helt när cookiehanteringen skrivs om i fas 3 — men om
  den överlever dit ska den fixas.

- [x] **Utloggning kräver inloggning**
  `ChalmersILLLogoutPageController` hade varken `[AllowAnonymous]` eller `[Authorize]` och täcktes
  därmed av det globala filtret. En användare vars auth-cookie gått ut men som har kvar den osignerade
  `ChalmersILL`-cookien kunde alltså inte nå utloggningssidan för att rensa den. Avstämt med användaren
  2026-09-10: inte avsiktligt. Åtgärdat med `[AllowAnonymous]`; characterization-test tillagt i
  `AuthorizationTest.cs`.

- [x] **`MemberFileStore` är varken trådsäker eller atomisk, och sväljer läsfel tyst**
  `MemberFileStore.cs` — `Load`/`Save` saknar låsning helt. `FileMembershipProvider.ChangePassword` och
  hela `MemberAdminService` gör `Load → mutera → Save`, vilket är en lost-update-race vid samtidiga
  ändringar. `File.WriteAllText` trunkerar först, så en krasch mitt i skrivningen kan förstöra hela
  användarregistret. `Load` har `catch (Exception) { return new List<MemberAccount>(); }` utan loggning
  — en låst eller korrupt fil ger alltså "alla inloggningar misslyckas" helt utan spår. Åtgärd: lås runt
  läs/skriv, skriv till temporär fil + `File.Replace`, logga fel istället för att svälja dem.

- [x] **`FileRoleProvider` kastar `NullReferenceException` om `Roles` är explicit `null` i JSON**
  `FileRoleProvider.cs` rad 31, 37 och 41 gör `account?.Roles...` — `?.` skyddar mot `account == null`
  men inte mot `Roles == null`. Default-initialiseringen i `MemberAccount.cs:9` gäller bara när nyckeln
  saknas helt, inte när den står som `"Roles": null`.

- [x] **`members.json` saknas fortfarande**
  `Chalmers.ILL/Config/` innehåller bara `members.example.json`. Filen är dessutom inte
  `<Content Include>`-ad i `Chalmers.ILL.csproj`, till skillnad från `chillinPrevalues.json` — det
  läggs till i fas 1a, se detaljer där. Exempelfilens `"see below"`-referens pekade på ingenting (JSON
  stödjer inte kommentarer) — rättat 2026-09-10 till att peka på
  [TODO-remove-umbraco.md](TODO-remove-umbraco.md).
  Att den riktiga produktionsfilen med kontona saknas är inte längre kvar här — det är en
  driftsättningsfråga, se fas 10 (punkten om bootstrap-SuperAdmin-kontot).

---

## Fas 0b: Förarbete (fristående, låg risk)

Allt här är oberoende av hostingbytet och minskar migreringens yta — mest genom att ta bort kod som
annars måste migreras i onödan. Gör detta först. Liksom fas 0a hör det hemma på grenen
`remove-dotnet-framework`, inte på `master`.

- [x] **Ta bort Microsoft Fakes ur testprojektet — betydligt enklare än det ser ut**
  Inventeringen visar att **inga shims faktiskt används**. `ShimsContext.Create()` förekommer 31 gånger
  (16 i `Mail/AutomaticMailSendingEngineTest.cs`, 16 i `OrderItems/BulkDataManagerTest.cs`, 3 i
  `Statistics/StatisticsTest.cs`) men **inget `Shim*`-objekt tilldelas någonsin inuti blocken** — de är
  ren ceremoni och kan raderas rakt av. Det enda som faktiskt används från Fakes är fyra
  auto-genererade *interface*-stubbar: `StubIOrderItemSearcher`, `StubITemplateService`,
  `StubIOrderItemManager`, `StubIMailService`. De ersätts trivialt med handskrivna stubbar — mönstret
  finns redan etablerat i 10 andra testfiler i samma projekt.
  Ta bort: alla fyra `.fakes`-filer, hela `FakesAssemblies/` (som dessutom innehåller en föräldralös
  `Examine.0.1.52.2941.Fakes.dll` utan motsvarande `.fakes`-fil), `Microsoft.QualityTools.Testing.Fakes`-
  referensen och `.fakes`-posterna i `Chalmers.ILL.Tests.csproj`.
  Städa samtidigt bort död testkod: `StatisticsTest.cs:16-22` (`GetFakeSearcher()` anropas aldrig) och
  `BulkDataManagerTest.cs:23-29` (`test1/test2/test3` beräknas men används inte).

- [x] **Ta bort bekräftat död kod**
  Var och en verifierad med sökning över hela kodbasen inklusive tester:
  - `Patron/Sierra.cs` (321 rader) — `ISierra` finns inte, `new Sierra(...)` finns inte, klassen
    registreras inte i `Bootstrapper.cs`. **Bekräftat död.** `SierraModel`/`SierraCache`/
    `SierraConnectionException` är däremot levande och oberoende — rör dem inte.
    Med `Sierra.cs` borta blir `Npgsql` oanvänt (se fas 8).
  - `MediaItems/AzureStorageEmulatorManager.cs` (96 rader) — refereras bara från en utkommenterad rad i
    `OwinStartup.cs:17`. Hårdkodad `C:\Program Files (x86)\...`-sökväg, `WindowsIdentity`/
    `WindowsPrincipal`, fem `Process.Start` mot `WebPICMD`. Windows-only och död.
  - `Views/MacroPartials/` (2 filer) — Umbraco-macro-wrappers som bara gör `Html.RenderPartial` mot
    `Views/Partials/`-varianterna. Ligger inte i någon view-location-format och refereras bara av
    `<Content Include>` i csproj. **Oåtkomliga.** Detta minskar antalet `@inherits`-vyer att migrera
    från 12 till 10.
  - `FakeFolio`-blocket i `Bootstrapper.cs` — den utkommenterade koden på rad 162-170 refererar
    `FakeFolioConnection`, en klass som **inte finns någonstans i kodbasen**; blocket går alltså inte
    ens att avkommentera. Själva `FakeFolio`-klassen (`Bootstrapper.cs:25-111`, 87 rader) är däremot
    *inte* utkommenterad — den kompileras men används bara av den döda koden.
    **Ta bort det utkommenterade blocket, men behåll `FakeFolio`-klassen** och flytta ut den ur
    `Bootstrapper.cs` till en egen fil: den ska återanvändas som FOLIO-fejk i utvecklingsmiljön, se
    punkten om körbarhet utan riktiga integrationer vid brytpunkten efter fas 2. Väljs den vägen bort
    kan klassen tas bort här i stället.
  - `ChalmersILLLoginPage.cshtml:58-60` — laddar `jquery.signalR.min.js` och `/signalr/hubs` men sidan
    laddar aldrig `chalmers.ill.js` och har inga `$.connection`-anrop. Helt oanvänt.
  - `ChalmersILL.cshtml:121-122` — `<!-- SignalR Alert Section (not in use) -->` med en utkommenterad
    `<div class="alerts">`. Sökning på `alerts` i alla `.js`/`.css`/`.cshtml` ger bara den
    utkommenterade raden själv. **Entydigt död**, ta bort.
  - `default.aspx` — en rad, `Inherits="umbraco.UmbracoDefault"`, pekar på en borttagen typ. Fortfarande
    `<Content Include>`-ad i csproj rad 313.
  - `Notifier.SetOrderItemManager` sätter ett `_orderItemManager`-fält som aldrig läses.

- [x] **Ta bort orörda Umbraco-kvarlämningar på disk innan SDK-style-konverteringen**
  Följande mappar under `Chalmers.ILL/` har **noll git-spårade filer** men ligger kvar på disk:
  `Install/`, `Umbraco/`, `Umbraco_Client/`, `App_Plugins/`, `MacroScripts/`, `Masterpages/`, `Xslt/`,
  `App_Browsers/`, `aspnet_client/`, `UserControls/`, `App_Code/`, `Media/`.
  (`UmbracoApi/` och `Templates/` har spårade filer och är **levande appkod** — rör dem inte.
  `App_Data/` innehåller gitignorade runtime-filer.)
  Med SDK-style-projekt inkluderas filer under projektroten implicit av mönster, så dessa måste bort
  **innan** konverteringen, annars blir de plötsligt del av bygget. Samma sak gäller `bower_components/`
  — se fas 5.

- [x] **Ta bort döda `using`-rader som ger falska träffar vid inventering**
  `using Microsoft.Exchange.WebServices.Data;` utan någon EWS-typanvändning i filen:
  `OrderItems/EntityFrameworkOrderItemManager.cs:19`,
  `Controllers/SurfaceControllers/OrderItemResetAllAnonymizationFlagsSurfaceController.cs:3`,
  `Controllers/SurfaceControllers/OrderItemMailSurfaceController.cs:9`,
  `Controllers/SurfaceControllers/OrderItemAnonymizationSurfaceController.cs:3`,
  `Mail/ChalmersOrderItemsMailSource.cs:16`, `Mail/IExchangeMailWebApi.cs:2`,
  `Mail/MicrosoftGraphMailWebApi.cs:16`.
  `using Npgsql;` utan användning: `Controllers/SurfaceControllers/OrderItemPatronDataSurfaceController.cs:4`,
  `Mail/ExchangeMailWebApi.cs:17`.
  `using System.Web.Hosting;` utan användning: `Mail/MailService.cs:10`.
  `using System.Web.Mvc;` i modeller: `Models/Page/ChalmersILLLogoutPageModel.cs:5`,
  `Mail/ChalmersOrderItemsMailSource.cs:8`.

- [x] **Fixa QR-kodens felaktiga MIME-typ och GDI-läcka**
  `OrderItemDeliverySurfaceController.cs:115-126` sparar bilden som `ImageFormat.Png` men bygger
  data-URI:n som `data:image/gif;base64,`. Fungerar via browser-sniffing men är fel. `qrCodeImage`
  (`Bitmap`, `IDisposable`) disposas dessutom aldrig — `using`-blocket omsluter bara `MemoryStream`,
  så varje request läcker ett GDI+-handle. Båda försvinner ändå när renderaren byts i fas 8, men om det
  dröjer är fixen en rad.

---

## Fas 1: Projektfil & target framework

**Fasen är uppdelad i 1a och 1b, och ordningen är viktig.** Byter man TFM till `net10.0` slutar
`System.Web` existera, och då kompilerar ingenting förrän hostinglagret är omskrivet i fas 2. Det ger
en lång sträcka utan vare sig bygge eller tester på någon plattform.

Den sträckan kortas genom att först konvertera projektformatet **utan** att byta TFM. Efter 1a går
`dotnet build` och `dotnet test` att köra, fortfarande på Windows, och testsviten ska vara grön —
vilket bevisar att projektkonverteringen i sig inte bröt något, innan TFM-bytet grumlar bilden.
**1a är den sista gröna kontrollpunkten före fas 2.**

### Fas 1a: SDK-style, behåll `net48`

- [x] **Konvertera båda projekten till SDK-style med `<TargetFramework>net48</TargetFramework>`**
  Nuvarande filer är det gamla verbosa MSBuild-formatet: `ToolsVersion="12.0"`,
  `<TargetFrameworkVersion>v4.6.1</TargetFrameworkVersion>`, 252 `<Compile Include>`, 82
  `<Content Include>`, `packages.config`. Byt till `<Project Sdk="Microsoft.NET.Sdk.Web">` respektive
  `<Project Sdk="Microsoft.NET.Sdk">`, implicit filinkludering och `PackageReference` — men **behåll
  .NET Framework som target** i det här steget.
  `dotnet build` klarar SDK-style-projekt som targetar `net48` på Windows; targeting packs för v4.8
  finns installerade (verifierat under planeringen).
  Höjningen från v4.6.1 till v4.8 är avsiktlig: v4.8 har bäst stöd i SDK-style-världen, och det är
  ändå en mellanstation.

  Detaljer att inte missa i `Chalmers.ILL.csproj`:
  - **Trasig referens:** rad 165-168 pekar på
    `..\packages\Microsoft.Net.Http.2.0.20505.0\lib\net40\System.Net.Http.WebRequest.dll` — paketet
    finns varken i `packages.config` eller i `packages\`-katalogen.
  - **WPF/WinForms-referenser i ett webbprojekt:** `PresentationCore` (150), `PresentationFramework`
    (151), `WindowsBase` (218), `System.Windows.Forms` (213), `System.EnterpriseServices` (216) — ingen
    kodanvändning av något av dem.
  - **Incheckade native-binärer:** `bin\amd64\sqlce*.dll`, `bin\x86\sqlce*.dll` (SQL Server Compact
    4.0), `Microsoft.VC90.CRT\msvcr90.dll` + `.manifest` (rad 221-237, 308-309). Windows-only,
    arkitekturspecifika, ingen SQL CE-användning i koden.
  - **`<Content Include="bin\System.Web.Mvc.dll" />`** (rad 229) — en DLL i `bin/` är incheckad som
    content, och `Chalmers.ILL.Tests.csproj:108-111` refererar den direkt via HintPath med
    `Version=4.0.0.1`, medan huvudprojektet använder paketets 4.0.0.0. Testprojektet bygger alltså mot
    en annan MVC-assembly än appen.
  - **MSBuild-imports att ta bort:** `EntityFramework.props`/`.targets` (rad 3, 743),
    `Microsoft.WebApplication.targets` (rad 712, plus en död kopia med `Condition="false"` på rad 713),
    `EnsureNuGetPackageBuildImports`-targeten (rad 732-738).
  - **Tom `<PostBuildEvent>`** (rad 739-742), källkontrollrester (`<SccProjectName>SAK</SccProjectName>`
    m.fl., rad 23-26), tomma `<Folder Include>` för `Properties\PublishProfiles\`, `Startup\`,
    `Views\Home\` (rad 614-616).
  - **Icke-standardkonfigurationer** `ChillinTest.Release`, `ChalmersILL`, `ChillinTest.Debug`
    (rad 681-710) med `<ExcludeFilesFromDeployment>Local.config</ExcludeFilesFromDeployment>`. Bestäm
    om dessa ska bevaras eller ersättas av `appsettings.{Environment}.json` (se fas 6).
  - **Glöm inte** `<Content Include>` för `Config/members.json` och `Config/members.example.json` —
    saknas idag.

  **Tre icke-uppenbara saker som krävdes för att SDK-style-konverteringen faktiskt skulle bygga**
  (gäller båda csproj, gjort 2026-09-11 — rör inte dessa utan att förstå varför):
  - `Microsoft.NET.Sdk.Web` sätter `OutputType` till `Exe` som default. Måste explicit sättas till
    `<OutputType>Library</OutputType>` i det här steget eftersom `Program.cs`/`Main` inte finns än
    (kommer i fas 2) — annars felar bygget med att ingen entry point hittas.
  - `Microsoft.NET.Sdk.Web` behandlar content-filer som `Content` by default (till skillnad från vanlig
    `Sdk`, som behandlar dem som `None`). Filer som redan täcks av den implicita globben
    (t.ex. `Config/chillinPrevalues.json`) får **inte** ha `<Content Include>` — det ger `NETSDK1022`
    (duplicate item). Använd `<Content Update>` när målet bara är att sätta metadata
    (`CopyToOutputDirectory`) på en redan implicit inkluderad fil.
  - Båda `Properties/AssemblyInfo.cs` finns kvar med manuella `[assembly: AssemblyVersion(...)]` m.fl.
    — SDK:n genererar motsvarande attribut automatiskt, vilket krockar. Krävde
    `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` i båda projekten.

- [x] **Byt testramverk i samma steg — det är det som gör `dotnet test` möjligt**
  MSTest v1 (`Microsoft.VisualStudio.QualityTools.UnitTestFramework`, GAC-referens) byts mot
  `MSTest.TestFramework` + `MSTest.TestAdapter` + `Microsoft.NET.Test.Sdk` som PackageReference.
  **Microsoft Fakes måste vara borttaget först** (fas 0b) — det kräver Visual Studios testprofiler
  och fungerar inte under `dotnet test`. Se fas 9 för detaljerna.
  Ta bort legacy-testprojekt-GUID:t, `<TestProjectType>` och `Microsoft.TestTools.targets`-importen.

- [x] **Uppdatera bygg-/testkommandon i [CLAUDE.md](CLAUDE.md) och `.claude/settings.local.json`**
  MSBuild + `vstest.console.exe` byts mot `dotnet build`/`dotnet test`. Båda `dotnet`-varianterna finns
  redan i vitlistan, så det räcker att ta bort de två gamla raderna och uppdatera CLAUDE.md:s
  Testrutiner-sektion. Görs i samma commit som konverteringen.

- [x] **Kontrollpunkt: kör hela sviten och bekräfta 151 gröna tester**
  Fortfarande på Windows, nu via `dotnet test`. **165 gröna, 0 röda** (2026-09-11) — fler än baseline på
  151 (verifierad 2026-09-10), men avstämt mot källan: `grep -rc "\[TestMethod\]" Chalmers.ILL.Tests`
  ger exakt 165 träffar, dvs. samtliga testmetoder som finns i koden kördes och gick igenom. Ökningen
  förklaras av "More tests."-committen (`3b28120`) som landade efter att 151-baseline togs, inte av
  något som tappades i konverteringen.
  **Committa vid grönt.** Det är den punkt man vill kunna gå tillbaka till. *(Ej gjort här — enligt
  CLAUDE.md:s TODO-lista-regel committar Claude aldrig utan explicit användarbegäran; användaren bör
  committa denna checkpoint manuellt.)*

### Fas 1b: byt till `net10.0`

- [x] **Installera .NET SDK 10**
  Maskinen har idag bara SDK 9.0.109 (runtimes 8.0.19 och 9.0.8). Kan göras när som helst innan
  detta steg — även parallellt med fas 0 och 1a.
  *(Tidigare stod här att EF6 måste verifieras mot `net10.0`. Det behövs inte längre — EF6 tas bort
  helt i fas 7.)*

- [x] **Byt `<TargetFramework>` till `net10.0` i båda projekten**
  **Härifrån är bygget trasigt tills fas 2 är klar.** `System.Web`, `System.Web.Mvc`,
  `System.Web.Helpers` m.fl. finns inte på modern .NET, så i princip varje controller och
  `Global.asax.cs` slutar kompilera samtidigt. Det är väntat och går inte att undvika — men det är
  också skälet till att 1a gjordes först.
  Räkna med att fas 1b och fas 2 utförs i ett svep. Försök inte kryssa av dem var för sig.
  Multitargeting (`net48;net10.0`) är teoretiskt möjligt men avrådes: hostinglagret ska ändå ersättas
  helt, så det skulle innebära `#if`-direktiv genom hela kodbasen till ingen nytta.

  **⚠️ Rättelse upptäckt 2026-09-11, gäller innan detta steg påbörjas:** fas-indelningen nedan är
  tematisk, inte kompileringsbar var för sig — svepet är i praktiken större än fas 1b+2. Tre saker
  till försvinner i samma ögonblick som TFM byts, men är formellt tilldelade *senare* faser:
  - `System.Web.Security.MembershipProvider`/`RoleProvider` (basklasserna för
    `FileMembershipProvider`/`FileRoleProvider`, se fas 3) finns inte i `net10.0`.
  - `System.Web.Mvc.Controller` (basklassen för samtliga 42 controllers, se fas 4) finns inte heller,
    och därmed inte `JsonRequestBehavior`/`[ValidateInput]` som används på ett 90-tal ställen i dem.
  - Klassisk `Microsoft.AspNet.SignalR.*` (`Notifier`/`NotificationHub`, se fas 5) är net45-only och
    går inte att paketreferera mot `net10.0` alls.

  Ett grönt `dotnet build` efter TFM-bytet kräver alltså fas 1b, 2, 3, 4 **och** SignalR-serverdelen
  av fas 5 i samma svep — inte bara 1b+2. (Klient-JS/bower-delen av fas 5 är fristående och kan vänta.)
  Planera om detta steg påbörjas som ett svep över samtliga dessa punkter, inte bara fas 1b+2.

  **Svepet genomfört 2026-09-15.** `dotnet build` och `dotnet test` gröna på `net10.0` (158/158,
  0 fel) för båda projekten. Utöver fas 1b+2+3+4+SignalR-server drogs fyra fas 8-punkter in i förtid
  eftersom paketen fysiskt inte kunde restaureras mot `net10.0` oavsett fasindelning: EWS-Api-2.0,
  WindowsAzure.Storage, System.Drawing (QR-kodrendering) och Npgsql/döda paket/OWIN-MVC5-stacken —
  se respektive ikryssade punkt i fas 8. Unity (fas 6) ersattes direkt med `IServiceCollection`
  eftersom `Microsoft.Practices.Unity` predates netstandard och inte gick att brygga.
  **Inte klart i samma svep** (medvetet avgränsat bort): Web.config-sektionerna (`sessionState`,
  `DbProviderFactories`, request-limits, HTTPS-redirect, static-content-MIME m.m.),
  `[ValidateAntiForgeryToken]` på login/lösenordsbyte, fullständiga pipeline-nivå-auktoriseringstester,
  `ConfigurationManager.AppSettings`/`appsettings.json`-migreringen (fas 6), och hela klient-JS/asset-
  delen av fas 5 (jquery.signalR, bower). Se även den nya observationen i Fas 0a-stil: `Config/log4net.config`
  saknades helt i repot trots att den punkten var ikryssad — skapad nu.
  Två övriga latenta fynd under svepet: `Views/Partials/Chalmers.ILL.LogItem.cshtml` hade fel
  modelltyp (aldrig fångat eftersom klassiska Razor-vyer inte typkontrolleras vid bygge — Core gör
  det), och `Statistics/DefaultStatCalc.cs` hade en tidszonsberoende dagberäkning som gav fel resultat
  på denna maskins tidszon (`America/Los_Angeles`) — båda fixade.

---

## Fas 2: Hosting — System.Web → ASP.NET Core

- [x] **Ersätt `Global.asax`/`Global.asax.cs` **och** `EventHandlers/OwinStartup.cs` med `Program.cs`**
  Det finns **två** startvägar idag, vilket är lätt att missa:
  - `Global.asax.cs` (25 rader, enda event-handlern är `Application_Start`) anropar
    `AreaRegistration.RegisterAllAreas()` (no-op — inga Areas finns, utelämnas helt),
    `FilterConfig.RegisterGlobalFilters`, `ViewEngineConfig.RegisterViewEngines`,
    `RouteConfig.RegisterRoutes`.
  - `EventHandlers/OwinStartup.cs` (klass `Chalmers.ILL.EventHandlers.Startup`, hittas via
    `owin:AppStartup` i Web.config) gör tre saker: ett tomt Azure-emulator-block (död kod, se fas 0b),
    `DbMigrator`-körningen, **`Bootstrapper.Initialise()` på rad 31** — dvs. hela Unity-DI-uppsättningen
    inklusive `DependencyResolver.SetResolver(...)` för MVC — och `app.MapSignalR()` på rad 34.

  **Att MVC:s DI initieras från OWIN och inte från `Application_Start` är den enskilt lättaste raden att
  missa i hela migreringen.** Båda filerna ersätts av en minimal-hosting-`Program.cs`.

- [x] **Migrera det globala `[Authorize]`-filtret**
  `App_Start/FilterConfig.cs:14` lägger till exakt ett filter: `new AuthorizeAttribute()`. I ASP.NET
  Core motsvaras det av `AddAuthorization` + en `AuthorizeFilter` i `MvcOptions.Filters`, eller
  `RequireAuthorization()` på endpoints.

  Undantag som måste bevaras (`[AllowAnonymous]` på klassnivå, inga action-nivå-undantag finns):
  `Page/ChalmersILLLoginPageController`, `LoginSurfaceController`,
  `OrderItemReceivedAtBranchSurfaceController` (QR-kodsflödet). **Plus** de två som ska läggas till
  enligt fas 0a: `SystemSurfaceController` och `PublicDataSurfaceController`.

  Notera också: 23 controllers har dessutom ett redundant explicit `[Authorize]`, och exakt en har
  `[Authorize(Roles = "SuperAdmin")]` (`MemberAdminSurfaceController.cs:12`) — den enda rollbaserade
  serverside-auktoriseringen i hela appen, och helt otestad idag.

- [x] **Migrera `App_Start/RouteConfig.cs` till endpoint-routing**
  Sex registreringar, **och ordningen är semantiskt lastbärande**:
  1. `routes.IgnoreRoute("{resource}.axd/{*pathInfo}")` — `.axd` finns inte i Core, kan utgå
  2. `LegacyUmbracoSurfaceAlias`: `umbraco/surface/{controller}/{action}/{id}`, inga controller/action-
     defaults. **Måste bevaras permanent** — URL:en är inbakad i redan utskrivna, fysiska QR-koder på
     följesedlar (se `OrderItemDeliverySurfaceController.cs`).
  3. `LegacyBestaellningarInstaellningarSlugAlias`: `bestaellningar/instaellningar/{*pathInfo}`
  4. `LegacyBestaellningarSlugAlias`: `bestaellningar/{*pathInfo}`
  5. `LegacyDiskSlugAlias`: `disk/{*pathInfo}`
  6. `Default`: `{controller}/{action}/{id}`, defaults `ChalmersILL`/`Index`

  Punkt 3 **måste** registreras före punkt 4 — annars sväljer orderlistans wildcard
  `/bestaellningar/instaellningar` också (kommentaren finns i `RouteConfig.cs:27-28`). Inga constraints
  finns någonstans. `RoutingTest.cs` täcker alla dessa idag, men måste själv skrivas om (fas 9).

- [x] **Ersätt `App_Start/ViewEngineConfig.cs`**
  Klassnamnet är missvisande — den registrerar ingen view engine, den *muterar* den befintliga
  `RazorViewEngine` genom att lägga `~/Views/Partials/{0}.cshtml` i både `PartialViewLocationFormats`
  och `ViewLocationFormats`. I Core: `services.Configure<RazorViewEngineOptions>(o => { ... })` på
  `ViewLocationFormats`.
  **Omfattningen är större än TODO:n tidigare angav:** `Views/Partials/` innehåller **27** `.cshtml`
  (15 direkt, 7 i `DeliveryType/`, 5 i `Settings/`), och **26 `PartialView("bart-namn")`-anrop** i
  controllers är beroende av patchen. Att missa den bröt i praktiken hela orderhanteringen förra gången
  (se [TODO-remove-umbraco.md](TODO-remove-umbraco.md)).

- [x] **Ersätt `Web.config`s `<system.webServer>`- och `<system.web>`-sektioner**
  Sektion för sektion, det som faktiskt måste porteras:
  - `<requestFiltering><requestLimits maxAllowedContentLength="1073741824">` (bytes) och
    `<httpRuntime maxRequestLength="1048576">` (KB) — **båda är 1 GB**, dvs. konsekventa idag. Ersätts
    av ett enda värde: `Kestrel:Limits:MaxRequestBodySize` (eller `[RequestSizeLimit]` per action).
    Kestrels default är bara **30 MB**, så gränsen måste sättas explicit — annars går uppladdning av
    stora bilagor sönder. Se även fas 10: Azure App Services front-end har en **egen** gräns ovanpå
    Kestrels, så värdet måste provas med en riktig uppladdning.
  - HTTPS-redirect-`<rewrite>`-regeln (undantag för `localhost`) — **ersätts inte av middleware utan
    av App Services `HTTPS Only`-inställning**, se fas 10. `UseHttpsRedirection()` i appen ger
    redirect-loop bakom App Services TLS-terminering om `ForwardedHeaders` inte satts först.
    **Notera befintlig bugg:** regeln har `appendQueryString="false"`, dvs. query strings tappas vid
    HTTP→HTTPS-redirect. Bevara inte det beteendet.
  - `<staticContent>`-MIME-typer (`.air`, `.svg`, `.woff`, `.ttf`, `.otf`, `.eot`) →
    `StaticFileOptions.ContentTypeProvider`. De flesta av dessa är default i Core; verifiera vilka som
    faktiskt behövs (`.air` är det inte).
  - `<remove name="X-Powered-By" />` → egen middleware (headern sätts inte av Kestrel alls, så
    sannolikt inget att göra).
  - `<httpRuntime targetFramework="4.5">` vs `<compilation targetFramework="4.6.1">` — inkonsekvent
    idag, försvinner ändå.
  - `debug="true"`, `customErrors mode="Off"`, `<trace enabled="true">` är **incheckade i
    produktionskonfigurationen**. Ersätt med miljöstyrd `UseDeveloperExceptionPage()`/
    `UseExceptionHandler()`.
  - Följande försvinner utan motsvarighet, inget att portera: `ScriptModule`, `ScriptHandlerFactory`,
    `ScriptResource.axd`, `*.asmx`-handlers (ASP.NET AJAX — ingen ASMX-tjänst finns i projektet),
    `FormsAuthenticationModule`, `ExtensionlessUrlHandler`, `TransferRequestHandler`,
    `<validation validateIntegratedModeConfiguration>`, `<xhtmlConformance>`, `<system.codedom>`,
    hela `<runtime><assemblyBinding>` (17 poster, varav `Microsoft.Owin` och `Microsoft.Owin.Security`
    är dubblerade).
  - `<globalization fileEncoding="UTF-8">` behövs inte i Core — men **BOM-fixen i `.cshtml`-filerna
    måste bevaras** (se Umbraco-TODO:ns mojibake-punkt). Kör inte något verktyg som strippar BOM.

- [x] **Ta bort `<sessionState>` — Session används inte alls**
  Verifierat: **noll** träffar på `Session[...]`, `HttpContext.Current.Session` eller `SessionState` i
  hela `Chalmers.ILL/`. `<sessionState mode="InProc" ...>` i Web.config är död konfiguration.
  Ingen session-middleware ska läggas till i Core — hela frågan om in-memory/Redis/SQL-backend bortfaller.

- [x] **Ta bort `<DbProviderFactories>`**
  Web.config rad 79-86 registrerar `MySql.Data.MySqlClient` (6.6.5.0) och `System.Data.SqlServerCe.4.0`.
  Båda är Umbraco-arv, ingen av dem används i kod, och ingendera finns i modern .NET.

- [x] **Ersätt `Request.ServerVariables`, `Request.Params` och besläktade `System.Web`-API:er**
  Finns inte i ASP.NET Core. Fullständig lista:
  - `Request.ServerVariables` — 6 ställen: `SystemSurfaceController.cs:155, 156, 159, 161`
    (`SERVER_NAME`, `HTTP_X_FORWARDED_FOR`), `Views/ChalmersILL.cshtml:8-10` och
    `Views/ChalmersILLLoginPage.cshtml:7-9` (`SERVER_NAME`, `SERVER_PORT_SECURE` för HTTPS-redirect).
    `SERVER_NAME` → `Request.Host`, `SERVER_PORT_SECURE` → `Request.IsHttps`, `HTTP_X_FORWARDED_FOR` →
    **`ForwardedHeaders`-middleware**, inte manuell header-parsning. Att `SystemSurfaceController`s
    IP-baserade cron-auktorisering hänger på detta gör att den måste verifieras noga.
  - `Request.UserHostAddress` — `SystemSurfaceController.cs:163, 165` →
    `HttpContext.Connection.RemoteIpAddress`
  - `Request.Params` — 5 ställen: `ChalmersILLDiskPageController.cs:28`, `Views/ChalmersILL.cshtml:82,
    126`, `Views/ChalmersILLOrderListPage.cshtml:193`,
    `Views/Partials/DeliveryType/ArticleByMailOrInternalMail.cshtml:67`. Ingen motsvarighet (unionen av
    QueryString+Form+Cookies+ServerVariables) — måste bli explicit `Request.Query[...]`.
  - `Request.QueryString` — 17 ställen (4 i controllers, 13 i vyer) → `Request.Query[...]`
  - `Request.IsAuthenticated` — `Views/ChalmersILL.cshtml:97` → `User.Identity.IsAuthenticated`
  - `Request.Path.StartsWith(string)` — `Views/ChalmersILL.cshtml:75`. I Core är `Request.Path` en
    `PathString`, inte `string` — **kompilerar inte**.
  - `Request.Url.AbsolutePath` — `LoginSurfaceController.cs:44, 49, 51`
  - `HttpUtility` — `EntityFrameworkOrderItemManager.cs:1770, 1773` (`UrlDecode`) och
    `Views/ChalmersILLOrderListPage.cshtml:24` (`ParseQueryString`) → `WebUtility`/`QueryHelpers`
  - `Response.Redirect`/`RedirectPermanent` — `LoginSurfaceController.cs:40, 44, 49`,
    `PasswordSurfaceController.cs:44, 48, 53, 58`, och **i vyer**: `Views/ChalmersILL.cshtml:13`,
    `Views/ChalmersILLLoginPage.cshtml:12`, `Views/ChalmersILLLogoutPage.cshtml:7`. I `System.Web`
    kastar `Response.Redirect` som standard `ThreadAbortException`, vilket
    `LoginSurfaceController.cs:51` (`return Redirect(...)` efter ett `Response.Redirect`) omedvetet
    förlitar sig på — den raden nås aldrig idag. I Core måste alla bli `return Redirect(...)`.
    **Redirect från en vy** (de tre sista) har ingen bra motsvarighet — logiken måste flyttas till
    controllern.

- [x] **Ersätt `HttpContext.Current` med injicerad `IHttpContextAccessor`**
  Exakt 4 anropsställen i 2 filer, alla mönstret `HttpContext.Current?.User?.Identity?.Name`:
  `Members/MemberInfoManager.cs:31, 41, 66` och `OrderItems/EntityFrameworkOrderItemManager.cs:1759`.
  Alla är redan null-säkra. **Men:** `MemberInfoManager` tar samtidigt `HttpRequestBase`/
  `HttpResponseBase` som metodparametrar och läser statiskt från `HttpContext.Current` — blandad
  abstraktionsnivå. Interfacet `IMemberInfoManager` måste ändras, vilket slår igenom i fyra teststubbar
  (fas 9).

- [x] **Ersätt `HttpRuntime.AppDomainAppPath` med `IWebHostEnvironment.ContentRootPath`**
  Två identiska ställen: `Members/MemberFileStore.cs:40-46` (→ `Config/members.json`) och
  `UmbracoApi/ChillinOrderConfiguration.cs:78-84` (→ `Config/chillinPrevalues.json`). Båda har
  `AppDomain.CurrentDomain.BaseDirectory` som fallback.
  **Notera:** `Server.MapPath`/`HostingEnvironment.MapPath` finns inte någonstans i kodbasen — det är
  bara dessa två ställen som gör sökvägsupplösning.

- [x] **Ta bort vestigiell ASP.NET Web API-infrastruktur**
  Inga `ApiController`-klasser finns (0 träffar i hela lösningen). `System.Web.Http` används bara i
  `Bootstrapper.cs:118` — `GlobalConfiguration.Configuration.DependencyResolver = new
  UnityWebApiDependencyResolver(container)` — och den `HttpConfiguration` som skapas där används aldrig
  (ingen `MapHttpRoute` finns). Ta bort raden, `DependencyResolution/UnityWebApiDependencyResolver.cs`
  och alla fyra `Microsoft.AspNet.WebApi*`-paket.

---

## 🔶 Brytpunkt efter fas 2: appen går att köra och titta på i webbläsare

Det här är det första tillfället i hela migreringen då appen faktiskt startar och svarar på HTTP —
`dotnet run` mot Kestrel, i Linux-containern, utan IIS Express och utan Windows-beroenden i
hosting-lagret. **Det är en viktig milstolpe och bör inte passeras utan att utnyttjas.**

Fram till hit har inget i projektet kunnat verifieras annat än via bygge och en testsvit som bevisligen
missar hela den intressanta felklassen. Umbraco-borttagningens fyra allvarligaste fynd — det försvunna
inloggningsskyddet, den trasiga `~/Views/Partials/`-sökvägen, mojibaken och den tomma
statusdropdownen — var alla omedelbart synliga i en webbläsare och alla osynliga för bygget. Fas 3–8
är precis den sträcka där samma sorts fel uppstår igen.

**Slutsatsen är att webbläsartestning inte hör hemma i fas 11, utan här.** Sätts den upp nu blir den en
återkopplingsslinga genom resten av migreringen i stället för en slutbesiktning.

- [x] **Gör Chromium + Puppeteer tillgängligt i Linux-containern**
  Installera Chromium och Puppeteer i utvecklingscontainern så att sidor kan öppnas, klickas och
  skärmdumpas därifrån — både av utvecklare och av Claude, som kan läsa PNG-filer direkt och därmed
  faktiskt *se* resultatet i stället för att gissa utifrån HTML.
  Praktiska noteringar för containern: Chromium behöver `--no-sandbox` (eller en egen användare med
  rätt capabilities) i de flesta containeruppsättningar, och Puppeteers medföljande nedladdning kan
  hoppas över till förmån för distributionens egen Chromium via
  `PUPPETEER_SKIP_CHROMIUM_DOWNLOAD` + `PUPPETEER_EXECUTABLE_PATH`. Skärmdumpar bör hamna i en
  gitignorad mapp.
  Överväg `Microsoft.Playwright` som alternativ — det har en förstklassig .NET-binding och skulle kunna
  köras från det befintliga testprojektet i stället för som separat Node-verktyg. Puppeteer är dock
  fullt tillräckligt för det som efterfrågas här (öppna sida, klicka, skärmdumpa), så välj det som är
  minst friktion i containern.

  **Provade `Microsoft.Playwright` först (2026-09-15)** — resonemanget om att slippa ett separat
  Node-verktyg höll inte i praktiken. `chromium` (apt) fungerar fint: WebSocket-handskakningen mot
  dess CDP-endpoint lyckas, och en rå CDP-navigering (`/json/new?url=...`) fungerar felfritt. Men
  `Microsoft.Playwright`s .NET-drivrutin (dess egen Node-baserade relæprocess) hängde sig på **all**
  riktig navigering — även mot en garanterat nåbar extern URL (`api.github.com`) — oavsett
  `LaunchAsync` vs `ConnectOverCDPAsync`, host-resolver-flaggor, IPv4/IPv6. Bekräftat vara
  drivrutinsspecifikt genom att köra **Puppeteer** (Node) mot samma Chromium-binär och samma
  app-URL: fungerade felfritt direkt, inklusive hela inloggningsflödet.
  **Bytte till Puppeteer** — `browser-check/` (nytt, `package.json` + `screenshot.js`, körs med
  `npm run screenshot`) ersätter `Microsoft.Playwright`-paketet och `BrowserSmokeTest.cs`, båda
  borttagna. `chromium` + `ENV CHROMIUM_EXECUTABLE_PATH` i `.devcontainer/Dockerfile` (krävde en
  rebuild, samma read-only-begränsning som SDK-installationen tidigare — nu genomförd och verifierad).
  Skärmdumpar av login → ifylld → inloggad (Desk-vy) → inställningssidan tagna och skickade till
  användaren, hela flödet verifierat visuellt i en riktig webbläsare.

- [x] **Se till att appen går att starta med så få beroenden som möjligt**
  För att brytpunkten ska vara användbar direkt måste inloggningssidan och startsidan gå att rendera
  utan att hela integrationsfloran är uppe. Kartlägg vad varje sida faktiskt kräver — inloggningssidan
  behöver i praktiken bara `members.json`, medan orderlistan kräver Elasticsearch och orderdetaljerna
  dessutom orderfilerna och `chillinPrevalues.json`. Ett `docker-compose` med Elasticsearch och
  Azurite behövs ändå för utvecklingsmiljön, så det arbetet betalar sig flera gånger om. Någon
  databasmotor behövs inte efter fas 7.

  **Verifierat 2026-09-15** med `dotnet run` + `curl`: `/` redirectar korrekt till
  `/ChalmersILLLoginPage` (302, oautentiserad), login-sidan renderar fullständigt (formulär,
  CSS/JS-referenser, antiforgery-token), `POST HandleLogin` med ett konto i `Config/members.json`
  loggar in korrekt (302 till `/disk/?login=ok` för Desk-roll, auth-cookie + medlemscookies satta),
  och en skyddad sida (`/disk/`) renderar för den inloggade sessionen — allt utan Elasticsearch,
  FOLIO, mail eller databas uppe. Krävde två saker utöver TFM-svepet, båda hårda förutsättningar för
  att nå hit, inte valfria:
  - **`Web.config`s `<appSettings>` läses aldrig av Kestrel** (var alltid IIS/System.Web-specifikt) —
    utan motsvarighet kraschar appen direkt (`ElasticSearchUrl` blir `null`). Löst med `App.config`
    (kopieras till `Chalmers.ILL.dll.config`, samma mekanism `System.Configuration.ConfigurationManager`
    redan använder för testprojektet) — en brygga, inte fas 6:s riktiga `IConfiguration`-migrering.
  - **Skarp DI-bugg**: `LoginSurfaceController` och `PasswordSurfaceController` har två publika
    konstruktorer (en för DI, en för tester). ASP.NET Core kunde inte avgöra vilken den skulle
    använda ("Multiple constructors accepting all given argument types") — kraschade på *varje*
    request, helt osynligt för enhetstester som konstruerar controllern direkt. Fixat med
    `[ActivatorUtilitiesConstructor]`. Exakt den sortens fel brytpunkten finns till för att fånga.
  - Skapade även lokala dev-filer (gitignorade, som avsett): `Config/members.json` (två testkonton,
    lösenord "chillin123") och `Config/chillinPrevalues.json` (status/typ/bibliotek i rätt
    "NN:Etikett"-format).

- [x] **Isolerat läge, steg A: gör appen körbar utan en enda riktig integration**
  Målet är att `dotnet run` ska ge en fungerande app utan ett enda hemligt konfigurationsvärde.
  **Sömmarna finns redan:** samtliga integrationer ligger bakom interface som registreras i
  `Bootstrapper.cs`, så det här är registreringsarbete, inte refaktorering.

  **Omtag 2026-09-16, avstämt med användaren.** Punkten hette tidigare "containrar för det som går,
  fejk för resten" och förutsatte Elasticsearch + Azurite som containrar i devcontainern. Det
  gäller fortfarande som *utvecklarväg*, men användaren vill dessutom ha en **helt isolerad
  testserver som egen Azure App Service** (se fas 10, steg B). En App Service-webbapp kan inte köra
  sidovagnscontainrar, så där måste även ES och Blob Storage ersättas i processen. Beslutet "fejka
  inte Elasticsearch" gäller alltså inte längre villkorslöst — se steg B för avgränsningen.

  **Arbetet delas i två:** steg A (nedan) är oberoende av fas 7 och ska göras direkt efter fas 6.
  Steg B (sökersättaren + Azure-instansen) kräver fas 7 och ligger i fas 10. Skälet att göra steg A
  tidigt är att webbläsarkontrollen då fungerar genom hela fas 7–8 i stället för först efteråt.

  **Omkopplaren.** En enda explicit flagga, `Chillin:Isolated` (bool, default `false`) — inte en
  jämförelse mot miljönamnet. Miljönamnet behövs till annat (`IsDevelopment()` styr felsidan,
  `Program.cs:94`), och testservern ska köra med produktionens felhantering, inte utvecklingslägets.
  Okänt/saknat/felstavat värde ⇒ **inte** isolerat. En fejk som är påslagen utan att någon märker
  det är farligare än ingen fejk alls.

  **Förgreningen måste ligga inuti `Bootstrapper.RegisterTypes`**, inte i `Program.cs`. Två rader
  konstruerar riktiga klienter *eagerly* och får inte köras alls i isolerat läge:
  `Bootstrapper.cs:39-43` (`new ElasticClient(...)` mot `config.ElasticSearchUrl`) och
  `Bootstrapper.cs:58` (`new FolioConnection()`, som läser fyra appSettings i fältinitierare och
  kastar `NullReferenceException` om de saknas). Dela metoden i en gemensam del och två grenar.
  Det är samma ändring som fas 3 pekar ut som blockerare för `WebApplicationFactory`-tester.

  **Samla fejkarna i namnrymden `Chalmers.ILL.Isolated`** (`Chalmers.ILL/Isolated/`). Då blir
  isoleringstestet en *tillåtelselista* — "varje söm ska lösa ut en typ i den namnrymden" — i stället
  för en uppräkning av dagens typer som ruttnar så fort någon lägger till en ny integration.
  Namnge efter backningen som befintlig kod gör: `File*` när fejken har tillstånd på disk, `Fake*`
  när den är tillståndslös.

  Fejkarna som ingår i steg A:
  - **`IMailWebApi`** (7 metoder) → `Isolated/FileMailWebApi.cs`. Utgående mail skrivs som
    `message.json` + `body.html` + bilagor i en `outbox`-mapp — kroppen som separat `.html` gör
    mailen öppningsbara i webbläsaren, vilket är hela poängen. `ReadMailQueue` läser en `inbox`-mapp
    man kan släppa filer i för att simulera inkommande beställningar. **Viktigast av alla fejkar** —
    Graph-vägen skickar mail till låntagare, vidarebefordrar och raderar meddelanden, och det finns
    idag ingen testdubbel alls för `IMailWebApi`, varken i produktionskod eller tester.
    **Bryt samtidigt ut tolkningen av inkommande beställningsmail** ur
    `MicrosoftGraphMailWebApi.cs:156-230` till en ren funktion som både Graph-implementationen och
    fejken anropar. Annars provar man mailflödet utan att prova mailtolkningen, som är den del som
    faktiskt går sönder. Characterization-test först, mot `Chalmers.ILL.Tests/Mail/Data/`.
  - **FOLIO** — `Chalmers.ILL/Services/FakeFolio.cs` implementerar **redan** alla sju interface
    (`IFolioItemService`, `IFolioRepository`, `IFolioService`, `IFolioInstanceService`,
    `IFolioHoldingService`, `IFolioCirculationService`, `IFolioUserService`). Färdigställ i stället
    för att börja om: flytta till `Isolated/`, byt `Console.WriteLine` mot log4net (Console försvinner
    i App Service), fyll i de fyra `Post`-överlagringar som returnerar `null` idag, och skriv den
    `FakeFolioConnection` som aldrig skrevs. Registrera alla åtta.
  - **`IMediaItemManager`** (3 metoder) → `Isolated/FileMediaItemManager.cs`. Nyttolast + en
    `.meta.json` med samma fyra fält som blobmetadatan. `DeleteOlderThan` måste behålla sin semantik
    — den anropas skarpt från `MaintenanceSurfaceController`. **URL-formen måste bevaras exakt:**
    `BlobStorageMediaItemManager.cs:104` bygger `BaseUrl + "umbraco/surface/MediaItemSurface/GetMediaItem/" + Id`,
    beroende av legacy-aliaset i `RouteConfig.cs:15-18`. Bryt ut URL-byggandet till en delad hjälpare
    så att de två implementationerna aldrig kan glida isär.
  - **Patrondata** — `IPatronDataProvider`, `IAffiliationDataProvider`, `IPersonDataProvider`.
    Påhittade låntagare över en gemensam liten tabell; okända nycklar ska ge *icke-träff*, så att
    "låntagaren hittades inte"-vägen går att prova. **Endast konstruerade personnummer** — riktig
    persondata får aldrig läggas i testdatan.
  - **Libris** behövs inte — `LibrisOrderItemsSource` är redan utkommenterad i
    `ChalmersSourceFactory.cs:37` (tjänsten lades ned 2025-09-08).
  - **`ITemplateService`** och **`IChillinTextRepository`** är ES-beroende men har små, enkla frågor
    (`automatic:false`, `id:N`, `nodeName:X`, match_all, exists) — filbaserade implementationer, inte
    via sökersättaren. `ElasticsearchTemplateService` innehåller dock även ren logik
    (`ReplaceMoustaches`, `GetPrettyLibraryNameFromLibraryAbbreviation`, `PopulateTemplateList` med
    `sv-se`-sorteringen) som inte får dupliceras. Bryt ut till en **abstrakt basklass** ovanpå två
    primitiver (`LoadAllTemplates`, `SaveTemplate`) — basklass och inte hjälpklass, därför att
    `ReplaceMoustaches` anropar rekursivt tillbaka in i `GetTemplateData` för `{{T:…}}`-injektion
    (`ElasticsearchTemplateService.cs:178`).

  **Datarot.** Allt tillstånd under **en** konfigurerbar rot, `Chillin:DataPath` — orderfiler
  (fas 7), `members.json`, `chillinPrevalues.json`, media, mail-outbox/inbox, mallar, fritexter,
  loggar. Upplösning: explicit nyckel, annars `$HOME/data` (ger `/home/data` på Linux App Service),
  annars en katalog relativt `ContentRootPath` lokalt. Samordna med fas 10:s beslut om
  `Chillin:MembersFilePath`/`Chillin:PrevaluesFilePath` — behåll de nycklarna, men låt deras default
  härledas ur `Chillin:DataPath`, så att en enda inställning räcker i Azure.
  **Dataroten får aldrig ligga under `ContentRoot` i drift** — deployment skriver över katalogen, och
  vid Run-From-Package är den skrivskyddad.
  `MemberFileStore` är idag statisk och löser sökvägen mot `AppDomain.CurrentDomain.BaseDirectory`
  (`MemberFileStore.cs:71-79`) — måste göras instansbaserad eller få sökvägen injicerad. Behövs för
  fas 10 ändå.

  **Räcken** (alla fyra är billiga och ska med):
  1. De riktiga typerna konstrueras aldrig — förgreningen ovan ger det gratis.
  2. En uppstartskontroll som **kraschar hellre än kör fel**: i isolerat läge ska varje söm lösa ut
     en typ i `Chalmers.ILL.Isolated`, och ingen hemlighetsnyckel får vara ifylld (fångar det
     troligaste verkliga felet — att någon klonar produktionens App Settings till testappen).
     Spegelvänt i `Live`: ingen typ ur `Chalmers.ILL.Isolated` får vara registrerad.
  3. En WARN-ram i loggen vid uppstart som räknar upp varje fejkad söm och var läget lästes ifrån.
     I `Live` loggas en INFO-rad, så att frånvaro av bannern aldrig är tvetydig.
  4. Avstämt med användaren: **ingen banner och inga testverktyg i gränssnittet.** Utkorg och data
     inspekteras som filer via Kudu/SSH. Notera dock att `showManulMailFetchingTools=true` redan
     renderar en chili-ikon som POST:ar `/SystemSurface/Update`
     (`Views/ChalmersILL.cshtml:48-52`) — befintlig funktionalitet, slå på den i stället för att
     bygga något nytt.

  **Fejkarna är första steget i en trappa, inte sista ordet.** Efter det här steget testas mot
  testversioner av FOLIO och Graph, och först därefter mot skarpa tjänster. Fejkarnas uppgift är
  alltså att göra appen körbar och klickbar tidigt — inte att bevisa att integrationerna fungerar.
  Håll dem enkla och uppenbara, och lägg ingen möda på att efterlikna verkliga felsvar; det hör
  hemma i testinstans-steget.
  Gör däremot fejk**datan** medvetet besvärlig — oklassificerade ordrar (`TypeId == -1`), null-fält,
  ordrar i varje status. Det är billigt och provocerar fram samma sorts fel som
  Umbraco-borttagningen orsakade (null `Type`, tom statusdropdown), som alla var renderingsfel snarare
  än integrationsfel och därför syns redan här.

  **Tester som hör till steg A:**
  - Isoleringsbeviset som tillåtelselista (se räcke 2), körd från både test och uppstartskod.
  - Ett test som bevisar att `Bootstrapper.RegisterTypes` lyckas i isolerat läge **utan någon
    konfiguration alls** — precis den regression dagens eagera `ElasticClient`/`FolioConnection`
    orsakar.
  - Ett test som bevisar att isolerat läge inte kan aktiveras i produktionsmiljön.
  - Med fejkarna på plats blir `WebApplicationFactory<Program>`-tester möjliga för första gången.
    `Chalmers.ILL.Tests/Controllers/RoutingTest.cs:108-143` är en färdig mall för in-process-hosting.

  **Genomfört 2026-09-16.** Alla sömmar i listan ovan fejkade, `Bootstrapper.RegisterTypes` uppdelad
  i `RegisterLiveSeams`/`RegisterIsolatedSeams`, ny namnrymd `Chalmers.ILL.Isolated`
  (`Chalmers.ILL/Isolated/`). Genomgång punkt för punkt:
  - **`IMailWebApi` → `FileMailWebApi`.** Outbox (`DataPath/mail/sentitems/{stamp}/message.json` +
    `body.html` + bilagor), inbox (`DataPath/mail/inbox/{id}.html`, droppa en fil för att simulera
    inkommande post), arkiv (`DataPath/mail/archive/{yyyy}/{mm}/`). Mailtolkningen bröts ut till
    `Mail/IncomingMailParser.cs` (ren funktion, delad med `MicrosoftGraphMailWebApi`) **innan** fejken
    skrevs, med characterization-test mot exakt den HTML-form
    `OrderItemMailSurfaceController.SendMailForNewOrder` genererar.
  - **FOLIO.** `Services/FakeFolio.cs` flyttad till `Isolated/FakeFolio.cs`, `Console.WriteLine` →
    log4net, de fyra `Post`-overloaderna bygger nu ett fejkat svarsobjekt från indata istället för
    `null`. Ny `Isolated/FakeFolioConnection.cs` (konstant token, inga appSettings-beroenden). Alla
    åtta interface registrerade.
  - **`IMediaItemManager` → `FileMediaItemManager`.** Nyttolast + `.meta.json` (name/orderItemNodeId/
    createDate/contentType — samma fyra fält som blob-metadatan) under `DataPath/media/`.
    URL-byggandet brutet ut till `MediaItems/MediaItemUrlBuilder.cs`, delad med
    `BlobStorageMediaItemManager` så de två implementationerna inte kan glida isär.
  - **Patrondata** — en enda `Isolated/FilePatronDataProvider.cs` för alla tre interface
    (`IPatronDataProvider`/`IAffiliationDataProvider`/`IPersonDataProvider`), fyra påhittade låntagare
    över en gemensam tabell (konstruerade personnummer i `19000101-000N`-mönster, aldrig riktig
    persondata) — en blockerad, en inaktiv, en oklassificerad, en normal.
  - **`ITemplateService`/`IChillinTextRepository`.** Ny abstrakt basklass
    `Templates/TemplateServiceBase.cs` med två primitiver (`LoadAllTemplates`/`SaveTemplate`) —
    `ElasticsearchTemplateService` och nya `Isolated/FileTemplateService` (en JSON-fil per mall under
    `DataPath/templates/`) ärver den. `ReplaceMoustaches`/`GetPrettyLibraryNameFromLibraryAbbreviation`/
    `PopulateTemplateList` m.fl. flyttade till basklassen oförändrade. `IChillinTextRepository` fick en
    enklare fil-fejk (`Isolated/FileChillinTextRepository.cs`, en JSON-fil) utan gemensam basklass —
    ingen delad logik att bryta ut.
  - **Elasticsearch/`IOrderItemSearcher` — avstämt med användaren 2026-09-16 (se AskUserQuestion i
    sessionen).** TODO-texten krävde att `new ElasticClient(...)` aldrig får köras i isolerat läge,
    men `EntityFrameworkOrderItemManager` m.fl. singletons i `RegisterTypes` måste ändå ha en
    konkret `IOrderItemSearcher` vid Bootstrap-tid (fas 7 har inte tagit bort den kopplingen än), och
    steg B (den riktiga sökersättaren) är explicit inte del av steg A. Löst med en minimal
    `Isolated/NullOrderItemSearcher` — tomma sökresultat, no-op `Added`/`Modified`/`Deleted`. Inte
    steg B:s sökersättare, bara det som krävs för att Bootstrap inte ska krascha. Orderlistan är tom
    i isolerat läge tills steg B; docker-compose-Elasticsearch är fortfarande utvecklarvägen för att
    faktiskt prova orderlistan.
  - **Datarot.** `IChillinConfiguration.DataPath` (explicit `Chillin:DataPath`, annars `$HOME/data`,
    annars en katalog bredvid `ContentRootPath`) och `.Isolated` (bool, default false). `MemberFileStore`
    och `ChillinOrderConfiguration` läser nu `members.json`/`chillinPrevalues.json` under `DataPath`
    istället för `AppDomain.CurrentDomain.BaseDirectory` — `Config/members.json`/`chillinPrevalues.json`
    är inte längre läsvägen (borttagna `<Content Update>`-poster i csproj; `.example.json`-mallarna
    ligger kvar som referens). De gitignorade lokala dev-filerna flyttades till `$HOME/data`, verifierat
    med en full `dotnet run` att login-flödet fortfarande fungerar identiskt mot den nya platsen.
    **Medvetet inte gjort:** loggfilens sökväg (`Config/log4net.config`s `App_Data/Logs/...`) flyttades
    **inte** under `DataPath` — inte del av fejklistan ovan, och log4nets `${}`-variabelsubstitution
    hade krävt en egen, oprövad ändring. Tas upp igen i fas 10 om Kudu/SSH-åtkomst till loggar kräver
    det.
  - **Räckena.** (1) Riktiga typer konstrueras aldrig i isolerat läge — verifierat av (2).
    (2) `Isolated/IsolationGuard.cs`, en tillåtelselista över 16 söm-interface, körd både från
    `Bootstrapper.RegisterTypes` (kraschar vid fel) och från tester. Hemlighetskontrollen känner igen
    `"******"`/`"xxx"` som etablerade platshållare (fas 6) — bara ett värde som inte matchar någon av
    dem räknas som en riktig hemlighet. (3) WARN-ram i loggen (`log4net`, inte konsolen — verifierat
    manuellt i `App_Data/Logs/ChillinTraceLog.txt`) som räknar upp varje fejkad söm; INFO-rad i Live.
    (4) Ingen banner/inga testverktyg i gränssnittet — inget nytt byggt här.
  - **"Isolerat läge kan inte aktiveras i produktionsmiljön"** — avstämt med användaren att detta INTE
    ska vara en miljönamnsjämförelse (den isolerade testservern i fas 10 kör medvetet med
    produktionens felhantering samtidigt som `Isolated=true`). Testat istället som "isolerat läge med
    ett riktigt hemlighetsvärde ifyllt kraschar" (`BootstrapperIsolationTest`), vilket är den faktiska
    skyddsmekanismen räcke 2 ger: om produktionens App Settings klonas till en isolerad app upptäcks
    det, oavsett miljönamn.
  - **Tester tillagda:** `BootstrapperIsolationTest` (4 st — tillåtelselistan i båda riktningarna,
    nollkonfiguration i isolerat läge, riktig hemlighet kraschar, platshållare kraschar inte),
    `IncomingMailParserTest` (4 st), `Controllers/IsolatedModeSmokeTest.cs` — första
    `WebApplicationFactory<Program>`-testet i projektet (`Microsoft.AspNetCore.Mvc.Testing` tillagt,
    `Program`-klassen gjord `public partial` för att vara synlig för testprojektet), 2 st: root
    redirectar till login (302), login-sidan renderar (200), i isolerat läge utan en enda konfigurerad
    hemlighet. 180/180 gröna (var 164 vid fas 6:s slut, +10 i en tidigare commit samma dag för
    Isolated/DataPath-konfigurationen och mailparsningsutbrytningen, +6 här).
  - **Verifierat manuellt med `dotnet run`, både lägen:** Live (`Chillin:Isolated=false`, dagens
    `appsettings.Development.json`) — oförändrat beteende, INFO-rad loggad. Isolerat
    (`Chillin__Isolated=true`, `Chillin__DataPath` pekat på en scratch-katalog, **inga andra
    miljövariabler satta**) — appen startar, `/` redirectar till login (302), login-sidan renderar
    (200), WARN-ramen loggas med alla åtta fejkade sömmarna uppräknade. Inloggning med de befintliga
    dev-kontona prövades men prövade lösenord matchade inte de lagrade hashen (orelaterat till denna
    ändring — samma `FileMembershipProvider`/`members.json`-mekanism som redan fanns; inte utrett
    vidare eftersom `LoginSurfaceControllerTest` redan täcker den lyckade inloggningsvägen med stubbar).
  - **Medvetet inte gjort här** (separata TODO-punkter): testdata-punkten nedan och "använd
    brytpunkten löpande"-punkten är egna, ofristående uppgifter.

- [ ] **⚠️ Ordna testdata för utvecklingsmiljön — saknas helt i planen i övrigt**
  Ingen annan punkt i den här listan säger var *innehållet* ska komma ifrån, och utan det är
  webbläsartestning nästan värdelös: en tom orderlista visar inte om orderhanteringen fungerar, och
  flera av de fel som Umbraco-borttagningen redan orsakat (null `Type`, tom statusdropdown,
  felaktig statusprefix-hantering) syntes bara med verkliga orderposter i verkliga statusar.
  Behövs:
  - **Orderfiler.** En anonymiserad uppsättning, framtagen med migreringsverktyget från fas 7 kört
    mot en kopia av driftdatabasen (appen har redan `AnonymizeOrder`). Alternativt ett seed-skript
    som skapar ordrar i varje status (`01:`–`17:`) och av varje typ (bok/artikel/inköpsförslag).
    Efter fas 7 är testdata bara en katalog med JSON-filer, vilket gör den lätt att checka in,
    dela och återställa mellan testkörningar — en påtaglig förenkling jämfört med en databasdump.
  - **Elasticsearch-index.** Orderlistan läser från ES, inte från lagringen — så en återindexering
    från orderfilerna måste gå att köra i utvecklingsmiljön. **Kontrollerat 2026-09-16: ingen sådan
    väg finns.** `MaintenanceSurfaceController` har bara `RunMaintenanceJobs` (raderar gamla
    mediafiler), och `BulkDataManager` läser bara. Lägg till en `RebuildSearchIndex` som går igenom
    orderfilerna och anropar `IOrderItemSearcher.Added`. Den behövs ändå i drift, som
    återställningsväg om indexet tappas — och den är obligatorisk efter varje uppladdning av testdata.
  - **`chillinPrevalues.json`** med riktiga värden i `"NN:Etikett"`-format — utan det är
    statusdropdownen tom och ingen order går att klassificera.
  - **`members.json`** med minst tre konton, ett per roll (`Desk`, `Administrator`, `SuperAdmin`),
    så att alla tre kodvägarna går att prova.

  - **Seedning.** Generera den medvetet besvärliga uppsättningen i kod, men **bara när
    målkatalogen saknas** — aldrig överskrivning. Användaren har valt persistent data, så ingen
    automatisk nollställning. Återställning = radera katalogen och starta om.
  - Checka in den lilla besvärliga uppsättningen i repot; checka **inte** in anonymiserad
    driftdata (storlek + kvarvarande GDPR-risk) — den laddas upp via Kudu.

  Detta bör lösas i samband med brytpunkten, inte senare — det är förutsättningen för att den
  löpande webbläsarkontrollen genom fas 3–8 ska säga något.

- [ ] **Använd brytpunkten löpande genom fas 3–8, inte bara vid fas 11**
  Ta en skärmdump av inloggningssidan, startsidan, orderlistan och en öppnad order **innan** fas 3
  påbörjas, och jämför efter varje efterföljande fas. Det är den billigaste tänkbara spärren mot
  precis de tysta regressioner som listas i fas 11 — och den enda metod som hade fångat samtliga fyra
  Umbraco-fynd ovan.

---

## Fas 3: Autentisering & Membership

Detta är den mest säkerhetskänsliga delen av migreringen. Umbraco-borttagningen tog vid ett tillfälle
tyst bort hela inloggningsskyddet utan att någon kod klagade — samma risk finns här.

- [x] **Skriv characterization-tester för inloggnings- och auktoriseringsflödet FÖRE omskrivningen**
  `AuthorizationTest.cs` finns men är rent reflektionsbaserad: den verifierar att filtret registreras
  och att rätt fyra sidcontrollers *saknar* `[AllowAnonymous]`. Den kör aldrig
  `AuthorizeAttribute.OnAuthorization` och verifierar aldrig att en oinloggad request faktiskt får
  401/redirect. Dessutom:
  - **`LoginSurfaceController.HandleLogin` är helt otestad** — den mest säkerhetskritiska metoden i
    applikationen har noll tester.
  - `PasswordSurfaceController.ChangePassword`s success-väg är otestad (bara render + ogiltig modell).
  - `[Authorize(Roles = "SuperAdmin")]` är otestat.
  - Att `RegisterGlobalFilters` faktiskt *anropas* från startup är otestat.

  Skriv beteendetester (helst mot en riktig request-pipeline) för dessa innan något rörs.

  **Åtgärdat i efterhand 2026-09-16** (rewriten hade redan skett; detta täcker upp). Statusgenomgång av
  de fyra punkterna:
  - `HandleLogin` — täcks nu av fyra tester i `LoginSurfaceControllerTest.cs` (giltiga uppgifter för
    Desk/annan roll, ogiltiga uppgifter, ogiltig modell). Redan på plats sedan tidigare i sveppet.
  - `ChangePassword`s success-väg — redan täckt av `ChangePassword_ChangeSucceeds_RedirectsWithSuccess`
    (fanns redan).
  - `RegisterGlobalFilters` anropas faktiskt från startup — synligt direkt i `Program.cs:62`
    (`AddControllersWithViews(FilterConfig.RegisterGlobalFilters)`), kombinerat med det befintliga
    enhetstestet som verifierar att metoden lägger till `AuthorizeFilter`.
  - `[Authorize(Roles = "SuperAdmin")]` — den gamla testen kollade bara attributet reflektivt. Nytt
    test tillagt: `MemberAdminSurfaceController_SuperAdminAttribute_ActuallyDeniesNonSuperAdminUsers`
    i `AuthorizationTest.cs`, som bygger en riktig `IAuthorizationService`/`AuthorizationPolicy` och kör
    den faktiska `RolesAuthorizationRequirement`-handlern mot en Desk- respektive SuperAdmin-`ClaimsPrincipal`.

  **Kvarstår medvetet:** ett fullt pipeline-test (`WebApplicationFactory<Program>` som gör en riktig
  HTTP-request och verifierar 401/redirect) är inte byggt. `Bootstrapper.RegisterTypes` bygger just nu
  en riktig `ElasticClient`/`EntityFrameworkOrderItemManager`/`FolioConnection` vid DI-uppstart, så ett
  sådant test skulle antingen kräva riktiga integrationer eller de fejkregistreringar som beskrivs i
  brytpunktens "Gör appen körbar utan riktiga integrationer"-punkt. Görs lämpligen tillsammans med den
  punkten. Fram tills dess är den löpande Puppeteer-baserade webbläsarkontrollen (se brytpunkten efter
  fas 2) den faktiska pipeline-nivå-verifieringen av inloggning/auktorisering.

- [x] **Skriv om `FileMembershipProvider`/`FileRoleProvider` till cookie-autentisering**
  `Members/FileMembershipProvider.cs` (109 rader) och `FileRoleProvider.cs` (61 rader) ärver
  `System.Web.Security.MembershipProvider`/`RoleProvider`, som inte finns i modern .NET.
  Ytan är liten: `FileMembershipProvider` implementerar bara `ValidateUser`, `GetUser` och
  `ChangePassword` (14 övriga medlemmar kastar `NotSupportedException`), `FileRoleProvider` bara
  `IsUserInRole`, `GetRolesForUser`, `GetAllRoles`, `RoleExists` (6 kastar `NotSupportedException`).

  **Behåll den fil-baserade lagringen** (`Config/members.json` via `MemberFileStore.cs`) — inget
  databasberoende ska införas, se motiveringen i Umbraco-TODO:n. Bygg om som
  `AddAuthentication().AddCookie(...)` med en egen `IUserStore`-liknande tjänst istället för
  provider-basklasserna.

  **Viktigt och lyckosamt detaljfynd:** `System.Web.Helpers.Crypto.HashPassword`/`VerifyHashedPassword`
  producerar PBKDF2-HMAC-SHA1, 1000 iterationer, 128-bit salt, 256-bit subkey med formatmarkör `0x00` —
  **exakt samma format** som `Microsoft.AspNetCore.Identity.PasswordHasher<T>` med
  `CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2`. Befintliga hashar i `members.json`
  kan alltså behållas och **ingen användare behöver byta lösenord**. (Till skillnad från vid
  Umbraco-borttagningen, då alla konton måste nollställas.)
  Överväg samtidigt rehash-on-login till IdentityV3 (PBKDF2-SHA256, 100k+ iterationer) — 1000
  SHA1-iterationer är svagt 2026.

  Passa också på: `MinRequiredPasswordLength => 6` deklareras men framtvingas ingenstans —
  `MemberAdminService.CreateMember` kontrollerar bara `IsNullOrWhiteSpace`, så enteckenslösenord
  accepteras idag.

- [x] **Migrera de statiska `Membership`/`Roles`-fasadanropen**
  Samtliga anropsställen:
  - `Membership.ValidateUser` — `LoginSurfaceController.cs:27`, `PasswordSurfaceController.cs:38`
  - `Membership.GetUser` — `PasswordSurfaceController.cs:42`
  - `MembershipUser.ChangePassword` — `PasswordSurfaceController.cs:43`
  - `Roles.IsUserInRole` — `LoginSurfaceController.cs:32` (`"Desk"`), **och två i Razor-vyer**:
    `Views/ChalmersILL.cshtml:32` (`"Administrator"`), `Views/ChalmersILLSettingsPage.cshtml:15`
    (`"SuperAdmin"`). Vyanropen är lätta att missa — de blir `User.IsInRole(...)`.

  Roller i bruk (3 st, hårdkodade strängar på 5 ställen utan konstanter): `Desk` (styr redirect efter
  login), `Administrator` (döljer UI-element, ingen serverside-effekt), `SuperAdmin` (den enda med
  reell auktoriseringseffekt). `members.example.json` nämner `Administrator` men inte `SuperAdmin` —
  uppdatera exemplet.

- [x] **Migrera `FormsAuthentication` till cookie-autentisering**
  Två anropsställen: `LoginSurfaceController.cs:29` (`SetAuthCookie(model.Login, false)`) och
  `Page/ChalmersILLLogoutPageController.cs:25` (`SignOut()`). Web.config `<authentication mode="Forms">`
  med `loginUrl="~/ChalmersILLLoginPage"` → `CookieAuthenticationOptions.LoginPath`. Görs i samma steg
  som punkten ovan.
  Sätt samtidigt `HttpOnly`, `Secure` och `SameSite` — dagens `<forms>` har varken `requireSSL` eller
  vettigt cookienamn (`name="yourAuthCookie"`, uppenbar mall-placeholder).
  Web.config-raden `<authorization><allow users="?" /></authorization>` (som tillåter alla på
  IIS-nivå) försvinner; hela auktoriseringen vilar redan idag på MVC-filtret.

- [x] **Lägg till `[ValidateAntiForgeryToken]` på login- och lösenordsbytes-POST**
  Finns idag på **noll** ställen i hela kodbasen. `[ValidateInput(false)]` finns däremot på 25 —
  request validation är helt borttaget i ASP.NET Core, så de attributen ska bara strykas, men det
  betyder också att det residuala XSS-skyddet från `System.Web` försvinner. Verifiera att vyerna
  output-encodar korrekt (15 `@Html.Raw`-anrop är värda en genomgång).

  **Åtgärdat 2026-09-16.** `[ValidateAntiForgeryToken]` tillagt på `LoginSurfaceController.HandleLogin`
  och `PasswordSurfaceController.ChangePassword`, samt `@Html.AntiForgeryToken()` i respektive
  `Html.BeginForm`-formulär (`Chalmers.ILL.Login.cshtml`, `Settings/ChangePassword.cshtml`) — utan den
  senare hade token aldrig postats med och alla inloggnings-/lösenordsbytesförsök blockerats.
  Reflektionstester tillagda: `HandleLogin_HasValidateAntiForgeryTokenAttribute`,
  `ChangePassword_HasValidateAntiForgeryTokenAttribute`.

  Genomgången av de 15 `@Html.Raw`-anropen gav tre verkliga XSS-fynd (befintliga sedan innan denna
  migrering, inte introducerade av den) — resten (`ArticleByMailOrInternalMail.cshtml`) är `Html.Raw`
  bara på hårdkodade `"<br />"`/`""`-literaler, ingen användardata:
  - `ChalmersILLOrderListPage.cshtml:97` — orderns `reference`-fält skrevs raw förutom
    nyradskonvertering. Fixat: HTML-encoda innan nyradsersättningen (`Html.Encode(...).Replace("\n", "<br />")`).
  - `Chalmers.ILL.Action.Mail.cshtml` (tre ställen) — mailloggens `logItem.Message`/`OriginalOrder`
    (fritext, kan komma från en patrons mailsvar) skrevs raw rakt in i dolda `<div>`:ar som sedan lästes
    med jQuery `.html()` och stoppades i ett textarea-värde. Eftersom det är riktig DOM-parsning kunde
    en `<script>`/`<img onerror=...>` i meddelandet exekvera direkt vid sidladdning, oavsett att
    div:en var `display:none`. Fixat: bytte `Html.Raw(...)` mot vanlig auto-encodande `@(...)`-output,
    och bytte motsvarande `.html()`-läsningar i samma fils inline-script till `.text()` så att de
    HTML-encodade tecknen avkodas tillbaka till ursprunglig text i stället för att synas som entiteter.
  - `Chalmers.ILL.OrderItem.cshtml:472-473` — `OrderItem`/event-mappningen JSON-serialiseras med
    Newtonsoft (som till skillnad från `System.Text.Json`s default-encoder inte escapar `<`) och skrivs
    rakt in i en `<script>`-block som ett JS-objektlitteral. Ett meddelande som innehåller `</script>`
    hade kunnat bryta ut ur blocket. Fixat: `.Replace("<", "\\u003c")` på den serialiserade strängen.

  Inga nya renderingstester tillagda för dessa vyer — testsviten har ingen infrastruktur för att
  rendera Razor-vyer än (skulle vara ett eget, större arbete, se fas 9). Verifiera visuellt vid nästa
  brytpunkts-webbläsarpass (fas 3–8), särskilt mailflödet och en order med specialtecken i referens/
  patronanteckningar.

---

## Fas 4: Controllers & Razor-vyer

- [x] **Byt `System.Web.Mvc.Controller` → `Microsoft.AspNetCore.Mvc.Controller` i 42 controllers**
  Exakt **42** controller-filer under `Controllers/SurfaceControllers/` (varav 8 i `Page/`), alla med
  `using System.Web.Mvc;` och alla ärvande `Controller`. Ingen `ApiController`, ingen
  `AsyncController`, **noll `async`-actions** i hela projektet. 99 publika action-metoder: 98 returnerar
  `ActionResult`, 1 returnerar `JsonResult` (`LogItemSurfaceController.cs:66`).

  Mekaniska ändringar med hög volym:
  - **`JsonRequestBehavior` — 64 förekomster i 31 filer** (63 `.AllowGet`, 1 `.DenyGet` i
    `SystemSurfaceController.cs:84`). Overloaden finns inte i Core; `Json(obj)` tillåter GET som
    standard. Alla 64 ska bara strykas — men notera att `.DenyGet`-fallet därmed tappar sin spärr och
    behöver ett `[HttpPost]`-attribut i stället (det har det redan).
  - **`[ValidateInput(false)]` — 25 förekomster**, alltid som `[HttpPost, ValidateInput(false)]`.
    Attributet finns inte i Core, stryks.

  Bekräftat **frånvarande** och alltså inga problem: `TempData`, `ViewBag`/`ViewData`,
  `HttpPostedFileBase`/`Request.Files`, `ContentResult`, `HttpStatusCodeResult`, `HttpNotFound()`,
  `[ChildActionOnly]`, `[OutputCache]`, `Html.Action`/`RenderAction` (**inga child actions** — det är
  den vanligaste MVC5→Core-fällan och den finns inte här), `MvcHtmlString`, custom
  `ActionFilterAttribute`, custom `IModelBinder`, custom `IViewEngine`, `HandleErrorAttribute`, Areas.
  `ModelState` används på bara 2 ställen, `return File(...)` på 1
  (`MediaItemSurfaceController.cs:40`).

- [x] **Migrera de 10 vyerna med `@inherits System.Web.Mvc.WebViewPage`**
  Ursprungligen 12, men 2 (`MacroPartials/*`) är döda och raderas i fas 0b. Kvar:
  `ChalmersILL.cshtml`, `ChalmersILLDiskPage.cshtml`, `ChalmersILLLoginPage.cshtml`,
  `ChalmersILLLogoutPage.cshtml`, `ChalmersILLOrderListPage.cshtml`, `ChalmersILLSettingsPage.cshtml`,
  `ChalmersILLStartPage.cshtml`, `ChalmersILLStatisticsPage.cshtml`,
  `Partials/Chalmers.ILL.OrderItem.cshtml`, `Shared/ReceivedAtBranchResult.cshtml`.
  Tre av dem (`ChalmersILLLoginPage.cshtml` plus de två döda) använder **icke-generisk** `WebViewPage`
  utan `<T>`. `WebViewPage` ersätts av `@model` utan explicit `@inherits`.
  Totalt finns 38 `.cshtml` under `Views/`; övriga 26 använder redan `@model`.

- [x] **Fixa `Layout = "ChalmersILL.cshtml"` — går sönder tyst**
  Fem vyer (`ChalmersILLLogoutPage`, `ChalmersILLOrderListPage`, `ChalmersILLSettingsPage`,
  `ChalmersILLStartPage`, `ChalmersILLStatisticsPage`) sätter `Layout` till ett **bart filnamn**.
  Layouten `ChalmersILL.cshtml` ligger i `Views/`-roten, inte i `Views/Shared/`. I ASP.NET Core löses
  `Layout` mot view location-formaten och kräver normalt full sökväg eller att filen ligger i
  `Views/Shared/`. Antingen flytta layouten till `Shared/` eller använd `~/Views/ChalmersILL.cshtml`.
  Det finns idag varken `_ViewStart.cshtml` eller `_ViewImports.cshtml` — båda bör läggas till.

- [x] **Skriv om de två `@helper`-blocken — men inte till `@functions`**
  Båda i `Views/ChalmersILLOrderListPage.cshtml`: `ParseStatusPrevalue` (rad 201-204) och
  `RenderOrderList` (rad 206-254, ~50 rader som skriver ut HTML, anropas på rad 157 och 186).
  **`@functions`-metoder i Core Razor kan inte returnera markup** — `@helper` gick att anropa som
  `@RenderOrderList(...)` och få HTML utskriven, det gör inte en `@functions`-metod med returvärde.
  Alternativ: bryt ut som partial views, eller använd templated Razor delegates
  (`Func<T, IHtmlContent>`), eller lokala funktioner som skriver till `Output`.
  **Notera:** filen har **redan** ett `@functions`-block (rad 13-76) med tre C#-metoder
  (`AppendArgumentsToQueryString`, `GetSortOrderFromOrderItemSearchResult`, `GetSigelFromLibraryName`)
  — det blocket är oproblematiskt och ska inte blandas ihop med `@helper`-frågan.
  `AppendArgumentsToQueryString` använder `HttpUtility.ParseQueryString` och verkar dessutom oanvänd —
  kontrollera och ta bort i så fall.

- [x] **Ersätt `.AsInt()` i `RenderOrderList`**
  Tre anrop på rad 214, 224 och 229 i `ChalmersILLOrderListPage.cshtml`. `.AsInt()` är en
  `System.Web.WebPages`-extension som inte finns i Core → `int.TryParse` eller `Convert.ToInt32`.

- [x] **Ta bort `Views/Web.config` och `Views/Web.config.transform`**
  `Views/Web.config` (51 rader) innehåller `<system.web.webPages.razor>` (`MvcWebRazorHostFactory`,
  `pageBaseType`, fyra namnrymder), `<appSettings webpages:Enabled=false>`,
  `<pages validateRequest="false">`, och `<httpHandlers>`/`<handlers>` med `HttpNotFoundHandler` som
  blockerar direkt `.cshtml`-åtkomst. Allt ersätts av `_ViewImports.cshtml` (`@using`-direktiven) —
  blockeringen av direktåtkomst är inbyggd i Core.
  `Views/Web.config.transform` är byte-identisk med `Views/Web.config` (NuGet-artefakt från
  UmbracoCms-paketet) och tas bort samtidigt. Notera att `Views/Web.config` är `<None Include>` i
  csproj, inte `<Content>`, dvs. den publiceras inte ens idag.

  Vy-`using`-rader att flytta till `_ViewImports.cshtml`: `System.Web.Security` finns i
  `ChalmersILLDiskPage.cshtml:2` och `ChalmersILLOrderListPage.cshtml:2`.

---

## Fas 5: SignalR och klientassets

- [x] **Migrera SignalR-servern — enklare än den ser ut, men med en tyst fälla**
  Ytan är minimal: `SignalR/NotificationHub.cs` är en **helt tom** `Hub` (inga serverside-metoder alls,
  klienten kan inte anropa något). `Notifier.cs` hämtar hub-kontexten statiskt via
  `GlobalHost.ConnectionManager.GetHubContext<NotificationHub>()` (rad 21 och 49) och anropar exakt en
  klientmetod, `Clients.All.updateStream(n)` (rad 43 och 62). Inga grupper, ingen `Clients.User`, ingen
  `OnConnected`/`OnDisconnected`. Byt till injicerad `IHubContext<NotificationHub>` och
  `app.MapHub<NotificationHub>(...)` i `Program.cs`.

  **Tyst fälla:** payloaden `OrderItemNotification` (`NodeId`, `EditedBy`, `EditedByMemberName`,
  `SignificantUpdate`, `IsPending`, `UpdateFromMail`) serialiseras med **PascalCase** i SignalR 2, och
  klienten läser `value.NodeId`, `value.EditedBy` osv. ASP.NET Core SignalR default-serialiserar med
  **camelCase** — klienten går sönder utan felmeddelande om man inte sätter
  `PayloadSerializerOptions.PropertyNamingPolicy = null`.

  **Säkerhetsnotering:** hubben är helt oautentiserad idag — det globala MVC-`AuthorizeAttribute`
  skyddar inte SignalR-endpointen. Överväg `[Authorize]` på hubben i samma veva.

- [ ] **Byt klient-JS från `jquery.signalR` till `@microsoft/signalr`**
  Alla `$.connection`-anrop ligger i **`Scripts/chalmers.ill.js`** — fyra stycken på rad 1283
  (`$.connection.notificationHub`), 1347 och 1349 (`hub.disconnected` + reconnect efter 5 s) samt 1354
  (`hub.start()` med `.done()`/`.fail()`). Vyerna innehåller **noll** `$.connection`-anrop, bara
  `<script src>`-taggar.
  Registrerade callbacks: enbart `notifier.client.updateStream` (rad 1286-1345), som hanterar
  mail-driven omladdning, radrefresh, badge-räknare för pending orders, och stulna lås.
  `$.connection.hub.disconnected` → `withAutomaticReconnect()`/`connection.onclose`.
  `hub.start()` anropas på top-level, inte i `$(document).ready`.
  `<script src="/signalr/hubs">` är den servergenererade hub-proxyn — den **finns inte alls** i
  ASP.NET Core SignalR, vilket är varför `$.connection.notificationHub` fungerar utan explicit hubnamn
  idag. Ersätts av `new signalR.HubConnectionBuilder().withUrl("/notificationHub")`.

- [ ] **Ersätt bower med en modern asset-strategi — blockerar deploy-pipelinen**
  **Detta saknades helt i den tidigare listan och är ett verkligt deployment-hinder.**
  `Chalmers.ILL/bower_components/` innehåller jquery, bootstrap, Snap.svg, Chart.js, moment, signalr,
  bootstrap-markdown, markdown, eonasdan-bootstrap-datetimepicker — och är **inte spårad i git**
  (0 filer, gitignorad på två ställen), men serveras vid körning från `/bower_components/...` i fyra
  vyer. Assets hämtas alltså med `bower install` vid deploy. Bower är deprecated sedan 2017.
  Dessutom: `bower_components/` är **inte** `<Content Include>`-ad i csproj idag, men med SDK-style
  inkluderas hela trädet implicit — samma fälla som Umbraco-mapparna i fas 0b.
  Åtgärd: `package.json` + npm, eller checka in ett kurerat `wwwroot/lib/`. Filerna måste under
  `wwwroot/` i Core oavsett, tillsammans med `Scripts/`, `Css/` och `images/`.
  **Ingen bundling/minifiering finns idag** (noll `System.Web.Optimization`, inga
  `@Scripts.Render`/`@Styles.Render`) — behåll den enkelheten om ni inte har skäl att ändra.
  `Views/ChalmersILL.cshtml:150` gör cache-busting med `?r=@DateTime.Now.Ticks`, dvs. filen cachas
  aldrig — värt att byta mot en versionssträng eller `asp-append-version`.

- [x] **Ta bort NuGet-paketet `jQuery` 1.6.4 — det är oanvänt**
  Vyerna laddar bower-versionen (`~2.1.3`) från `/bower_components/jquery/dist/jquery.min.js`.
  NuGet-paketets jQuery används ingenstans. Det är alltså inte en uppgradering utan en borttagning.

  **Redan gjort.** Verifierat 2026-09-16: ingen `<PackageReference Include="jQuery"`) finns i något
  `.csproj` längre — försvann redan i fas 1b:s paketstädning (samma svep som Npgsql/döda paket/
  OWIN-MVC5-stacken). Ingen kodändring behövdes, bara ikryssning.

---

## Fas 6: Dependency injection & konfiguration

**⚠️ Fas 6 är en hård förutsättning för fas 10 — upptäckt 2026-09-16.** Appen läser konfiguration
uteslutande via `System.Configuration.ConfigurationManager.AppSettings`, dvs. ur `App.config` som
bakas in i deploymentartefakten som `Chalmers.ILL.dll.config`. Verifierat: **noll** förekomster av
`Environment.GetEnvironmentVariable` i hela `Chalmers.ILL/`.

På Windows-planen substituerade IIS in App Services Application Settings i `Web.config` — det är en
Windows-/IIS-specifik plattformsfunktion. **På Linux-planen finns den inte:** app settings exponeras
bara som miljövariabler. Med `Web.config` borttagen och `App.config` som brygga går appen därför
**inte att konfigurera i Azure över huvud taget** förrän den här fasen är gjord. Punkten "Flytta
`Local.config` till App Settings" i fas 10 förutsätter tyst att `IConfiguration` redan finns.

Konsekvens för ordningen: fas 6 ska göras före fas 10, och före allt arbete som behöver en
miljöstyrd inställning i Azure.

- [x] **Ersätt Unity med `Microsoft.Extensions.DependencyInjection`**
  Paketet heter `Unity` 3.0.1304.1 i `packages.config` (assemblynamnet är `Microsoft.Practices.Unity`
  — sök på rätt sak vid PackageReference-migreringen).
  `Bootstrapper.cs` har **31 aktiva registreringar** (39 textuella förekomster, varav 8 utkommenterade
  i FakeFolio-blocket; rad 147 och 151 är ömsesidigt uteslutande if/else-grenar, så 30 exekveras).
  Registreringarna täcker konfiguration, NEST `ElasticClient`, `HttpClient`, mail, mediahantering,
  FOLIO-integrationen (7 interfaces), patrondataleverantörer och manuellt konstruerade singletons.
  Hela listan flyttas till `builder.Services`. `DependencyResolution/UnityMvcDependencyResolver.cs` och
  `UnityWebApiDependencyResolver.cs` tas bort helt, liksom
  `Chalmers.ILL.Tests/DependencyResolution/UnityDependencyResolversTest.cs` (8 tester som bara testar
  adaptrarna).

  **Den svåra biten är inte antalet registreringar utan livstiderna.** Se fas 7 —
  `EntityFrameworkOrderItemManager` och `Notifier` är manuellt konstruerade singletons med en
  cirkulär koppling som löses med setter-injection (`Bootstrapper.cs:198-199`). Den konstruktionen kan
  inte flyttas rakt av.

- [x] **Migrera `ConfigurationManager.AppSettings` → `IConfiguration`/`IOptions<T>`**
  ~60 anropsställen i 19 filer. **Sök inte bara på `ConfigurationManager.`** — fem filer använder
  `using static System.Configuration.ConfigurationManager` och skriver bara `AppSettings[...]`:
  `Services/FolioService.cs:5`, `Services/FolioItemService.cs:4`, `Models/HoldingBasic.cs:2`,
  `Models/ItemBasic.cs:3`, `Models/InstanceBasic.cs:2`.
  Övriga: `Configuration/DefaultChillinConfiguration.cs` (13 st), `EventHandlers/OwinStartup.cs`,
  `Connections/FolioConnection.cs`, `Providers/LibrisOrderItemsSource.cs` (8),
  `Mail/ChalmersOrderItemsMailSource.cs` (9), `Mail/ExchangeMailWebApi.cs`,
  `Mail/MicrosoftGraphMailWebApi.cs`, `Mail/MailService.cs`, `Patron/SierraCache.cs` (6),
  `Patron/SolrLibcdksAffiliationDataProvider.cs`, `SystemSurfaceController.cs`,
  `OrderItemMailSurfaceController.cs`, `LoginSurfaceController.cs`,
  `Page/ChalmersILLOrderListPageController.cs`.

  Web.config har **46 appSettings-nycklar**. Flera är hemligheter (`chalmersIllExhangePass`,
  `MicrosoftGraphClientSecret`, `folioPassword`, `patronCacheSolrBasicAuthPassword`, `librisApiKey`,
  `sierraConnectionString`). Web.config deklarerar `<appSettings file="Local.config">` men
  **`Local.config` finns inte i repot** — det är den avsedda mekanismen för hemligheter idag. Ersätt
  med user-secrets i utveckling och miljövariabler/`appsettings.Production.json` i drift.
  Nycklar som blir överflödiga och kan strykas: `ValidationSettings:UnobtrusiveValidationMode`,
  `aspnet:UseTaskFriendlySynchronizationContext`, `webpages:Enabled`, `enableSimpleMembership`,
  `autoFormsAuthentication`, `owin:AppStartup`, `log4net.Config`.
  Nyckeln `UseMicrosoftGraphMailService` försvinner när EWS tas bort (fas 8).

  **⚠️ 19 nycklar som koden läser saknas helt i `App.config`** (inventerat 2026-09-16) — de returnerar
  `null` idag, så mail- och FOLIO-flödena går inte att köra ens med riktiga credentials:
  `chalmersIllExhangeLogin`, `chalmersIllExhangePass`, `chalmersIllSenderAddress`,
  `chalmersILLArchiveProcessedMails`, `chalmersILLForwardingAddress`, `chillinStatisticalCodeId`,
  `holdingPermanentLocationId`, `instanceIdentifierTypeId`, `instanceModesOfIssuance`,
  `instanceResourceTypeId`, `instanceStatusId`, `itemMaterialTypeId`, `itemPermanentLoanTypeId`,
  `itemPermanentLoanTypeIdInHouse`, `servicePointHuvudbiblioteketId`,
  `servicePointLindholmenbiblioteketId`, `servicePointArkitekturbiblioteketId`, `LibPSearchUrl`,
  `LibPSearchApiKey`.
  Värdena finns bara i den driftsatta Windows-appens App Settings och måste hämtas därifrån.
  Särskilt allvarligt: `chalmersIllSenderAddress` används av den **aktiva** Graph-vägen
  (`MicrosoftGraphMailWebApi.cs:257, 331`), och de nio FOLIO-UUID:erna sätts i *fältinitierare* i
  `Models/HoldingBasic.cs`, `ItemBasic.cs` och `InstanceBasic.cs`, dvs. de utvärderas vid
  objektskapande och skickas som `null` in i FOLIO.
  Tre nycklar finns i `App.config` men används inte i kod: `chalmersILLMailSignature`,
  `messageTemplatesLink`, `sierraConnectionString` (den sista naturligt — `Patron/Sierra.cs` togs
  bort i fas 0b).

  **Upplägg (avstämt 2026-09-16):** `IChillinConfiguration` utökas till att täcka samtliga nycklar
  och backas av `Microsoft.Extensions.Configuration`. Den injiceras överallt — i vyerna via
  `@inject` i `_ViewImports.cshtml`, och in i FOLIO-POCO:erna via deras konstruktorer från
  `FolioService`/`FolioItemService`, som redan är DI-konstruerade. Målet är **noll** statiska
  `ConfigurationManager`-anrop kvar; halvvägs är värre än antingen.

  **Gjort hittills:**
  - [x] Appens egen `IConfiguration` omdöpt till `IChillinConfiguration` (commit `ae416fb`).
    Krockade namnmässigt med `Microsoft.Extensions.Configuration.IConfiguration`, och en fil som
    bara har `using Microsoft.Extensions.Configuration` hade tyst bundit mot fel interface.
  - [x] `appsettings.json` + `builder.Configuration` inkopplat
  - [x] De ~60 anropsställena migrerade
  - [x] `System.Configuration.ConfigurationManager`-paketet och `App.config` borttagna

  **Genomfört 2026-09-16.** `IChillinConfiguration` utökad till 54 properties (grupperade: hosts,
  Graph-mail, legacy Exchange-fält, storage/sök, Libris, FOLIO, patron-Solr, LibPSearch, diverse).
  `DefaultChillinConfiguration` backas nu av injicerad `Microsoft.Extensions.Configuration
  .IConfiguration` mot en ny `Chalmers.ILL/appsettings.json` under `"Chillin"`-sektionen (samma
  dev-placeholder-värden — `xxx`/`******` — som `App.config` hade, plus de 19 tidigare saknade
  nycklarna som platshållare). Alla ~60 anropsställen migrerade: FOLIO-ytan (`FolioConnection`,
  `FolioService`, `FolioItemService`, samt `HoldingBasic`/`InstanceBasic`/`ItemBasic` som POCO:er
  fick värdena via konstruktorn istället för fältinitierare), mail-ytan (`MailService`,
  `MicrosoftGraphMailWebApi`, `ChalmersOrderItemsMailSource`, `OrderItemMailSurfaceController`,
  `LibrisOrderItemsSource`), controllers (`SystemSurfaceController`,
  `ChalmersILLOrderListPageController`, `LoginSurfaceController`), `Program.cs`
  (`checkForPendingDatabaseMigrations`) och samtliga fyra vyer som läste `AppSettings` direkt
  (`ChalmersILL.cshtml`, `ChalmersILLLoginPage.cshtml`, `ChalmersILLStartPage.cshtml`,
  `Chalmers.ILL.Action.Mail.cshtml`) via ett nytt `@inject IChillinConfiguration Config` i
  `_ViewImports.cshtml`.
  **Sidoupptäckt:** `SierraCache`/`SolrLibcdksAffiliationDataProvider` (patron-Solr-implementationerna)
  är inte kopplade i `Bootstrapper.cs` — `IPatronDataProvider`/`IAffiliationDataProvider` resolvar
  till `FolioPatronDataProvider`/`PdbAffiliationDataProvider`, och inget `new SierraCache(...)` eller
  `new SolrLibcdksAffiliationDataProvider(...)` finns någonstans. Död kod, migrerad ändå (för att
  fortsätta kompilera) men inte borttagen — beslut om att ta bort dem hör inte hemma i den här punkten.
  Verifierat manuellt med `dotnet run`: inloggning, disk-vy och inställningssidan renderar felfritt
  utan `App.config`, enbart mot `appsettings.json`.
  **Kvarstår medvetet:** ingen `appsettings.Development.json`/`appsettings.Production.json`-uppdelning
  och ingen user-secrets-koppling för hemligheter — det är nästa punkt nedan
  ("Sätt upp `appsettings.json` + miljöspecifika filer"), som är fristående. De 46 ursprungliga
  Web.config-nycklarna som redan var överflödiga (`ValidationSettings:UnobtrusiveValidationMode` m.fl.)
  togs aldrig med i `appsettings.json` eftersom de aldrig lästes av någon kod.

- [x] **Sätt upp `appsettings.json` + miljöspecifika filer**
  Det finns ingen `Web.Debug.config`/`Web.Release.config`-transform idag, så det saknas miljöuppdelning
  att bevara. Skapa `appsettings.json` + `appsettings.Development.json`/`appsettings.Production.json`.
  Kandidater för miljöspecifika värden: sökvägen till orderkatalogen (se fas 7 och 10),
  `testServer`/`liveServer`, `cronServerIpAddress`, `BaseUrl`, `ElasticSearchUrl`.
  `checkForPendingDatabaseMigrations` och `chillinOrderItemsDb` utgår helt med fas 7.
  Ersätt samtidigt de tre icke-standardkonfigurationerna `ChillinTest.Debug`/`ChillinTest.Release`/
  `ChalmersILL` i csproj (se fas 1).

  **Genomfört 2026-09-16.** De fem uppräknade nycklarna (`BaseUrl`, `TestServer`, `LiveServer`,
  `CronServerIpAddress`, `ElasticSearchUrl`) flyttades ut ur `appsettings.json` till
  `appsettings.Development.json` (dagens localhost-värden) respektive `appsettings.Production.json`
  (`"xxx"`-platshållare i väntan på fas 10:s riktiga Azure-värden — miljövariabler i Azure App
  Settings övertrumfar ändå filen). `checkForPendingDatabaseMigrations`/`chillinOrderItemsDb` rördes
  inte, som noterat ovan hör de till fas 7. De tre icke-standardkonfigurationerna i csproj behövde
  **ingen åtgärd** — verifierat att `ChillinTest.Debug`/`ChillinTest.Release`/`ChalmersILL` redan
  försvann som en bieffekt av SDK-style-konverteringen i fas 1a (0 träffar på `ChillinTest` i hela
  repot).

  **Sidofynd, hanterat:** ASP.NET Core defaultar `ASPNETCORE_ENVIRONMENT` till **Production** när
  variabeln är osatt (ingen `launchSettings.json` fanns), vilket tyst hade bytt `dotnet run` från
  fungerande localhost-värden till `appsettings.Production.json`s `"xxx"`-platshållare — exakt den
  sortens fel brytpunkten efter fas 2 finns till för att fånga. `.devcontainer/`s filer är
  skrivskyddade i den här sessionen (samma begränsning som SDK-installationen och Chromium-bytet
  tidigare), så `ENV ASPNETCORE_ENVIRONMENT=Development` i `Dockerfile` var inte en väg. Löst med
  standardmekanismen istället: `Chalmers.ILL/Properties/launchSettings.json` med en `Development`-
  profil — den filen publiceras aldrig (SDK:n utesluter den från publish), så den påverkar inte
  Azure-driftsättningen, bara lokal `dotnet run`. Verifierat med en full `dotnet run`: "Hosting
  environment: Development" i loggen, `/` → 302 → `/ChalmersILLLoginPage`, login-sidan 200 — samma
  flöde som brytpunktens ursprungliga verifiering.

- [x] **Ta bort `<configSections>`**
  Tre poster: `log4net`, `entityFramework`, `system.web.webPages.razor`.
  `log4net` har redan en egen fil (`Config/log4net.config` via `configSource`) — den behålls som fil,
  men kopplingen via `ConfigurationManager` byts mot explicit `XmlConfigurator.Configure(...)` i
  `Program.cs`. Se fas 0a: appendern måste dessutom bytas eftersom den pekar på en Umbraco-typ.

  **Beslut 2026-09-16: log4net behålls, `Microsoft.Extensions.Logging` väljs bort.** Avstämt med
  användaren, som vill ha loggfiler på disk och minimal Azure-integration (ingen Application
  Insights). `Microsoft.Extensions.Logging` har **ingen inbyggd filprovider** — Console, Debug,
  EventSource och EventLog är allt som finns — så ett byte skulle kräva Serilog eller NLog som nytt
  beroende, enbart för att återfå den funktion log4net redan har och som är verifierad
  (`Log4NetConfigurationTest` skriver en rad och läser tillbaka den från disk). Argumenten för
  ILogger var strukturerad loggning och Azure-native utdata; båda är bortvalda.
  Kvarstår: `log4net` 2.0.12 har en känd sårbarhet (GHSA-4f7c-pmjv-c25w) och senaste version är
  3.4.0 — ett majorsteg med egna brytande ändringar. Uppgraderingen hör hemma i fas 8, inte här.
  `entityFramework`-sektionen försvinner helt med EF6-borttagningen (fas 7), liksom
  `chillinOrderItemsDb`-connection-stringen — det finns ingen databas kvar att peka ut.
  `system.web.webPages.razor` hanteras av `_ViewImports.cshtml`.

  **Verifierat 2026-09-16: redan uppfyllt utan ny kod.** `<configSections>` fanns bara i `App.config`,
  som togs bort i sin helhet i föregående punkt ("Migrera `ConfigurationManager.AppSettings`") — 0
  träffar på `configSections`/`entityFramework`/`system.web.webPages.razor` kvar i repot (sökt över
  alla `.config`/`.csproj`). `log4net` konfigureras redan explicit från `Program.cs` (fas 0a),
  `system.web.webPages.razor` hanteras redan av `_ViewImports.cshtml` (fas 4). `entityFramework`-
  sektionen och `chillinOrderItemsDb`-anslutningssträngen försvann alltså i praktiken redan här —
  **notera dock:** `OrderItemsDbContext` (`base("chillinOrderItemsDb")`) har därmed **ingen kvarvarande
  källa** för den anslutningssträngen. Ofarligt just nu (inget i isolerat/utvecklingsflödet rör
  `EntityFrameworkOrderItemManager`), men databasen är alltså redan de facto onåbar innan fas 7 formellt
  tar bort den. Inget att åtgärda här — det är precis vad fas 7 gör.

---

## Fas 7: Ersätt databasen med filbaserad lagring

Databasberoendet tas bort helt. EF6, SQL Server, connection strings och de 19 migrationerna ersätts av
**en JSON-fil per order**.

**Varför det fungerar — belagt genom kodgranskning:**
- Samtliga fem läsvägar i `EntityFrameworkOrderItemManager` hämtar **hela aggregatet**:
  `.Where(x => x.NodeId == n).Include(AttachmentList).Include(LogItemsList).Include(SierraInfo).Include(SierraInfo.adress)`.
  Det finns ingen query som läser loggposter fristående, ingen join mellan ordrar och ingen
  aggregering. Ordern med sina bilagor, loggposter och Sierra-info läses och skrivs alltid som en
  enhet — datamodellen är alltså redan ett dokumentaggregat.
- `OrderItemModel` har exakt tre navigationsegenskaper (`LogItemsList`, `AttachmentList`,
  `SierraInfo`), varav den sista har en egen `adress`. Loggposterna lagras dessutom redan delvis som
  JSON i en kolumn.
- **All sökning går redan till Elasticsearch.** `DefaultStatMngr` (statistik) och `BulkDataManager`
  (publik API) tar bara `IOrderItemSearcher` och rör aldrig en `DbContext`.
- Databasen används i praktiken bara till: hämta på `NodeId`, hämta på `OrderId`, hämta på
  `EditedBy`, lägg till, spara, ta bort. Inget av det är relationellt.

**Avstämt med användaren:** inget annat system läser Chillin-databasen direkt, och volymen är
ca 10 000–100 000 ordrar.

**Detta ersätter det tidigare beslutet om scoped `DbContext` via DI.** Två problem löses upp i stället
för att åtgärdas: dagens `Dictionary<threadId, DbContext>` utan låsning försvinner med EF6, och
EF6-på-`net10.0` behöver inte längre verifieras.

- [x] **Implementera filbaserad `IOrderItemManager`**
  Interfacet behålls oförändrat — det är implementationen som byts. Ersätt de 13 dataåtkomstställena
  i `EntityFrameworkOrderItemManager` (rad 47, 75, 131, 184, 353, 430, 522, 1434, 1453, 1625, 1744
  m.fl.) med läsning och skrivning av en fil per order.
  - **En fil per order**, hela aggregatet serialiserat som JSON (Newtonsoft finns redan och modellerna
    har redan `[JsonProperty]`-attribut på sina håll).
  - **Katalogindelning krävs vid er volym.** 10 000–100 000 filer i en platt katalog fungerar dåligt
    över Azure Files. Dela upp på id-intervall (t.ex. `orders/00/`, `orders/01/`, … efter
    `NodeId / 1000`) eller på år. Välj något som gör en fils sökväg direkt beräkningsbar från
    `NodeId`, så att uppslag aldrig kräver katalogläsning.
  - **`OrderId` → `NodeId`** behöver en egen liten uppslagsstruktur (en indexfil, eller sök i ES).
  - Ta samtidigt bort `Database/OrderItemsDbContext.cs`, hela `Migrations/` (19 migrationer, 39 filer)
    och den nu överflödiga klassen `SaveException` om den bara rör EF.

  **Genomfört 2026-09-16.** `Chalmers.ILL/OrderItems/FileOrderItemManager.cs` ersätter
  `EntityFrameworkOrderItemManager` (borttagen, liksom `Database/OrderItemsDbContext.cs`,
  `Migrations/` och `SaveException`; `EntityFramework`-paketreferensen borttagen ur csproj; EF-bara
  attribut som `[Key, DatabaseGenerated]` städade bort från `OrderItemModel`/`SierraModel`/`LogItem`/
  `OrderAttachment`). En fil per order under `DataPath/orders/{NodeId/1000:D3}/{NodeId}.json`.
  `OrderId`→`NodeId` löst med en egen indexfil (`OrderIdIndex.cs`, se punkten om `NodeId` nedan för
  varför inte ES) snarare än sökning.

  **Det gamla trådnyckade `Dictionary<threadId, DbContext>`-mönstret är borttaget, men den batchning
  det gav är INTE det** — den är medveten, inte ett EF-implementationsdetalj. Nästan varje
  controller under `Controllers/SurfaceControllers/` anropar `Set*`/`AddLogItem` flera gånger med
  `doReindex=false, doSignal=false` och avslutar med ett sista anrop med defaultvärdena (`true,
  true`) som ska flusha alltihop i **en** filskrivning, **en** ES-indexering och **en**
  SignalR-notifiering. Verifierat genom att läsa igenom samtliga anropsställen (inte bara
  `EntityFrameworkOrderItemManager` själv) innan omskrivningen påbörjades. `FileOrderItemManager`
  replikerar detta med en tråd-nyckad "pending order"-buffert (`_threadIdToPendingOrder`) istället
  för EF:s identity map — samma trådaffinitets-antagande som förut (fas 4: "noll async-actions",
  så en request kör på en och samma tråd hela vägen).

  **Tre latenta fel hittade under genomläsningen, fixade som en del av omskrivningen** (inte
  bevarade avsiktligt trasiga för "byte-identisk" kompatibilitet — se motivering per punkt):
  - `SetReference` var den enda `Set*`-metoden vars interna `AddLogItem`-anrop **saknade** explicit
    `false, false` (alla 16 andra har det). Effekten: `SetReference` sparade/reindexerade/notifierade
    alltid direkt, oavsett vad anroparen bad om — `OrderItemReferenceSurfaceController.cs:47` anropar
    den just med `false, false` och förlitar sig (omedvetet) på detta. Ofarligt idag (inget annat
    anrop följer i den kedjan), men uppenbart oavsiktligt givet mönstret överallt annars. Fixat till
    samma mönster som resten.
  - Läsning av en order som inte finns kastade `NullReferenceException` inifrån `FillOutStuff(null)`
    **innan** koden nådde sin egen `if (orderItem != null) ... else throw OrderItemNotFoundException`
    — den avsedda kastsatsen var död kod. Osynligt för slutanvändaren (controllers fångar `Exception`
    generellt), men gav sämre felmeddelanden vid felsökning. `FileOrderItemManager` kastar nu den
    avsedda `OrderItemNotFoundException` med en beskrivande text.
  - `FillOutStuff` (denormaliserar `*Id`-fält till strängar, t.ex. `DeliveryLibraryId` →
    `DeliveryLibrary`) anropades inkonsekvent — vissa `Set*`-metoder körde den efter en mutation,
    andra inte, vilket kunde lämna en sträng ur synk med sitt id i det som faktiskt sparades/
    indexerades i ES (synligt först nästa gång ett *annat* fält råkade trigga en ny `FillOutStuff`).
    `FileOrderItemManager` kör den nu ovillkorligen precis före varje skrivning — idempotent och
    billigt, så ingen nackdel med att köra den "för ofta".

  **En medveten förenkling:** den gamla tvåfas-skapelsen (spara för att få ett `NodeId` från EF:s
  identity-kolumn, sätt sedan `OrderId` med det och spara igen) gjorde att en nyskapad order kortvarigt
  syntes i ES med sitt temporära MD5-baserade `OrderId` innan den korrigerades vid den andra sparningen.
  Med en egen `NodeId`-räknare (se nästa punkt) behövs ingen databasrundtripp för att få ett id —
  `NodeId` och det slutgiltiga `OrderId` sätts i ett svep, en skrivning, en ES-indexering.
  `MakeDuplicate` (som i EF-versionen batchade två olika entiteter — käll­ordern och kopian — i en enda
  `SaveChanges`) hanteras nu som två sekventiella, oberoende operationer eftersom den nya bufferten bara
  håller en orders pending-ändringar per tråd åt gången; kopian flushas alltid direkt (den har inget
  senare anrop att batchas med), källordens logginlägg respekterar anroparens `doReindex`/`doSignal`.

  Characterization-tester tillagda i `FileOrderItemManagerTest.cs` (14 st): batchningsmönstret end-to-end
  (flera `false,false`-anrop + ett flushande default-anrop ger exakt en reindex/notify), att
  `doReindex=false` utan uppföljande flush aldrig når disk, att `MakeDuplicate` loggar på båda
  ordrarna, att `ResetAllAnonymizationFlags`/`SetIsAnonymized` inte sparar när inget ändrats, att
  blandade `NodeId` inom samma tråds pending-batch kastar (`InvalidOperationException`) istället för
  att tyst tappa data, `GetLockedOrderItems` mot ES, samt de två ursprungliga `FillOutStuff`-testerna.
  191/191 gröna. Verifierat manuellt med `dotnet run` i Live-läge: appen startar, `Bootstrapper`
  konstruerar `FileOrderItemManager`/`NodeIdGenerator`/`OrderIdIndex` utan fel, inloggningsflödet
  fungerar identiskt mot brytpunkten.

- [x] **Lös `NodeId`-genereringen — den kräver eftertanke**
  `NodeId` är idag en identity-kolumn. Den ligger i URL:er och i **tryckta QR-koder på fysiska
  följesedlar**, så den måste förbli `int` och får **aldrig** återanvändas eller kollidera.
  Behövs: en varaktig räknare med atomär uppräkning. Enkelinstans (fastställt beslut) gör detta
  hanterbart — en räknarfil skyddad av samma lås som skrivningarna, eller max+1 läst vid uppstart och
  därefter hållen i minnet. Räknaren måste överleva omstart och deploy, dvs. ligga i den persistenta
  katalogen och inte i `wwwroot`.
  Sätt startvärdet till högsta befintliga `NodeId` + 1 vid migreringen.

  **Genomfört 2026-09-16.** `Chalmers.ILL/OrderItems/NodeIdGenerator.cs` — en räknarfil
  (`DataPath/orders/next-node-id.txt`), läst en gång och sedan hållen i minnet bakom ett lås,
  skriven (atomiskt, temp+`File.Replace`) vid varje allokering. Saknas filen (färsk `DataPath`, eller
  en migrering som inte hunnit sätta den) bootstrapas den från högsta befintliga `NodeId` bland
  ordrarna på disk + 1, annars 1 — migreringsverktyget (se den punkten nedan) förväntas skriva filen
  direkt för riktiga volymer istället för att förlita sig på skanningen.

- [x] **Flytta `EditedBy`-uppslaget till Elasticsearch**
  `GetLocksForCurrentMember` (rad 184, `.Where(x => x.EditedBy == memberId)`) är den **enda** frågan
  som inte är en nyckeluppslagning. Med filer skulle den bli en full katalogskanning, vilket är
  oacceptabelt över Azure Files vid er volym. ES indexerar redan `editedBy` — låt frågan gå dit i
  stället. Liten ändring, men den måste göras, annars blir sidladdningen långsammare för varje order
  som tillkommer.

  **Genomfört 2026-09-16.** `FileOrderItemManager.GetLockedOrderItems` gör
  `_orderItemSearcher.Search("editedBy:\"" + memberId + "\"")`. Enda konsumenten
  (`OrderItemSurfaceController.GetLocksForCurrentMember`) läser bara `item.NodeId` från resultatet,
  så det spelar ingen roll att ES-dokumenten (till skillnad från EF-frågan, som körde med
  `LazyLoadingEnabled=false` och inga `Include()`) råkar innehålla hela aggregatet — en förbättring,
  inte en regression.

- [x] **Gör skrivningarna atomära och trådsäkra**
  Skriv till temporär fil och byt namn (`File.Move`/`File.Replace`) så att en avbruten skrivning
  aldrig lämnar en halv order på disk. Lås per order vid läs–ändra–skriv-cykler.
  En fil per order ger **bättre** isolering än dagens `members.json`-mönster: två användare som
  redigerar olika ordrar rör aldrig samma fil. Samma krav som i fas 0a, men här från början.
  Notera att appen har en uttrycklig låsfunktion i domänen (`LockOrderItem`,
  `TakeOverLockedOrderItem`, `EditedBy`) — samtidig redigering är alltså ett designat scenario, inte
  ett kantfall.

  **Genomfört 2026-09-16.** Skrivning: temp-fil + `File.Replace`/`File.Move`, samma mönster som
  `MemberFileStore` (fas 6). Låsning: en `SemaphoreSlim(1,1)` per `NodeId`
  (`ConcurrentDictionary<int, SemaphoreSlim>`), tagen när en order först läses in för mutation och
  släppt när ändringen flushas eller (vid undantag) kastas bort — inte `lock`/`Monitor`, eftersom
  Enter/Exit för en och samma order kan ske i olika anrop (batchningen ovan) och `Monitor` kräver att
  samma anropsramverk släpper det som togs.

- [x] **Bryt den cirkulära kopplingen `Notifier` ↔ `OrderItemManager`**
  `Bootstrapper.cs:191-199` konstruerar båda manuellt och kopplar ihop dem med
  `notifier.SetOrderItemManager(...)` + `orderItemManager.SetNotifier(...)`.
  Halva cirkeln är redan död: `Notifier._orderItemManager` sätts men **läses aldrig** (fas 0b). Ta
  bort `SetOrderItemManager` helt och låt `Notifier` ta `IHubContext<NotificationHub>` via
  konstruktorn. Behövs oavsett lagringsval, men blir enklare nu när ingen `DbContext`-livstid ska
  koordineras.

- [x] **Flytta ES-indexeringen ut ur `SaveChanges`**
  `OrderItemsDbContext.SaveChanges` är idag overridad och pushar ändringar till Elasticsearch samt
  notifierar via `_notifier`. När `DbContext` försvinner måste den logiken flytta till den nya
  lagringsimplementationen. Det är ett bra tillfälle att göra ordningen explicit: skriv fil →
  indexera i ES → notifiera, med tydlig felhantering om ES-steget misslyckas (idag kan fil och index
  hamna i otakt utan att någon märker det).
  **Skriv characterization-tester för spara-flödet innan detta rörs.**
  `EntityFrameworkOrderItemManagerTest.cs` har idag bara 2 tester, och båda anropar den privata
  `FillOutStuff` via reflection — spara-vägen är helt otestad.

  **Genomfört 2026-09-16, tillsammans med "Implementera filbaserad IOrderItemManager" ovan.**
  `FileOrderItemManager.Flush` gör ordningen explicit: skriv fil → `_orderItemSearcher.Added`/
  `.Modified` → `_notifier.ReportNewOrderItemUpdate` om `doSignal`. Ingen egen felhantering runt
  ES-steget lades till utöver att undantag där redan förhindrar att den lokala pending-bufferten
  och låset lämnas i ett inkonsekvent tillstånd (`DiscardPending` i `catch`) — fil och index kan
  fortfarande hamna i otakt om ES-anropet kastar efter att filen redan skrivits, precis som i
  EF-versionen (där `SaveChanges` kunde lyckas skriva till SQL men sedan kasta i
  ES-indexeringsloopen). Inte en regression, men inte heller åtgärdat här; en riktig lösning
  (t.ex. en reindexeringskö) hör hemma i fas 10:s "återställningsväg om indexet tappas".
  Characterization-testerna för spara-flödet beskrivs under föregående punkt.

- [ ] **Bygg engångsmigreringen från SQL till filer**
  Ett fristående verktyg som läser den befintliga databasen och skriver ut en fil per order.
  Måste vara **verifierbar**: jämför antal poster, och stickprovsjämför fullständiga aggregat
  (inklusive loggposter och bilagor) mellan databas och fil. Kör om ES-indexeringen efteråt och
  jämför träffantal mot databasen.
  Detta verktyg är dessutom vad som producerar testdatan till utvecklingsmiljön — kör det mot en
  anonymiserad kopia (appen har redan `AnonymizeOrder`). Se punkten om testdata vid brytpunkten.

- [ ] **Sätt upp backup av orderfilerna**
  Databasen gav point-in-time-återställning; filer gör det inte automatiskt. Å andra sidan är JSON
  triviala att kopiera — en schemalagd kopiering till Blob Storage räcker långt, och ger dessutom en
  läsbar arkivform. Bestäm frekvens och retention innan driftsättning.

---

## Fas 8: Externa tjänster & NuGet-paket

Fullständig paketinventering: `Chalmers.ILL/packages.config` har **37 paket**,
`Chalmers.ILL.Tests/packages.config` har **ett** (`EWS-Api-2.0`) — testramverket kommer helt från
GAC/VS, inte NuGet.

- [x] **Ta bort EWS helt (Graph är bekräftat i drift)**
  Ta bort: `Mail/ExchangeMailWebApi.cs`, `Mail/IExchangeMailWebApi.cs` (döp om interfacet till något
  leverantörsneutralt, t.ex. `IMailWebApi`), `EWS-Api-2.0`-paketet, if/else-grenen i
  `Bootstrapper.cs:145-152` och appSetting-nyckeln `UseMicrosoftGraphMailService` med dess property i
  `IConfiguration`/`DefaultChillinConfiguration.cs`.
  Detta tar samtidigt bort `SecureString`-hanteringen (`ExchangeMailWebApi.cs:412, 414, 439`) och de
  tre `Marshal`-anropen för unmanaged minne (rad 444, 445, 449), samt
  `ServicePointManager.ServerCertificateValidationCallback`-overriden på rad 34 (en global
  certifikatvalidering-override — bra att den försvinner).

  **Kritiskt detaljfynd som tidigare saknades:** `Models/Mail/MailQueueModel.cs:5,20` har
  `using Microsoft.Exchange.WebServices.Data;` och `public ItemId Id { get; set; }` — en **EWS-typ i en
  domänmodell**. Även Graph-vägen är alltså bunden till EWS-assemblyn:
  `MicrosoftGraphMailWebApi.cs:196` gör `m.Id = mailData.id.ToString()` (fungerar via `ItemId`s
  implicita strängkonvertering) och rad 124, 137, 141 stoppar in `mqm.Id` i Graph-URL:er.
  **`EWS-Api-2.0` kan inte tas bort förrän `MailQueueModel.Id` bytts till `string`.**

  Bra nyhet: `MailService.cs` är redan helt EWS-agnostisk — ingen `using`, ingen EWS-typ i någon
  signatur eller body. Den behöver inte förenklas, bara följa med interfacets namnbyte.

- [x] **Byt `WindowsAzure.Storage` (7.1.2) mot `Azure.Storage.Blobs`**
  Avgränsat till `MediaItems/BlobStorageMediaItemManager.cs` (129 rader): `CloudStorageAccount` (3),
  `CloudBlobClient` (3), `CloudBlobContainer` (3), `CloudBlockBlob` (5), plus 16 medlemsanrop
  (`UploadFromStream`, `FetchAttributes`, `Metadata[...]`, `SetMetadata`, `Properties.ContentType`,
  `SetProperties`, `DownloadToStream`, `Delete`). Allt är synkront legacy-API.
  `IMediaItemManager`-signaturerna kan behållas men implementationen skrivs om
  (`CreateIfNotExists` → `CreateIfNotExistsAsync`, `FetchAttributes` → `GetPropertiesAsync` osv.) —
  eftersom kodbasen är helt synkron behöver ni bestämma om det här är stället att införa `async`,
  eller om ni blockar med `.GetAwaiter().GetResult()` tills vidare. **Blockering i Kestrel är
  riskablare än i IIS** (trådsvält); föredra `async` hela vägen upp genom `IMediaItemManager`.
  Tar med sig `Microsoft.Data.OData`, `Microsoft.Data.Edm`, `Microsoft.Data.Services.Client`,
  `System.Spatial` och `Microsoft.Azure.KeyVault.Core` i fallet (transitiva OData-beroenden, ingen
  egen kodanvändning).

- [x] **Byt QR-renderingen från `System.Drawing` till `PngByteQRCode`**
  `OrderItemDeliverySurfaceController.cs:115-126`. `System.Drawing.Common` är Windows-only från .NET 6
  och kastar `PlatformNotSupportedException` på Linux.
  **Krävs trots att driften är Windows** — utan bytet går följesedels-/leveranssidan inte att köra
  eller testa i utvecklingsmiljön, och det är just den sidan som genererar QR-koderna som måste
  verifieras (fas 11).
  `QRCoder` har redan `PngByteQRCode` som ger `byte[]` direkt och eliminerar rad 119-124 inklusive
  hela `System.Drawing`-beroendet — **byt renderare, inte bibliotek**.
  Bilden sparas aldrig till fil och streamas aldrig; den base64-kodas till en data-URI som konsumeras i
  `Views/Partials/DeliveryType/ArticleInTransit.cshtml:58`. Fixa MIME-typen i samma veva (fas 0b).
  Detta är den **enda** `System.Drawing`-användningen i C#-kod i hela projektet.

- [x] **Ta bort `Npgsql` (inte uppgradera)**
  Den tidigare listan sa att Npgsql var "aktivt använt i `Patron/Sierra.cs`". Det stämmer bara i den
  meningen att `Sierra.cs` är dess enda konsument — och `Sierra.cs` är bekräftat död kod (fas 0b).
  Rätt åtgärd är alltså borttagning. Kontrollera att de två döda `using Npgsql;`-raderna är städade
  först.

- [x] **Ta bort döda paket utan kodanvändning**
  `MySql.Data` (ingen `MySqlConnection`/`MySql.Data.*` i någon `.cs`, bara `<DbProviderFactories>` i
  Web.config), `Microsoft.AspNet.Mvc.FixedDisplayModes`, `Microsoft.Web.Infrastructure`, `jQuery` 1.6.4
  (se fas 5).

- [x] **Ta bort hela OWIN- och MVC5-paketstacken**
  Faller bort med hostingbytet men saknades i den tidigare listan och måste bort ur `packages.config`:
  `Microsoft.Owin` 4.2.2, `Microsoft.Owin.Host.SystemWeb` 4.2.2, `Microsoft.Owin.Security` 4.2.2,
  `Owin` 1.0, `Microsoft.AspNet.Mvc` 4.0.20710.0, `Microsoft.AspNet.Razor` 2.0.20710.0,
  `Microsoft.AspNet.WebPages` 2.0.20710.0 (som är källan till `System.Web.Helpers.Crypto`, se fas 3),
  alla fyra `Microsoft.AspNet.WebApi*` 5.2.0, samt de fyra `Microsoft.AspNet.SignalR*` 2.0.3
  (inklusive `.JS`-paketet).

  **Notera versionsavvikelsen:** projektet beskrivs som MVC5, men `Microsoft.AspNet.Mvc` är
  **4.0.20710.0**, och både `Web.config` och `Views/Web.config` binder `System.Web.Mvc` → **4.0.0.0**.
  Det är MVC4, inte MVC5. Påverkar inget i migreringen men förklarar varför vissa MVC5-recept inte
  matchar koden.

- [ ] **Uppgradera `Elasticsearch.Net`/`NEST` 6.3.1**
  Används via `ElasticClient`-registreringen i `Bootstrapper.cs:142` och
  `ElasticSearchOrderItemSearcher` (den aktiva `IOrderItemSearcher`). NEST 6.x är gammalt och sedan
  några år ersatt av `Elastic.Clients.Elasticsearch`, som har ett annorlunda API.
  **Kontrollera den faktiska Elasticsearch-serverversionen i drift innan valet görs** — den avgör om
  det räcker med en versionsuppgradering inom NEST eller om hela klientbytet krävs.

- [ ] **Uppgradera övriga låsta paketversioner**
  `Newtonsoft.Json` 8.0.1 (mycket gammal; överväg `System.Text.Json` för nya ställen men behåll
  Newtonsoft där `[JsonProperty]`-attribut används i modellerna), `HtmlAgilityPack` 1.4.6,
  `log4net` 2.0.12, `QRCoder` 1.4.3, `Microsoft.Identity.Client` 4.48.1 /
  `Microsoft.IdentityModel.Abstractions` 6.22.0 (MSAL — används för Graph-autentisering, har fullt stöd
  på modern .NET, bara versionsbump).
  Ta samtidigt bort de 10 `ServicePointManager.SecurityProtocol |= Tls12`-anropen
  (`Repositories/FolioRepository.cs:93`, `Patron/FolioPatronDataProvider.cs:177`,
  `Connections/FolioConnection.cs:60, 99`, `Mail/MicrosoftGraphMailWebApi.cs:47, 402, 434, 472, 514,
  546`) — de är Framework-legacy och no-ops på modern .NET.

- [x] **Rensa `packages\`-katalogen från obsoleta mappar**
  Ligger kvar på disk men finns inte i någon `packages.config` (kvarlämning efter Umbraco-borttagningen):
  `UmbracoCms.6.1.6`, `UmbracoCms.Core.6.1.6`, `ClientDependency*`, `Lucene.Net.2.9.4.1`,
  `MiniProfiler.2.1.0`, `SharpZipLib.0.86.0`, `xmlrpcnet.2.5.0`, `CommonServiceLocator.1.0`,
  `Unity.Mvc4.1.4.0.0`, `Unity.WebAPI.5.1`. Hela `packages\` försvinner med PackageReference.

  **Redan gjort.** Verifierat 2026-09-16: `packages\` finns inte längre på disk någonstans i
  repot — försvann redan när `packages.config` togs bort i fas 1a:s PackageReference-konvertering.
  Ingen kodändring behövdes, bara ikryssning.

---

## Fas 9: Tester

Nuläge: **23 `[TestClass]`, 151 `[TestMethod]`** (verifierat), inga `[Ignore]`/`[DataRow]`/
`[TestCategory]`.

- [x] **Byt från MSTest v1 till MSTest v3**
  `Chalmers.ILL.Tests.csproj:81` refererar `Microsoft.VisualStudio.QualityTools.UnitTestFramework`
  Version 10.0.0.0 — GAC-referens utan HintPath, dvs. den allra äldsta MSTest-varianten. Projektet har
  dessutom legacy-testprojekt-GUID (`{3AC096D0-...}`), `<TestProjectType>UnitTest</TestProjectType>`
  och en `Microsoft.TestTools.targets`-import.
  Byt till `MSTest.TestFramework` + `MSTest.TestAdapter` + `Microsoft.NET.Test.Sdk`.
  `Assert.IsInstanceOfType`, `CollectionAssert.AreEqual`, `[TestInitialize]`/`[TestCleanup]` finns kvar
  i MSTest v3, så assertions-koden är i stort sett portabel.
  **Görs redan i fas 1a**, inte här — det är bytet till MSTest v3 som gör `dotnet test` möjligt och
  därmed hela den gröna kontrollpunkten. Punkten står kvar här för sammanhangets skull.
  Lägg till **Moq** (eller NSubstitute) samtidigt — projektet har inget mockningsbibliotek alls idag.
  Notera ordningsberoendet: **Microsoft Fakes måste bort först** (fas 0b), annars går sviten inte att
  köra under `dotnet test`.

  **Åtgärdat 2026-09-16.** Själva ramverksbytet var redan klart sedan fas 1a (verifierat:
  `Chalmers.ILL.Tests.csproj` refererar bara `MSTest.TestFramework`/`MSTest.TestAdapter` 3.6.4, ingen
  spår av `Microsoft.VisualStudio.QualityTools.UnitTestFramework` eller legacy-testprojekt-GUID).
  Kvarstod bara Moq-tillägget. Tillagt (4.20.72) med ett minimalt bevis-test,
  `Infrastructure/MoqSmokeTest.cs`, som mockar `INotifier` och verifierar anropet — bara för att
  bevisa att det faktiskt löser sig och fungerar under `dotnet test` på `net10.0`. Befintliga tester
  rörs inte: handskrivna stubbar förblir det etablerade mönstret för dem, Moq är bara tillgängligt för
  nya tester som vill ha det.

- [ ] **Skriv om `RoutingTest.cs` — den dyraste enskilda testposten**
  11 tester som bygger en tom `RouteCollection`, kör `RouteConfig.RegisterRoutes(routes)` och matchar
  URL:er via tre handskrivna stubbar: `StubHttpContext : HttpContextBase` (rad 160),
  `StubHttpRequest : HttpRequestBase` (rad 175), `StubServerUtility : HttpServerUtilityBase` (rad 188).
  `RouteCollection`, `HttpContextBase`, `HttpServerUtilityBase` och `StopRoutingHandler` finns
  **inget** av i ASP.NET Core. Alla 11 måste skrivas om från grunden — lämpligen som riktiga
  integrationstester med `WebApplicationFactory`, vilket faktiskt ger *bättre* täckning än idag
  (de nuvarande testerna verifierar route-matchning men aldrig att en request landar rätt).
  Täckningen som måste bevaras: default-route, `/ChalmersILLOrderListPage/Index`,
  `umbraco/surface/...`-aliaset (QR-koderna!), `/bestaellningar`, `/disk`,
  `/bestaellningar/instaellningar` — var och en med och utan avslutande slash.

- [ ] **Ersätt `HttpContextBase`-baserad testinfrastruktur i controllertesterna**
  33 controller-instansieringar i 10 filer, alla med direkt `new` och handskrivna stubbar. **Tre olika
  HttpContext-strategier används parallellt**, och bara en av dem är svår:
  - **Svår:** riktig `new HttpContext(request, response)` + `HttpContextWrapper` —
    `MediaItemControllersTest.cs:88-91`, `OrderItemSurfaceControllerTest.cs:129-132`,
    `PageControllersTest.cs:190-193`. Konstruktorn finns inte i Core; byt till `DefaultHttpContext`.
  - **Enkel:** handskriven `HttpContextBase`-subklass — `PasswordSurfaceControllerTest.cs:50-78`.
  - **Trivial:** ingen HttpContext alls — `MemberAdminSurfaceControllerTest.cs`,
    `LogItemSurfaceControllerTest.cs` m.fl.

  **Följdändring:** alla `IMemberInfoManager`-metoder tar `HttpRequestBase`/`HttpResponseBase` som
  parametrar. När interfacet ändras (fas 2) slår det igenom i fyra teststubbar:
  `PageControllersTest.cs:198-208`, `OrderItemSurfaceControllerTest.cs:137-145`,
  `PasswordSurfaceControllerTest.cs:80-88`, `Members/MemberInfoManagerTest.cs:154-156`.

- [x] **Ta bort döda referenser ur `Chalmers.ILL.Tests.csproj`**
  Direkta assembly-referenser till `System.Web`, `System.Web.ApplicationServices`,
  `System.Web.Extensions`, `System.Web.Abstractions`, `System.Web.Helpers`, `System.Web.Http`,
  `System.Web.Mvc`, `System.Web.Routing`, `Microsoft.Exchange.WebServices`,
  `Microsoft.Practices.Unity` faller bort naturligt vid SDK-style-konverteringen.
  Ta bort `app.config` (17 binding redirects, samtliga irrelevanta i modern .NET) och HintPath-
  referensen till `..\Chalmers.ILL\bin\System.Web.Mvc.dll`.

  **Redan gjort.** Verifierat 2026-09-16: `Chalmers.ILL.Tests.csproj` har idag bara fem
  `PackageReference` (log4net, Mvc.Testing, Test.Sdk, MSTest.TestAdapter/TestFramework) plus en
  `ProjectReference` och en `FrameworkReference` — inga assembly-`<Reference>`, inget `app.config`,
  ingen HintPath. Föll bort naturligt i fas 1a/1b:s SDK-style- och TFM-svep. Ingen kodändring
  behövdes, bara ikryssning.

- [ ] **Täck luckorna som granskningen avslöjade**
  Utöver omskrivningarna ovan saknas tester helt för: `LoginSurfaceController.HandleLogin`,
  `PasswordSurfaceController.ChangePassword`s success-väg, `[Authorize(Roles="SuperAdmin")]`,
  att oinloggade requests faktiskt avvisas, och de två maskin-till-maskin-endpointsen från fas 0a.
  Prioritera dessa **före** fas 3, inte efter.

---

## Fas 10: Deployment — Azure App Service

**Målmiljön är Azure App Service för Linux** — webbapp, inte container. Containern är enbart
utvecklingsmiljö (se brytpunkten efter fas 2), men kör samma OS som driften.

Två konsekvenser av OS-bytet:
- **Ingen IIS, ingen ANCM.** På Linux-planen körs appen direkt på Kestrel bakom App Services
  front-end. Ingen `web.config` genereras vid publicering, till skillnad från Windows-planen. Det
  betyder också att `Web.config` försvinner helt ur projektet efter fas 2 och 6 — det finns ingen
  variant kvar som behöver den.
- **Ny App Service-plan krävs.** En befintlig App Service kan inte konverteras mellan Windows och
  Linux. Se den egna punkten nedan.

Appen körs redan på App Service idag: `SystemSurfaceController.IsRequestAuthorized()` gör
`Request.ServerVariables["HTTP_X_FORWARDED_FOR"].ToString().Split(':').First()`, och att klippa bort
`:port` från `X-Forwarded-For` är just Azures beteende. Den detaljen är alltså redan hanterad — men
den måste bevaras när koden byter till `ForwardedHeaders`.

- [ ] **Skapa ny App Service-plan för Linux och flytta över miljön**
  Rent infrastrukturarbete — ingen kodändring, men det måste planeras eftersom det inte går att
  konvertera den befintliga appen. Att flytta: App Settings (inkl. det som idag ligger i
  `Local.config`), custom domain, TLS-certifikat, eventuella IP-restriktioner och VNet-integration,
  samt `Always On`- och `HTTPS Only`-inställningarna. Inga connection strings — databasen är borta
  efter fas 7. Däremot måste **orderfilerna** flyttas till den nya appens `/home/data/`, och det är
  den enda datan som inte kan återskapas.
  Kör gärna den nya Linux-appen parallellt med den gamla under genomtestningen (fas 11) — den gamla
  Windows-appen med nuvarande Umbraco-version i drift kan då stå kvar orörd som återställningsväg
  ända fram till att domänen flyttas. Det är en tryggare modell än slot-swap när både OS och
  ramverk byts samtidigt.

- [ ] **Säkerställ att ICU finns i både devcontainer och App Service**
  `Templates/ElasticsearchTemplateService.cs:28` och `:134` sorterar malldescriptions med
  `String.Compare(..., new CultureInfo("sv-se"))`. På Windows sköts det av NLS, på Linux av **ICU**.
  Saknas ICU i bilden — eller sätts `InvariantGlobalization=true` i projektfilen, vilket är en vanlig
  reflex för att krympa containerbilder — blir jämförelsen invariant, och då sorteras å/ä/ö in bland
  a/o i stället för efter z. Det syns direkt som felsorterade mallar i gränssnittet, och det kastar
  inget fel.
  Sätt alltså **inte** `InvariantGlobalization`, och undvik ICU-lösa basbilder (t.ex. Alpine utan
  `icu-libs`). Verifiera sorteringen i fas 11 — den är lätt att kontrollera och lätt att missa.
  Se även `Extensions/DateTimeExtensions.cs:13`, som formaterar med `CultureInfo.CurrentCulture`;
  formatsträngen är explicit så risken är liten, men beteendet skiljer sig om ingen locale är satt.

- [ ] **Gör sökvägarna till `members.json` och `chillinPrevalues.json` konfigurerbara**
  Efter fas 2 läses båda relativt `IWebHostEnvironment.ContentRootPath`, dvs. från `wwwroot` — och
  det duger inte i drift av två skäl: deployment skriver över katalogen, och vid Run-From-Package är
  den dessutom skrivskyddad, vilket skulle slå ut kontoadministrationssidan
  (`MemberAdminSurfaceController`) som skriver till `members.json` vid körning.

  **Beslut:** filerna läggs upp manuellt i en katalog utanför `wwwroot` som är åtkomlig via Kudu —
  under `/home/data/` (Linux App Service). Den lagringen är persistent och överlever driftsättningar,
  och är åtkomlig via Kudu/SSH-konsolen för manuell uppladdning.

  Implementationen blir därmed liten: gör sökvägarna till **App Settings** (t.ex.
  `Chillin:MembersFilePath`, `Chillin:PrevaluesFilePath`) med `ContentRootPath`-relativ default så att
  lokal utveckling fungerar utan konfiguration. Ingen lagringsabstraktion behövs — `MemberFileStore`
  och `ChillinOrderConfiguration` behåller sin filbaserade implementation, bara sökvägsupplösningen
  ändras. Använd `Path.Combine` och läs hemmappen via miljövariabel, så att samma kod fungerar på
  Linux lokalt och Windows i Azure.

  Låsningen och den atomiska skrivningen från fas 0a behövs fortfarande.

- [ ] **Lägg upp ett bootstrap-SuperAdmin-konto i `members.json` innan första driftsättning**
  Kontohantering sker via `MemberAdminSurfaceController`/`MemberAdminService` (inställningssidan,
  skyddat av `[Authorize(Roles = "SuperAdmin")]`), som skapar konton och hashar lösenord åt användaren
  — de ~20 kontona ska skapas där, inte skrivas för hand. Men verktyget kräver att man redan är
  inloggad som `SuperAdmin`, så minst ett konto måste ändå läggas i `members.json` manuellt (samma
  `System.Web.Helpers.Crypto.HashPassword`-metod som i [TODO-remove-umbraco.md](TODO-remove-umbraco.md))
  innan resten kan skapas via gränssnittet. Görs i samband med att filen läggs upp under `/home/data/`
  ovan.

- [ ] **Flytta statiska filer till `wwwroot/`**
  `Scripts/` (bara `chalmers.ill.js`), `Css/` (2 filer), `images/`, och det som ersätter
  `bower_components/` (fas 5). Uppdatera de hårdkodade absoluta sökvägarna i vyerna — det finns noll
  `Url.Content`/`Url.Action`-anrop, alla länkar är strängar.

- [ ] **Rensa Windows-antaganden ur sökvägshanteringen**
  Linux både i utveckling och drift, så fel här upptäcks nu i devcontainern i stället för först i
  Azure. Gå igenom:
  - **Skiftlägeskänslighet.** Särskilt mappnamnet `Config` mot `config`, som Web.config skriver som
    `configSource="config\log4net.config"` idag — med gemener **och** bakstreck.
  - **Sökvägsseparator.** `Path.Combine` överallt; aldrig hårdkodat `\`.
  - **Hemmapp/temp-kataloger** via miljövariabler i stället för hårdkodade rötter.

  Verifierat vid inventeringen: inga hårdkodade enhetsbeteckningar (`C:\`) finns kvar i C#-koden
  utöver `AzureStorageEmulatorManager.cs`, som ändå tas bort i fas 0b.

- [ ] **Ersätt HTTPS-redirecten med App Services `HTTPS Only`-inställning**
  IIS-rewrite-regeln försvinner med `Web.config`. På App Service finns HTTPS-omdirigering som en
  plattformsinställning — använd den i stället för `UseHttpsRedirection()`.
  **Varning:** används `UseHttpsRedirection()` i appen samtidigt som App Service terminerar TLS ser
  Kestrel inkommande trafik som HTTP och skickar 301 till HTTPS i en **oändlig loop**, om inte
  `ForwardedHeaders` är korrekt konfigurerat först. Plattformsinställningen undviker problemet helt.
  Bonus: den nuvarande regelns bugg med `appendQueryString="false"` (tappade query strings) försvinner
  på köpet.

- [ ] **Konfigurera `ForwardedHeaders` för App Service**
  Krävs för två saker: att `Request.IsHttps`/`Scheme` blir rätt, och att cron-serverns IP-kontroll
  fungerar. På App Service är front-endens IP varken känd eller stabil, så standardmönstret är att
  rensa `KnownProxies` och `KnownNetworks` — annars förkastas headern och `RemoteIpAddress` blir
  plattformens adress i stället för klientens. Alternativt kan
  `Microsoft.AspNetCore.AzureAppServices.HostingStartup` sköta det.
  Bevara `:port`-strippningen: Azure skickar `X-Forwarded-For` på formen `ip:port`.

- [ ] **Flytta `Local.config` till App Settings**
  App Service Application Settings blir miljövariabler och läses av `IConfiguration` utan extra kod —
  en bättre matchning än dagens `<appSettings file="Local.config">`. Connection strings läggs under
  App Services egen "Connection strings"-sektion (blir `ConnectionStrings__`-prefixade).
  Hemligheterna (`MicrosoftGraphClientSecret`, `folioPassword`, `patronCacheSolrBasicAuthPassword`,
  `librisApiKey`) bör gå via Key Vault-referenser snarare än att stå i klartext bland App Settings.
  Sätt `ASPNETCORE_ENVIRONMENT` här (se fas 6).
  Ta bort `<ExcludeFilesFromDeployment>Local.config</ExcludeFilesFromDeployment>` ur de tre
  konfigurationerna i csproj (fas 1) — mekanismen finns inte kvar.

- [ ] **⚠️ Vyerna redirectar till produktion om värdnamnet är okänt — upptäckt 2026-09-16**
  `Views/ChalmersILL.cshtml:7-15` och `Views/ChalmersILLLoginPage.cshtml:5-13` kör vid varje
  sidladdning:
  ```csharp
  var isNotLocalhost        = Request.Host.Host != "localhost";
  var isNotTestServer       = Request.Host.Host != AppSettings["testServer"];
  var isNotSecureLiveServer = !(Request.IsHttps && Request.Host.Host == AppSettings["liveServer"]);
  if (isNotLocalhost && isNotTestServer && isNotSecureLiveServer)
      Response.Redirect("https://" + AppSettings["liveServer"], true);
  ```
  Den nya Linux-appen får ett nytt värdnamn (`*.azurewebsites.net`) innan domänen flyttas. Fram till
  dess matchar varken `testServer` eller `liveServer`, och **varje besökare skickas rakt in i den
  gamla produktionsappen** — vilket är precis tvärtemot avsikten med att köra de två parallellt under
  fas 11. Sätt `testServer` till den nya appens värdnamn direkt vid uppsättningen.
  Gäller i än högre grad en isolerad testinstans, som aldrig får bounca användare till drift.

  **Bieffekt från fas 2 som inte åtgärdades:** `Response.Redirect(url, true)` i ASP.NET Core avbryter
  inte exekveringen — andra argumentet är `permanent`, inte `endResponse`. Vyn fortsätter alltså
  renderas efter redirecten. Fas 2 noterade att redirect från vy måste flytta till controllern; för
  just de här två gjordes det inte.

- [ ] **Dokumentera enkelinstans som uttrycklig förutsättning**
  Ingen SignalR-backplane behövs — beslutat, appen körs på en instans. Men beroendet är osynligt i
  koden och tyst i sitt felläge, så det ska skrivas ned där någon hittar det:
  `Notifier` skickar via `Clients.All`, vilket bara når klienter anslutna till **samma instans**.
  Skalas App Service-planen någon gång ut tappas realtidsnotiser för alla användare som hamnat på en
  annan instans, och det yttrar sig bara som "listan uppdaterar sig ibland inte" — inget fel loggas.
  ARR-affinitet (på som standard) hjälper inte, eftersom problemet är serverns utskick, inte klientens
  anslutning. Åtgärden vid en framtida utskalning är Azure SignalR Service eller Redis-backplane.
  Samma sak gäller den filbaserade lagringen — både medlemmar och ordrar: den tål inte flera
  instanser som skriver. Efter fas 7 är detta en **hårdare** förutsättning än tidigare, eftersom
  orderlagringen då saknar en databas som annars hade hanterat samtidighet.
  Lägg noteringen i README eller i CLAUDE.md:s arkitekturnoter, inte bara som en kodkommentar.

- [ ] **Slå på `Always On`**
  App Service laddar ur inaktiva appar, vilket ger kallstart för cron-anropen.
  *(Punkten om att flytta ut databasmigreringen utgår — `DbMigrator` och
  `checkForPendingDatabaseMigrations` finns inte kvar efter fas 7.)*

- [ ] **Verifiera uppladdningsgränsen mot App Service**
  Kestrels default är 30 MB och måste höjas (fas 2), men App Services front-end har en **egen** gräns
  ovanpå det. Testa en faktisk uppladdning nära den avsedda storleken innan driftsättning — det räcker
  inte att sätta `MaxRequestBodySize`.

- [ ] **Välj deploymentmetod och bygg pipeline**
  Dagens Web Deploy-baserade publicering (`Properties\PublishProfiles\` är tom i repot, profilerna är
  lokala) ersätts lämpligen av GitHub Actions eller Azure DevOps mot App Service. Pipelinen måste
  hantera asset-steget som ersätter bower (fas 5), och får **inte** röra de persistenta
  konfigurationsfilerna (se första punkten).
  Med Linux hela vägen kan pipelinen köra på en Linux-agent och publicera rakt av — samma OS som
  både devcontainern och målmiljön, utan runtime-identifierare att hålla reda på.

- [ ] **Gör den slutliga genomtestningen i Azure, inte bara i devcontainern**
  Med Linux på båda sidor är devcontainern en trogen repetition av produktion, så den här punkten är
  inte längre den riskspärr den hade varit vid ett OS-glapp — men den är fortfarande motiverad.
  Kvarvarande skillnader som bara finns i Azure: front-endens uppladdningsgräns, `X-Forwarded-*` och
  TLS-terminering, `/home`-lagringen, `Always On`-beteendet, och åtkomsten till Elasticsearch,
  Blob Storage och FOLIO från Azures nätverk snarare än från utvecklingsmaskinen.
  Kör fas 11 mot den nya Linux-appen innan domänen flyttas dit (se punkten om planbytet ovan).

- [ ] **Se över cron-anropen mot `SystemSurfaceController`**
  Endpointsen `Update` och `SendOutAutomaticMailsThatAreDue` anropas av en extern cron-server,
  auktoriserad på IP via `cronServerIpAddress`. Behåll gärna modellen, men överväg alternativen nu när
  plattformen ändå byts: en `IHostedService`/`BackgroundService` i appen, eller en Azure Function med
  timer-trigger. (Det finns idag noll bakgrundsjobb i processen — inga timers, inga trådar, ingen
  `QueueBackgroundWorkItem`.) Väljs bakgrundsjobb i appen krävs `Always On`, och utskalning gör att
  jobbet annars körs en gång per instans.

### Isolerad testserver på Linux-planen (isolerat läge, steg B)

Avstämt med användaren 2026-09-16. Förutsätter fas 7 — dessförinnan skulle testservern behöva en
egen SQL-databas, dvs. precis den externa integration hela idén går ut på att bli av med.
Steg A (fejkarna, omkopplaren, dataroten) ligger vid brytpunkten efter fas 2 och ska vara gjort.

Motivet: appen har aldrig körts skarpt i sitt nuvarande skick, och fas 7–11 är just den sträcka där
tysta regressioner uppstår. En egen instans som är helt avskuren — inga mail når låntagare, inga
poster skapas i FOLIO, inga personnummer skickas till PDB, ingen produktionsdata rörs — gör att de
faserna kan verifieras löpande i stället för först vid driftsättning.

Låt gärna den isolerade appen bli den **första** appen på den nya Linux-planen. Den provkör då
gratis, utan produktionsrisk, precis det som listas ovan som "syns bara i Azure": ICU-sorteringen,
`/home`-lagringens persistens, `ForwardedHeaders` bakom App Services front-end, uppladdningsgränsen
och `Always On`.

- [ ] **Beskär sökytan innan sökersättaren byggs**
  Görs först och avgör hur stor resten blir — varje fråga som tas bort är en frågeform som aldrig
  behöver implementeras. Inventerat 2026-09-16; samtliga `IOrderItemSearcher.Search`-anropsställen:
  `ChalmersILLOrderListPageController.cs:41` (fritext från sökrutan — **enda genuint fria ytan**) och
  `:50`, `ChalmersILLDiskPageController.cs:27`, `SystemSurfaceController.cs:186, 209, 227`,
  `ChalmersOrderItemsMailSource.cs:470`, `AutomaticMailSendingEngine.cs:188`,
  `BulkDataManager.cs:21`, `ProviderDataSurfaceController.cs:49`,
  `StatisticsSurfaceController.cs:72` (`*` — hämtar allt), `DefaultStatMngr.cs:25`
  (`StatisticsVariable.LuceneQueries`, genererade av JS i `ChalmersILLStatisticsPage.cshtml:274, 440`
  — ser fri ut men är i praktiken `fält:(v1 OR v2) AND createDate:[a TO b] AND NOT createDate:d`),
  `LibrisOrderItemsSource.cs:221, 226` (**död** — Libris avstängt), samt `AggregatedProviders()`
  (enda aggregeringen).
  Att göra: ta bort Libris-grenen; avgör om den utkommenterade `ManualAnonymizationItems`
  (`ChalmersILLOrderListPageController.cs:67`) ska tillbaka eller tas bort; skriv ned statistiksidans
  exakta frågeformer; bestäm och dokumentera vad sökrutan ska stödja. Resultatet blir en
  **frågekravlista** som är både specifikation och testfall för nästa punkt.

- [ ] **Bygg den minimala sökersättaren**
  `IOrderItemSearcher` mot fas 7:s orderfiler, i minnet: läs in alla ordrar vid uppstart och håll
  listan uppdaterad via `Added`/`Modified`/`Deleted`. Vid testserverns datamängd är linjär
  genomsökning gott och väl snabb nog, och det tar bort hela problemet med ett index i otakt.
  - **Fältuppslag:** NEST 6 härleder fältnamn i camelCase från property-namnen (`nodeId`,
    `followUpDate`, `sierraInfo.record_id`). Ingen av de sökta properties i `OrderItemModel` har
    `[JsonProperty]`. Serialisera med samma Newtonsoft-inställningar som ES-vägen och slå upp i
    `JObject` — då blir fältnamnen automatiskt identiska.
  - **⚠️ Textmatchningen är den detalj som är lätt att få fel.** ES analyserar textfält med
    standardanalysatorn, som gör om `"03:Beställd"` till tokens `03` och `beställd`. Det är därför
    `status:Beställd`, `status:03\:Beställd` och `status:"05:Levererad"` alla fungerar idag.
    Implementeras matchningen som **stränglikhet returnerar merparten av frågorna noll träffar utan
    att något fel kastas**. Tokenisera likadant: gemener, dela på icke-alfanumeriska tecken, matcha
    på token; citerat värde = alla tokens i följd.
  - **Frågespråk**, begränsat till frågekravlistan: `fält:värde`, `fält:"fras"`, `fält:(a OR b)`,
    `fält:[a TO b]` (datum, `*` som öppen gräns), `AND`/`OR`/`NOT`, `-term`, `_exists_:fält` och
    `!_exists_:fält`, parenteser, `*` = allt. Default-operatorn i ES `query_string` är `OR` — bevara.
  - `Search(query, size, fields)` behöver inte projicera på riktigt; enda anroparen
    (`SystemSurfaceController.cs:227`) läser bara `nodeId`. Sortera `CreateDate` fallande som
    ES-implementationen. `AggregatedProviders()` grupperar på `providerName`, sorterar på antal
    fallande och lägger `TIB`/`Libris`/`Subito` först (`ElasticSearchOrderItemSearcher.cs:76`).
  - Tabelldriven testsvit mot ett litet inbyggt dataset, med frågekravlistan som testfall —
    inklusive de analysatorberoende fallen.

  **Avgränsning som måste skrivas in i fas 11:** en egen utvärderare avviker från ES i kantfall,
  särskilt i sökrutan. Testservern kan därför **inte** användas för att verifiera sökbeteende — den
  verifierar att appen fungerar. Sökningen ska provas mot riktig Elasticsearch före driftsättning.

- [ ] **Sätt upp den isolerade App Service-instansen**
  Egen webbapp på Linux-planen. Samma byggartefakt som produktionsappen — skillnaden ska vara
  **uteslutande app settings**, aldrig en separat build eller gren.
  App settings: `Chillin:Isolated=true`, `Chillin:DataPath=/home/data`, `BaseUrl`,
  `testServer=<appens värdnamn>`, `showManulMailFetchingTools=true`. **Inga** Graph-, FOLIO-, PDB-
  eller ES-hemligheter — frånvaron är i sig ett räcke som uppstartskontrollen verifierar.
  `Always On` på, `HTTPS Only` på, `InvariantGlobalization` **inte** satt (ICU krävs för
  `sv-se`-sorteringen). Kudu/SSH behövs för datauppladdning till `/home/data`.
  Ingen delad deployment slot — en slot-swap mot produktionsappen skulle kunna föra över isolerat
  läge dit.
  **Sätt `testServer` direkt vid uppsättningen** — annars slår redirect-fällan ovan till och
  skickar varje besökare till produktionsappen.
  Överväg IP-restriktion: instansen har ingen riktig data, men den har heller inga spärrar, och
  `PublicDataSurfaceController.GetChillinDataForSierraPatron` är `[AllowAnonymous]` med CORS `*`.

- [ ] **Cron-jobben på testservern**
  `SystemSurfaceController.Update` och `SendOutAutomaticMailsThatAreDue` driver automatiska
  statusändringar, anonymisering och automatmail — flöden som annars aldrig provas. `testServer`-
  inställningen ovan ger redan åtkomst: `IsRequestAuthorized()` (`SystemSurfaceController.cs:157-175`)
  släpper igenom allt när värdnamnet matchar `testServer`.
  **Notera att det är en medveten försvagning** som är acceptabel enbart för att instansen inte rör
  något verkligt. `testServer` får aldrig sättas till produktionens värdnamn — det skulle öppna båda
  endpointsen för hela internet.

- [ ] **Uppdatera [README.md](README.md)**
  Setup-instruktionerna beskriver fortfarande hur man laddar ner Umbraco 6.1.6, packar upp det över
  repot och installerar ett Umbraco-paket via WebMatrix. Helt inaktuellt sedan Umbraco-borttagningen
  och blir än mer missvisande efter den här migreringen. `Prerequisites` nämner npm, bower och ett
  Exchange-konto — alla tre stämmer inte längre efter fas 5 och 8.

---

## Fas 11: Genomtestning före driftsättning

Umbraco-borttagningen och .NET Framework-borttagningen driftsätts tillsammans, efter en samlad
genomtestning. Den här listan finns för att den testningen inte ska missa de saker som **varken bygget
eller testsviten kan fånga**. Att det är en reell lucka är belagt: testsviten var grön samtidigt som
inloggningsskyddet saknades och `~/Views/Partials/`-sökvägen var trasig. Båda fångades på grenen och
inget nådde drift — men ingendera upptäcktes av en automatisk kontroll.

**Testtrappan.** Fas 11 är sista steget av flera, inte det enda:
1. Fejkade integrationer i devcontainern, löpande genom fas 3–8 (se brytpunkten efter fas 2)
2. Testversioner av FOLIO och Graph
3. Denna genomtestning, mot den nya Linux-appen i Azure

Punkterna nedan förutsätter alltså att integrationerna redan provats mot testinstanser. Det som
återstår här är helheten i skarp miljö.

**Det mesta bör redan vara avklarat innan man kommer hit.** Om Chromium/Puppeteer sattes upp vid
brytpunkten och användes löpande, är den här listan en slutlig avstämning snarare än första gången
någon tittar på appen — och flera punkter kan då köras som skript i stället för manuellt. Punkterna
som ändå kräver handpåläggning är utmärkta nedan.

**Kör den slutliga genomgången mot den nya Linux-appen i Azure, inte bara i devcontainern.** Samma OS
på båda sidor gör devcontainern till en trogen repetition, så glappet är litet — men inte noll. Det
som bara finns i Azure: front-endens uppladdningsgräns, `X-Forwarded-*` och TLS-terminering,
`/home`-lagringen, `Always On`, och nätverksåtkomsten till Elasticsearch, Blob Storage och FOLIO.
Den gamla Windows-appen kan stå kvar orörd som återställningsväg tills domänen flyttas (se fas 10).

**Ärvda, ännu overifierade punkter från Umbraco-borttagningen** (markerade "ej verifierat i webbläsare"
i [TODO-remove-umbraco.md](TODO-remove-umbraco.md)) — de är fortfarande overifierade och måste med här:

- [ ] De 12 vyer som bytte från `/umbraco/surface/{Controller}/{Action}` till `/{Controller}/{Action}`
- [ ] De ~44 anropen i `Scripts/chalmers.ill.js` som bytte samma rutt — detta är hela
  orderhanteringsgränssnittet: låsa/låsa upp, importera dokument, sätta status/typ/leveransbibliotek,
  leverans, reklamation, mail, patrondata, provider, ta emot bok, loggposter
- [ ] Att `.cshtml`-vyerna kompileras och renderas korrekt utan `RazorBuildProvider`-overriden och utan
  `UmbracoCms`-paketen
- [ ] Att mojibake-fixen håller (åäö renderas rätt) — särskilt i de 8 vyer som fick UTF-8-BOM tillagd

**Nytt som tillkommit i och med den här migreringen:**

- [ ] **QR-koder på redan utskrivna följesedlar** — 🖐️ **kräver handpåläggning.** Alias-routen
  `umbraco/surface/{controller}/{action}/{id}` måste fungera efter bytet till endpoint-routing (fas 2).
  Ett skript kan verifiera att URL:en svarar, men själva poängen är att en *fysisk, redan utskriven*
  följesedel går att skanna med en riktig telefon — inte bara att en nygenererad QR-kod fungerar.
  Detta går inte att rätta i efterhand: sedlarna är utskrivna på papper och kan inte bytas ut.
- [ ] **Partial view-upplösning.** 26 `PartialView("bart-namn")`-anrop förlitar sig på att
  `~/Views/Partials/{0}.cshtml` finns i `ViewLocationFormats`. Öppna en order och klicka igenom
  samtliga åtgärdsflikar — det var exakt det här som var trasigt sist.
- [ ] **Layout-upplösning.** De fem vyerna med `Layout = "ChalmersILL.cshtml"` (bart filnamn) — verifiera
  att sidorna får sin layout och inte renderas nakna.
- [ ] **SignalR-realtidsuppdateringar.** Öppna orderlistan i två webbläsarfönster, ändra en order i det
  ena och kontrollera att det andra uppdateras. Detta fångar både PascalCase/camelCase-problemet i
  payloaden och att `withAutomaticReconnect` fungerar. Testa även återanslutning genom att starta om
  servern med fönstren öppna.
- [ ] **Inloggning, utloggning, rollbeteende.** Logga in som konto med respektive `Desk` (ska landa på
  `/disk/`), `Administrator` och `SuperAdmin` (ska se Konton-fliken). Verifiera att befintliga
  lösenordshashar i `members.json` fortfarande fungerar efter bytet till `PasswordHasher<T>` — det är
  hela poängen med IdentityV2-kompatibilitetsläget (fas 3).
- [ ] **Lösenordsbyte**, inklusive felvägen: fel nuvarande lösenord ska ge felmeddelande, inte
  `?success=true` (fas 0a).
- [ ] **De två maskin-till-maskin-endpointsen.** `POST /SystemSurface/Update`,
  `POST /SystemSurface/SendOutAutomaticMailsThatAreDue` från cron-serverns IP, och
  `GET /PublicDataSurface/GetChillinDataForSierraPatron?recordId=...` utan inloggning. Ingendera är
  klickbar i gränssnittet och båda missas därför lätt.
- [ ] **Loggning.** Att en loggfil faktiskt skapas och fylls (fas 0a). Verifiera på Linux, där
  sökvägsseparator och skiftlägeskänslighet skiljer sig.
- [ ] **De externa integrationerna** — 🖐️ **kräver handpåläggning.** FOLIO och Graph ska vid det här
  laget vara provade mot sina testinstanser (steg 2 i testtrappan ovan); det som återstår här är att
  verifiera dem i skarp konfiguration, tillsammans med Elasticsearch-sökning och -indexering, Azure
  Blob-uppladdning/-nedladdning av bilagor, patrondata och Libris-pollning.
  Mailutskick bör gå mot en testadress i det allra sista steget — det är den integration som kan
  orsaka mest skada om den beter sig fel, eftersom den når låntagare direkt.
- [ ] **Filuppladdning och nedladdning av bilagor** — `MediaItemSurfaceController` är den enda platsen
  som returnerar `File(...)`.
- [ ] **Statiska filer** serveras korrekt från `wwwroot/` (fas 10), inklusive de tecken- och
  MIME-typskänsliga (`.svg`, `.woff`, `.ttf`).
- [ ] **Lagringsbytet** — 🖐️ **kräver handpåläggning.** Kör engångsmigreringen från SQL till filer
  (fas 7) mot en kopia och verifiera: samma antal ordrar, stickprov på fullständiga aggregat
  inklusive loggposter och bilagor, och att ES-återindexeringen ger samma träffantal som databasen.
  Kontrollera också att `NodeId`-räknaren startar över högsta befintliga id — annars återanvänds id
  som redan står på tryckta följesedlar.
  Verifiera att orderfilerna hamnar i den persistenta katalogen och överlever en deploy.

**Förberedelse som måste vara gjord innan driftsättning, inte bara testad:**

- [ ] `Config/members.json` skapad på servern med samtliga konton. Umbracos gamla lösenordshashar kan
  inte migreras, så alla ~20 konton behöver nya lösenord (se [TODO-remove-umbraco.md](TODO-remove-umbraco.md)).
  Filen är gitignorad och kopieras inte av bygget förrän `<Content Include>` lagts till (fas 1).
- [ ] `Config/chillinPrevalues.json` på plats med `"NN:Etikett"`-formatet intakt.
- [ ] Hemligheter flyttade från `Local.config`-mekanismen till miljövariabler/secrets (fas 6).

---

## Städning (kan göras oberoende, oavsett ordning)

- [ ] Ta bort `Chalmers.ILL.csproj.user` och `desktop.ini` ur projektmappen
- [ ] Ta bort `TestResults/` ur repot om den är spårad
- [ ] Överväg om `Migration/hammer-api-method.pl` och `scripts/Remove-DeadUmbracoBrowserAdapters.ps1`
  fortfarande fyller någon funktion, eller om de är engångsverktyg som kan arkiveras
- [ ] Uppdatera [CLAUDE.md](CLAUDE.md)s Arkitekturnoter-sektion när fas 2, 6 och 7 är klara — de
  beskriver läget efter Umbraco-borttagningen och blir inaktuella

- [ ] **Fyra latenta defekter hittade 2026-09-16, alla verifierade i koden**
  Ingen är brådskande — de rör tomma index respektive kosmetik — men de ska med före driftsättning,
  och de tre första blir naturliga att ta när motsvarande filbaserade implementation skrivs.
  - `ElasticsearchTemplateService.CreateTemplate` rad 107-108 gör
    `(response.Aggregations["max_id"] as ValueAggregate).Value.Value` — `NullReferenceException`
    respektive `InvalidOperationException` på ett **tomt mallindex**, dvs. exakt vid första
    uppsättningen av en ny miljö.
  - `ChillinTextRepository.All()` gör `hit.Id` på ett `FirstOrDefault()`-resultat (NRE på tomt
    index), och `ByTextField` gör `response.Documents.First()` (`InvalidOperationException`).
    Samma sak: slår till först i en ny miljö.
  - `BlobStorageMediaItemManager` skriver `Uri.EscapeDataString(name)` till blobmetadatan (rad 48)
    men läser den **utan** `UnescapeDataString` (rad 102). `MediaItemModel.Name` kommer alltså
    tillbaka procentkodad ("Bilaga%20ett.pdf") och visas så i gränssnittet.
  - `FolioConnection.cs:12-15` läser fyra appSettings i **fältinitierare** med `.ToString()` —
    saknad nyckel ger `NullReferenceException` redan under DI-uppbyggnaden, inte vid första
    FOLIO-anropet. Faller bort om fas 6 gör konfigurationen injicerad.
