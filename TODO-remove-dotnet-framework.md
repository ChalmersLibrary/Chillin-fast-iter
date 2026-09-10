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

- [ ] **`PasswordSurfaceController` ignorerar returvärdet från lösenordsbytet**
  `PasswordSurfaceController.cs:43` — `user.ChangePassword(...)` returnerar `bool` som kastas bort, och
  redirect sker till `?success=true` oavsett utfall. Användaren får "lösenordet ändrat" även när det
  inte ändrades. `catch (Exception)` på rad 40-49 sväljer dessutom allt utan loggning.

- [ ] **Lösenordsbyte och rollkontroll utgår från en osignerad klientcookie**
  `MemberInfoManager` lagrar login-namnet i cookien `ChalmersILL` utan signering, utan `HttpOnly` och
  utan `Secure`. `PasswordSurfaceController.cs:35` hämtar login-namnet därifrån (inte från
  `User.Identity.Name`) innan lösenordet byts, och vyerna `ChalmersILL.cshtml:32` /
  `ChalmersILLSettingsPage.cshtml:15` gör `Roles.IsUserInRole(Model.CurrentMemberLoginName, ...)` mot
  samma cookievärde. Exploaten är begränsad (lösenordsbytet kräver fortfarande offrets nuvarande
  lösenord via `Membership.ValidateUser`, och `MemberAdminSurfaceController` skyddas av ett riktigt
  `[Authorize(Roles="SuperAdmin")]`), men designen är fel. Byt till `User.Identity.Name`/`User.IsInRole`.
  Görs lämpligen som en del av fas 3, men noteras här eftersom det är ett befintligt fel.

- [x] **`Uri.EscapeUriString` korrumperar cookien vid vissa tecken**
  `MemberInfoManager.cs:53-55` och `:67-69` escapar cookie-subvärden med `Uri.EscapeUriString`. Den
  escapar inte `&`, `=` eller `;` — precis de tecken som avgränsar subvärden i en `System.Web`-cookie.
  Ett `memberText` som innehåller något av dem gör cookien osammanhängande. Metoden är dessutom
  obsolet sedan .NET 5 (`SYSLIB0013`) och kommer att ge byggvarning efter fas 1. Rätt verktyg är
  `Uri.EscapeDataString`. Faller sannolikt bort helt när cookiehanteringen skrivs om i fas 3 — men om
  den överlever dit ska den fixas.

- [ ] **Utloggning kräver inloggning**
  `ChalmersILLLogoutPageController` har varken `[AllowAnonymous]` eller `[Authorize]` och täcks därmed
  av det globala filtret. En användare vars auth-cookie gått ut men som har kvar den osignerade
  `ChalmersILL`-cookien kan alltså inte nå utloggningssidan för att rensa den. Troligen ofarligt, men
  bekräfta att det är avsiktligt när fas 3 görs — annars `[AllowAnonymous]`.

- [ ] **`MemberFileStore` är varken trådsäker eller atomisk, och sväljer läsfel tyst**
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

- [ ] **`members.json` saknas fortfarande**
  `Chalmers.ILL/Config/` innehåller bara `members.example.json`. Utan den riktiga filen kan ingen logga
  in (och `MemberFileStore` säger det inte, se ovan). Filen är dessutom inte `<Content Include>`-ad i
  `Chalmers.ILL.csproj`, till skillnad från `chillinPrevalues.json` — den kopieras alltså inte av
  bygget utan måste placeras manuellt på servern. Exempelfilens `"see below"`-kommentar pekar
  dessutom på ingenting (JSON stödjer inte kommentarer); instruktionen för hur en hash genereras finns
  bara i [TODO-remove-umbraco.md](TODO-remove-umbraco.md).

---

## Fas 0b: Förarbete (fristående, låg risk)

Allt här är oberoende av hostingbytet och minskar migreringens yta — mest genom att ta bort kod som
annars måste migreras i onödan. Gör detta först. Liksom fas 0a hör det hemma på grenen
`remove-dotnet-framework`, inte på `master`.

- [ ] **Ta bort Microsoft Fakes ur testprojektet — betydligt enklare än det ser ut**
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

- [ ] **Ta bort bekräftat död kod**
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

- [ ] **Ta bort orörda Umbraco-kvarlämningar på disk innan SDK-style-konverteringen**
  Följande mappar under `Chalmers.ILL/` har **noll git-spårade filer** men ligger kvar på disk:
  `Install/`, `Umbraco/`, `Umbraco_Client/`, `App_Plugins/`, `MacroScripts/`, `Masterpages/`, `Xslt/`,
  `App_Browsers/`, `aspnet_client/`, `UserControls/`, `App_Code/`, `Media/`.
  (`UmbracoApi/` och `Templates/` har spårade filer och är **levande appkod** — rör dem inte.
  `App_Data/` innehåller gitignorade runtime-filer.)
  Med SDK-style-projekt inkluderas filer under projektroten implicit av mönster, så dessa måste bort
  **innan** konverteringen, annars blir de plötsligt del av bygget. Samma sak gäller `bower_components/`
  — se fas 5.

- [ ] **Ta bort döda `using`-rader som ger falska träffar vid inventering**
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

- [ ] **Fixa QR-kodens felaktiga MIME-typ och GDI-läcka**
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

- [ ] **Konvertera båda projekten till SDK-style med `<TargetFramework>net48</TargetFramework>`**
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

- [ ] **Byt testramverk i samma steg — det är det som gör `dotnet test` möjligt**
  MSTest v1 (`Microsoft.VisualStudio.QualityTools.UnitTestFramework`, GAC-referens) byts mot
  `MSTest.TestFramework` + `MSTest.TestAdapter` + `Microsoft.NET.Test.Sdk` som PackageReference.
  **Microsoft Fakes måste vara borttaget först** (fas 0b) — det kräver Visual Studios testprofiler
  och fungerar inte under `dotnet test`. Se fas 9 för detaljerna.
  Ta bort legacy-testprojekt-GUID:t, `<TestProjectType>` och `Microsoft.TestTools.targets`-importen.

- [ ] **Uppdatera bygg-/testkommandon i [CLAUDE.md](CLAUDE.md) och `.claude/settings.local.json`**
  MSBuild + `vstest.console.exe` byts mot `dotnet build`/`dotnet test`. Båda `dotnet`-varianterna finns
  redan i vitlistan, så det räcker att ta bort de två gamla raderna och uppdatera CLAUDE.md:s
  Testrutiner-sektion. Görs i samma commit som konverteringen.

- [ ] **Kontrollpunkt: kör hela sviten och bekräfta 151 gröna tester**
  Fortfarande på Windows, nu via `dotnet test`. Samma antal som baseline (verifierad 2026-09-10).
  Avviker antalet ska orsaken redas ut här — inte senare, när TFM-bytet gör allt svårare att felsöka.
  **Committa vid grönt.** Det är den punkt man vill kunna gå tillbaka till.

### Fas 1b: byt till `net10.0`

- [ ] **Installera .NET SDK 10**
  Maskinen har idag bara SDK 9.0.109 (runtimes 8.0.19 och 9.0.8). Kan göras när som helst innan
  detta steg — även parallellt med fas 0 och 1a.
  *(Tidigare stod här att EF6 måste verifieras mot `net10.0`. Det behövs inte längre — EF6 tas bort
  helt i fas 7.)*

- [ ] **Byt `<TargetFramework>` till `net10.0` i båda projekten**
  **Härifrån är bygget trasigt tills fas 2 är klar.** `System.Web`, `System.Web.Mvc`,
  `System.Web.Helpers` m.fl. finns inte på modern .NET, så i princip varje controller och
  `Global.asax.cs` slutar kompilera samtidigt. Det är väntat och går inte att undvika — men det är
  också skälet till att 1a gjordes först.
  Räkna med att fas 1b och fas 2 utförs i ett svep. Försök inte kryssa av dem var för sig.
  Multitargeting (`net48;net10.0`) är teoretiskt möjligt men avrådes: hostinglagret ska ändå ersättas
  helt, så det skulle innebära `#if`-direktiv genom hela kodbasen till ingen nytta.

---

## Fas 2: Hosting — System.Web → ASP.NET Core

- [ ] **Ersätt `Global.asax`/`Global.asax.cs` **och** `EventHandlers/OwinStartup.cs` med `Program.cs`**
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

- [ ] **Migrera det globala `[Authorize]`-filtret**
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

- [ ] **Migrera `App_Start/RouteConfig.cs` till endpoint-routing**
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

- [ ] **Ersätt `App_Start/ViewEngineConfig.cs`**
  Klassnamnet är missvisande — den registrerar ingen view engine, den *muterar* den befintliga
  `RazorViewEngine` genom att lägga `~/Views/Partials/{0}.cshtml` i både `PartialViewLocationFormats`
  och `ViewLocationFormats`. I Core: `services.Configure<RazorViewEngineOptions>(o => { ... })` på
  `ViewLocationFormats`.
  **Omfattningen är större än TODO:n tidigare angav:** `Views/Partials/` innehåller **27** `.cshtml`
  (15 direkt, 7 i `DeliveryType/`, 5 i `Settings/`), och **26 `PartialView("bart-namn")`-anrop** i
  controllers är beroende av patchen. Att missa den bröt i praktiken hela orderhanteringen förra gången
  (se [TODO-remove-umbraco.md](TODO-remove-umbraco.md)).

- [ ] **Ersätt `Web.config`s `<system.webServer>`- och `<system.web>`-sektioner**
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

- [ ] **Ta bort `<sessionState>` — Session används inte alls**
  Verifierat: **noll** träffar på `Session[...]`, `HttpContext.Current.Session` eller `SessionState` i
  hela `Chalmers.ILL/`. `<sessionState mode="InProc" ...>` i Web.config är död konfiguration.
  Ingen session-middleware ska läggas till i Core — hela frågan om in-memory/Redis/SQL-backend bortfaller.

- [ ] **Ta bort `<DbProviderFactories>`**
  Web.config rad 79-86 registrerar `MySql.Data.MySqlClient` (6.6.5.0) och `System.Data.SqlServerCe.4.0`.
  Båda är Umbraco-arv, ingen av dem används i kod, och ingendera finns i modern .NET.

- [ ] **Ersätt `Request.ServerVariables`, `Request.Params` och besläktade `System.Web`-API:er**
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

- [ ] **Ersätt `HttpContext.Current` med injicerad `IHttpContextAccessor`**
  Exakt 4 anropsställen i 2 filer, alla mönstret `HttpContext.Current?.User?.Identity?.Name`:
  `Members/MemberInfoManager.cs:31, 41, 66` och `OrderItems/EntityFrameworkOrderItemManager.cs:1759`.
  Alla är redan null-säkra. **Men:** `MemberInfoManager` tar samtidigt `HttpRequestBase`/
  `HttpResponseBase` som metodparametrar och läser statiskt från `HttpContext.Current` — blandad
  abstraktionsnivå. Interfacet `IMemberInfoManager` måste ändras, vilket slår igenom i fyra teststubbar
  (fas 9).

- [ ] **Ersätt `HttpRuntime.AppDomainAppPath` med `IWebHostEnvironment.ContentRootPath`**
  Två identiska ställen: `Members/MemberFileStore.cs:40-46` (→ `Config/members.json`) och
  `UmbracoApi/ChillinOrderConfiguration.cs:78-84` (→ `Config/chillinPrevalues.json`). Båda har
  `AppDomain.CurrentDomain.BaseDirectory` som fallback.
  **Notera:** `Server.MapPath`/`HostingEnvironment.MapPath` finns inte någonstans i kodbasen — det är
  bara dessa två ställen som gör sökvägsupplösning.

- [ ] **Ta bort vestigiell ASP.NET Web API-infrastruktur**
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

- [ ] **Gör Chromium + Puppeteer tillgängligt i Linux-containern**
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

- [ ] **Se till att appen går att starta med så få beroenden som möjligt**
  För att brytpunkten ska vara användbar direkt måste inloggningssidan och startsidan gå att rendera
  utan att hela integrationsfloran är uppe. Kartlägg vad varje sida faktiskt kräver — inloggningssidan
  behöver i praktiken bara `members.json`, medan orderlistan kräver Elasticsearch och orderdetaljerna
  dessutom orderfilerna och `chillinPrevalues.json`. Ett `docker-compose` med Elasticsearch och
  Azurite behövs ändå för utvecklingsmiljön, så det arbetet betalar sig flera gånger om. Någon
  databasmotor behövs inte efter fas 7.

- [ ] **Gör appen körbar utan riktiga integrationer — containrar för det som går, fejk för resten**
  Målet är att `dotnet run` i devcontainern ska ge en fungerande app utan ett enda hemligt
  konfigurationsvärde. **Sömmarna finns redan:** samtliga integrationer ligger bakom interface som
  registreras i `Bootstrapper.cs`, så det här är registreringsarbete, inte refaktorering.

  **Dela upp de fyra — de är inte samma problem:**

  *Behövs inte längre:* **SQL Server** — databasen tas bort i fas 7, så devcontainern behöver ingen
  databasmotor alls. Orderdatan är filer i en katalog.

  *Kör som containrar (kräver inga hemligheter, ger full trohet):*
  - **Elasticsearch** — officiell image, inga credentials. **Fejka inte den här.**
    `IOrderItemSearcher.Search(string query)` tar rå Lucene-syntax — verkliga anrop ser ut som
    `status:03\:Beställd AND followUpDate:[... TO ...]` och
    `updateDate:[* TO ...] AND status:("05:Levererad" OR ...) AND (!_exists_:isAnonymizedAutomatically)`.
    En trogen fejk vore ett litet sökmotorprojekt i sig.
  - **Azure Blob Storage** — Azurite är den officiella emulatorn och ersätter den borttagna
    `AzureStorageEmulatorManager` (fas 0b).

  *Fejka (går genuint inte att nå lokalt):*
  - **FOLIO** — `FakeFolio` i `Bootstrapper.cs:25-111` implementerar **redan** alla sju interface
    (`IFolioItemService`, `IFolioRepository`, `IFolioService`, `IFolioInstanceService`,
    `IFolioHoldingService`, `IFolioCirculationService`, `IFolioUserService`) med tomma/kanonade svar.
    Den saknar bara `FakeFolioConnection`, som aldrig skrevs. **Färdigställ den i stället för att
    börja om** — flytta ut ur `Bootstrapper.cs` till en egen fil (se fas 0b, där den annars föreslås
    tas bort; behåll den om den här punkten görs).
  - **Microsoft Graph-mail** — `IExchangeMailWebApi` har sju metoder, alla enkla att fejka mot
    filsystemet: utgående mail skrivs som filer i en `outbox`-mapp, `ReadMailQueue` läser en
    `inbox`-mapp man kan släppa filer i för att simulera inkommande beställningar. Det gör dessutom
    hela mailflödet *inspekterbart* i webbläsartestningen, vilket det inte är mot en riktig brevlåda.
  - **Patrondata och Libris** — `IPatronDataProvider`, `IAffiliationDataProvider`,
    `IPersonDataProvider` (PDB/Solr) och `LibrisOrderItemsSource`. Samma mönster.
  - **`IMediaItemManager`** kan valfritt också fejkas mot en mapp (bara tre metoder) om man vill
    slippa även Azurite för en snabb start.

  **Inkoppling:** en enda miljöstyrd brytpunkt i `Program.cs` — inte per-tjänst-flaggor som dagens
  `UseMicrosoftGraphMailService` (som ändå tas bort i fas 8). Fejkregistreringarna ska bara kunna
  aktiveras i `Development`, aldrig i `Production`, och appen ska **logga tydligt vid uppstart** när
  de är aktiva. En fejk som är påslagen utan att någon märker det är farligare än ingen fejk alls.

  **Fejkarna är första steget i en trappa, inte sista ordet.** Efter det här steget testas mot
  testversioner av FOLIO och Graph, och först därefter mot skarpa tjänster. Fejkarnas uppgift är
  alltså att göra appen körbar och klickbar tidigt — inte att bevisa att integrationerna fungerar.
  Håll dem enkla och uppenbara, och lägg ingen möda på att efterlikna verkliga felsvar; det hör
  hemma i testinstans-steget.
  Gör däremot fejk**datan** medvetet besvärlig — oklassificerade ordrar (`TypeId == -1`), null-fält,
  ordrar i varje status. Det är billigt och provocerar fram samma sorts fel som
  Umbraco-borttagningen orsakade (null `Type`, tom statusdropdown), som alla var renderingsfel snarare
  än integrationsfel och därför syns redan här.

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
    från orderfilerna måste gå att köra i utvecklingsmiljön. Kontrollera om `BulkDataManager` eller
    `MaintenanceSurfaceController` redan har en väg för detta, annars behövs ett litet verktyg.
    Det behövs ändå i drift, som återställningsväg om indexet tappas.
  - **`chillinPrevalues.json`** med riktiga värden i `"NN:Etikett"`-format — utan det är
    statusdropdownen tom och ingen order går att klassificera.
  - **`members.json`** med minst tre konton, ett per roll (`Desk`, `Administrator`, `SuperAdmin`),
    så att alla tre kodvägarna går att prova.

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

- [ ] **Skriv characterization-tester för inloggnings- och auktoriseringsflödet FÖRE omskrivningen**
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

- [ ] **Skriv om `FileMembershipProvider`/`FileRoleProvider` till cookie-autentisering**
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

- [ ] **Migrera de statiska `Membership`/`Roles`-fasadanropen**
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

- [ ] **Migrera `FormsAuthentication` till cookie-autentisering**
  Två anropsställen: `LoginSurfaceController.cs:29` (`SetAuthCookie(model.Login, false)`) och
  `Page/ChalmersILLLogoutPageController.cs:25` (`SignOut()`). Web.config `<authentication mode="Forms">`
  med `loginUrl="~/ChalmersILLLoginPage"` → `CookieAuthenticationOptions.LoginPath`. Görs i samma steg
  som punkten ovan.
  Sätt samtidigt `HttpOnly`, `Secure` och `SameSite` — dagens `<forms>` har varken `requireSSL` eller
  vettigt cookienamn (`name="yourAuthCookie"`, uppenbar mall-placeholder).
  Web.config-raden `<authorization><allow users="?" /></authorization>` (som tillåter alla på
  IIS-nivå) försvinner; hela auktoriseringen vilar redan idag på MVC-filtret.

- [ ] **Lägg till `[ValidateAntiForgeryToken]` på login- och lösenordsbytes-POST**
  Finns idag på **noll** ställen i hela kodbasen. `[ValidateInput(false)]` finns däremot på 25 —
  request validation är helt borttaget i ASP.NET Core, så de attributen ska bara strykas, men det
  betyder också att det residuala XSS-skyddet från `System.Web` försvinner. Verifiera att vyerna
  output-encodar korrekt (15 `@Html.Raw`-anrop är värda en genomgång).

---

## Fas 4: Controllers & Razor-vyer

- [ ] **Byt `System.Web.Mvc.Controller` → `Microsoft.AspNetCore.Mvc.Controller` i 42 controllers**
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

- [ ] **Migrera de 10 vyerna med `@inherits System.Web.Mvc.WebViewPage`**
  Ursprungligen 12, men 2 (`MacroPartials/*`) är döda och raderas i fas 0b. Kvar:
  `ChalmersILL.cshtml`, `ChalmersILLDiskPage.cshtml`, `ChalmersILLLoginPage.cshtml`,
  `ChalmersILLLogoutPage.cshtml`, `ChalmersILLOrderListPage.cshtml`, `ChalmersILLSettingsPage.cshtml`,
  `ChalmersILLStartPage.cshtml`, `ChalmersILLStatisticsPage.cshtml`,
  `Partials/Chalmers.ILL.OrderItem.cshtml`, `Shared/ReceivedAtBranchResult.cshtml`.
  Tre av dem (`ChalmersILLLoginPage.cshtml` plus de två döda) använder **icke-generisk** `WebViewPage`
  utan `<T>`. `WebViewPage` ersätts av `@model` utan explicit `@inherits`.
  Totalt finns 38 `.cshtml` under `Views/`; övriga 26 använder redan `@model`.

- [ ] **Fixa `Layout = "ChalmersILL.cshtml"` — går sönder tyst**
  Fem vyer (`ChalmersILLLogoutPage`, `ChalmersILLOrderListPage`, `ChalmersILLSettingsPage`,
  `ChalmersILLStartPage`, `ChalmersILLStatisticsPage`) sätter `Layout` till ett **bart filnamn**.
  Layouten `ChalmersILL.cshtml` ligger i `Views/`-roten, inte i `Views/Shared/`. I ASP.NET Core löses
  `Layout` mot view location-formaten och kräver normalt full sökväg eller att filen ligger i
  `Views/Shared/`. Antingen flytta layouten till `Shared/` eller använd `~/Views/ChalmersILL.cshtml`.
  Det finns idag varken `_ViewStart.cshtml` eller `_ViewImports.cshtml` — båda bör läggas till.

- [ ] **Skriv om de två `@helper`-blocken — men inte till `@functions`**
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

- [ ] **Ersätt `.AsInt()` i `RenderOrderList`**
  Tre anrop på rad 214, 224 och 229 i `ChalmersILLOrderListPage.cshtml`. `.AsInt()` är en
  `System.Web.WebPages`-extension som inte finns i Core → `int.TryParse` eller `Convert.ToInt32`.

- [ ] **Ta bort `Views/Web.config` och `Views/Web.config.transform`**
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

- [ ] **Migrera SignalR-servern — enklare än den ser ut, men med en tyst fälla**
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

- [ ] **Ta bort NuGet-paketet `jQuery` 1.6.4 — det är oanvänt**
  Vyerna laddar bower-versionen (`~2.1.3`) från `/bower_components/jquery/dist/jquery.min.js`.
  NuGet-paketets jQuery används ingenstans. Det är alltså inte en uppgradering utan en borttagning.

---

## Fas 6: Dependency injection & konfiguration

- [ ] **Ersätt Unity med `Microsoft.Extensions.DependencyInjection`**
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

- [ ] **Migrera `ConfigurationManager.AppSettings` → `IConfiguration`/`IOptions<T>`**
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

- [ ] **Sätt upp `appsettings.json` + miljöspecifika filer**
  Det finns ingen `Web.Debug.config`/`Web.Release.config`-transform idag, så det saknas miljöuppdelning
  att bevara. Skapa `appsettings.json` + `appsettings.Development.json`/`appsettings.Production.json`.
  Kandidater för miljöspecifika värden: sökvägen till orderkatalogen (se fas 7 och 10),
  `testServer`/`liveServer`, `cronServerIpAddress`, `BaseUrl`, `ElasticSearchUrl`.
  `checkForPendingDatabaseMigrations` och `chillinOrderItemsDb` utgår helt med fas 7.
  Ersätt samtidigt de tre icke-standardkonfigurationerna `ChillinTest.Debug`/`ChillinTest.Release`/
  `ChalmersILL` i csproj (se fas 1).

- [ ] **Ta bort `<configSections>`**
  Tre poster: `log4net`, `entityFramework`, `system.web.webPages.razor`.
  `log4net` har redan en egen fil (`Config/log4net.config` via `configSource`) — den behålls som fil,
  men kopplingen via `ConfigurationManager` byts mot explicit `XmlConfigurator.Configure(...)` i
  `Program.cs`. Se fas 0a: appendern måste dessutom bytas eftersom den pekar på en Umbraco-typ.
  Alternativt: gå över till `Microsoft.Extensions.Logging` helt. Bestäm vilket — halvvägs är värre än
  antingen.
  `entityFramework`-sektionen försvinner helt med EF6-borttagningen (fas 7), liksom
  `chillinOrderItemsDb`-connection-stringen — det finns ingen databas kvar att peka ut.
  `system.web.webPages.razor` hanteras av `_ViewImports.cshtml`.

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

- [ ] **Implementera filbaserad `IOrderItemManager`**
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

- [ ] **Lös `NodeId`-genereringen — den kräver eftertanke**
  `NodeId` är idag en identity-kolumn. Den ligger i URL:er och i **tryckta QR-koder på fysiska
  följesedlar**, så den måste förbli `int` och får **aldrig** återanvändas eller kollidera.
  Behövs: en varaktig räknare med atomär uppräkning. Enkelinstans (fastställt beslut) gör detta
  hanterbart — en räknarfil skyddad av samma lås som skrivningarna, eller max+1 läst vid uppstart och
  därefter hållen i minnet. Räknaren måste överleva omstart och deploy, dvs. ligga i den persistenta
  katalogen och inte i `wwwroot`.
  Sätt startvärdet till högsta befintliga `NodeId` + 1 vid migreringen.

- [ ] **Flytta `EditedBy`-uppslaget till Elasticsearch**
  `GetLocksForCurrentMember` (rad 184, `.Where(x => x.EditedBy == memberId)`) är den **enda** frågan
  som inte är en nyckeluppslagning. Med filer skulle den bli en full katalogskanning, vilket är
  oacceptabelt över Azure Files vid er volym. ES indexerar redan `editedBy` — låt frågan gå dit i
  stället. Liten ändring, men den måste göras, annars blir sidladdningen långsammare för varje order
  som tillkommer.

- [ ] **Gör skrivningarna atomära och trådsäkra**
  Skriv till temporär fil och byt namn (`File.Move`/`File.Replace`) så att en avbruten skrivning
  aldrig lämnar en halv order på disk. Lås per order vid läs–ändra–skriv-cykler.
  En fil per order ger **bättre** isolering än dagens `members.json`-mönster: två användare som
  redigerar olika ordrar rör aldrig samma fil. Samma krav som i fas 0a, men här från början.
  Notera att appen har en uttrycklig låsfunktion i domänen (`LockOrderItem`,
  `TakeOverLockedOrderItem`, `EditedBy`) — samtidig redigering är alltså ett designat scenario, inte
  ett kantfall.

- [ ] **Bryt den cirkulära kopplingen `Notifier` ↔ `OrderItemManager`**
  `Bootstrapper.cs:191-199` konstruerar båda manuellt och kopplar ihop dem med
  `notifier.SetOrderItemManager(...)` + `orderItemManager.SetNotifier(...)`.
  Halva cirkeln är redan död: `Notifier._orderItemManager` sätts men **läses aldrig** (fas 0b). Ta
  bort `SetOrderItemManager` helt och låt `Notifier` ta `IHubContext<NotificationHub>` via
  konstruktorn. Behövs oavsett lagringsval, men blir enklare nu när ingen `DbContext`-livstid ska
  koordineras.

- [ ] **Flytta ES-indexeringen ut ur `SaveChanges`**
  `OrderItemsDbContext.SaveChanges` är idag overridad och pushar ändringar till Elasticsearch samt
  notifierar via `_notifier`. När `DbContext` försvinner måste den logiken flytta till den nya
  lagringsimplementationen. Det är ett bra tillfälle att göra ordningen explicit: skriv fil →
  indexera i ES → notifiera, med tydlig felhantering om ES-steget misslyckas (idag kan fil och index
  hamna i otakt utan att någon märker det).
  **Skriv characterization-tester för spara-flödet innan detta rörs.**
  `EntityFrameworkOrderItemManagerTest.cs` har idag bara 2 tester, och båda anropar den privata
  `FillOutStuff` via reflection — spara-vägen är helt otestad.

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

- [ ] **Ta bort EWS helt (Graph är bekräftat i drift)**
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

- [ ] **Byt `WindowsAzure.Storage` (7.1.2) mot `Azure.Storage.Blobs`**
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

- [ ] **Byt QR-renderingen från `System.Drawing` till `PngByteQRCode`**
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

- [ ] **Ta bort `Npgsql` (inte uppgradera)**
  Den tidigare listan sa att Npgsql var "aktivt använt i `Patron/Sierra.cs`". Det stämmer bara i den
  meningen att `Sierra.cs` är dess enda konsument — och `Sierra.cs` är bekräftat död kod (fas 0b).
  Rätt åtgärd är alltså borttagning. Kontrollera att de två döda `using Npgsql;`-raderna är städade
  först.

- [ ] **Ta bort döda paket utan kodanvändning**
  `MySql.Data` (ingen `MySqlConnection`/`MySql.Data.*` i någon `.cs`, bara `<DbProviderFactories>` i
  Web.config), `Microsoft.AspNet.Mvc.FixedDisplayModes`, `Microsoft.Web.Infrastructure`, `jQuery` 1.6.4
  (se fas 5).

- [ ] **Ta bort hela OWIN- och MVC5-paketstacken**
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

- [ ] **Rensa `packages\`-katalogen från obsoleta mappar**
  Ligger kvar på disk men finns inte i någon `packages.config` (kvarlämning efter Umbraco-borttagningen):
  `UmbracoCms.6.1.6`, `UmbracoCms.Core.6.1.6`, `ClientDependency*`, `Lucene.Net.2.9.4.1`,
  `MiniProfiler.2.1.0`, `SharpZipLib.0.86.0`, `xmlrpcnet.2.5.0`, `CommonServiceLocator.1.0`,
  `Unity.Mvc4.1.4.0.0`, `Unity.WebAPI.5.1`. Hela `packages\` försvinner med PackageReference.

---

## Fas 9: Tester

Nuläge: **23 `[TestClass]`, 151 `[TestMethod]`** (verifierat), inga `[Ignore]`/`[DataRow]`/
`[TestCategory]`.

- [ ] **Byt från MSTest v1 till MSTest v3**
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

- [ ] **Ta bort döda referenser ur `Chalmers.ILL.Tests.csproj`**
  Direkta assembly-referenser till `System.Web`, `System.Web.ApplicationServices`,
  `System.Web.Extensions`, `System.Web.Abstractions`, `System.Web.Helpers`, `System.Web.Http`,
  `System.Web.Mvc`, `System.Web.Routing`, `Microsoft.Exchange.WebServices`,
  `Microsoft.Practices.Unity` faller bort naturligt vid SDK-style-konverteringen.
  Ta bort `app.config` (17 binding redirects, samtliga irrelevanta i modern .NET) och HintPath-
  referensen till `..\Chalmers.ILL\bin\System.Web.Mvc.dll`.

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
