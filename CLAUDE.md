# Pågående migreringar

Projektet genomgår två migreringar i följd:

1. **Umbraco-borttagning** — klar, se [TODO-remove-umbraco.md](TODO-remove-umbraco.md) (alla punkter ikryssade).
   Behåll filen som historik och referens för arkitekturbeslut tagna under arbetet.
2. **.NET Framework-borttagning** — pågående, se [TODO-remove-dotnet-framework.md](TODO-remove-dotnet-framework.md).
   Målet är att migrera från .NET Framework 4.6.1 (`System.Web`/IIS) till modern .NET. Detta är ett
   väsentligt större ingrepp än Umbraco-borttagningen.

   **Motivet är att komma bort från Windows.** Appen ska kunna byggas, köras och testas direkt på
   Linux — så att ingen Windows-VM behövs lokalt och en Linux-devcontainer kan användas. Driften
   flyttar med: Azure App Service byter från Windows- till **Linux-plan**, vilket ger samma OS i
   utveckling och drift. Appen körs på **en instans** (ingen SignalR-backplane, filbaserad
   medlemslagring OK).
   Filen är indelad i faser (0a–11) som ska tas i ordning, och inleds med en tabell **Fastställda
   designbeslut** (target framework, lagring, driftmiljö, mail, skalning). De besluten är avstämda med
   användaren och ska inte omprövas utan ny avstämning. Notera särskilt att **databasen tas bort helt**
   — EF6 och SQL Server ersätts av en JSON-fil per order, se fas 7.

   **Fas 0a är inte migreringsarbete** utan latenta defekter som upptäcktes vid inventeringen — bl.a. att
   log4net är helt okonfigurerat sedan Umbraco-borttagningen och att två maskin-till-maskin-endpoints
   blockeras av det globala `[Authorize]`. De görs tidigt för att de är billiga och oberoende, inte för
   att de är akuta.

   **Brytpunkten efter fas 2** är den punkt där appen för första gången går att köra i webbläsare
   (Kestrel i Linux-containern). Där sätts Chromium/Puppeteer upp, och därifrån ska webbläsarkontroll
   användas löpande genom fas 3–8 — inte sparas till slutet.

   **Fas 11** är avstämningslistan för genomtestningen före driftsättning, inklusive de punkter från
   Umbraco-borttagningen som ännu är markerade "ej verifierat i webbläsare".

## Driftläge

Ingenting av Umbraco-borttagningen är driftsatt. Hela arbetet ligger på grenen `remove-dotnet-framework`;
`master` är fortfarande den driftsatta Umbraco-versionen. Backporta därför **inte** rättningar till
`master` — allt hör hemma på migreringsgrenen. Umbraco-borttagningen och .NET Framework-borttagningen
driftsätts tillsammans, efter en samlad genomtestning (fas 11).

Praktisk konsekvens: fel som hittas i den här kodbasen är latenta, inte pågående driftincidenter. Beskriv
dem som sådana. Omvänt betyder det att appen aldrig har körts skarpt i sitt nuvarande skick — bygge och
grön testsvit säger mycket lite om att den faktiskt fungerar, vilket är varför fas 11 finns.

Testtäckningen i [Chalmers.ILL.Tests](Chalmers.ILL.Tests) är bristfällig. Innan en punkt i endera TODO-listan
görs klar: lägg till eller verifiera ett characterization-test som täcker nuvarande beteende för den
berörda ytan (kontroller/flöde), om det saknas. Testet ska verifiera beteende (t.ex. via HTTP-anrop/output)
snarare än interna implementationsdetaljer (Umbraco-typer, `System.Web`-typer, etc.), så att det överlever
omskrivningen.

## Arbetsrutiner

Föredra **Edit-verktyget** framför Bash/PowerShell-kommandon när båda kan lösa uppgiften, eftersom
edits är förhandsgodkända och inte kräver användarinteraktion. Använd scripts bara när det inte finns
ett Edit-alternativ (t.ex. ta bort filer, köra byggen/tester), eller när antalet edits skulle bli
oskäligt stort.

Committa och pusha alltid ändringar direkt när en avgränsad uppgift är klar och testerna är gröna
(se [Testrutiner](#testrutiner)) — utan att fråga om lov varje gång. Detta gäller enbart grenen
`remove-dotnet-framework`; pusha aldrig till `master` (se [Driftläge](#driftläge)). Skriv commit-
meddelanden på samma sätt som befintlig historik. Om testerna inte är gröna: committa inte, utan
rapportera felet istället.

## Testrutiner

Kör alltid testerna **innan** och **efter** kodändringar för att säkerställa att befintligt beteende
inte brutits. Båda projekten är SDK-style sedan fas 1a och targetar `net10.0` sedan fas 1b, så bygge
och tester körs med `dotnet`, förhandsgodkänt i `.claude/settings.local.json`:

**1. Bygg:**
```bash
dotnet build Chalmers.ILL.Tests/Chalmers.ILL.Tests.csproj /p:Configuration=Debug /v:minimal
```

**2. Kör tester (bara om bygget lyckades):**
```bash
dotnet test Chalmers.ILL.Tests/Chalmers.ILL.Tests.csproj /p:Configuration=Debug /v:minimal
```

Alla tester ska vara gröna innan arbetet rapporteras klart.

## Arkitekturnoter

Läget efter Umbraco-borttagningen och efter fas 2/6/7 av .NET Framework-borttagningen (dvs. det
aktuella läget på grenen, inte bara startpunkten för det arbete som återstår):

- **Hosting (fas 2):** ASP.NET Core minimal hosting, `Program.cs`, Kestrel — inget `System.Web`, IIS
  eller `Global.asax`/OWIN kvar. Controllers ärver `Microsoft.AspNetCore.Mvc.Controller`, inte
  `System.Web.Mvc.Controller`. `HttpContext.Current` är borta; kontext går via
  `Microsoft.AspNetCore.Http.HttpRequest`/`HttpResponse` (t.ex. `IMemberInfoManager`s signaturer).
- **DI (fas 6):** `Microsoft.Extensions.DependencyInjection`/`IServiceCollection` via
  `Bootstrapper.RegisterTypes`, inte Unity. `IUmbracoWrapper`/`UmbracoWrapper` är borttagna helt.
- **Konfiguration (fas 6):** `IChillinConfiguration` (`DefaultChillinConfiguration`, backad av
  `appsettings.json`/`appsettings.{Environment}.json`), inte `ConfigurationManager.AppSettings`/
  `App.config`/`Web.config`. `ChillinOrderConfiguration`/`IChillinOrderConfiguration` ligger kvar i
  namnrymden `Chalmers.ILL.UmbracoApi` men är **inte** Umbraco-typer — de är appkonfiguration
  (prevalues). `Bootstrapper.cs` importerar fortfarande `using Chalmers.ILL.UmbracoApi` av den
  anledningen.
- **Lagring (fas 7):** Den primära `IOrderItemManager` är `FileOrderItemManager`
  (`Chalmers.ILL/OrderItems/`) — en JSON-fil per order under `IChillinConfiguration.DataPath/orders/`,
  inte EF6/SQL Server. Databasen är borttagen helt (inget `DbContext`, inga migrationer). Skriv inte
  ny kod som förutsätter en databas.
- Datamodellen är redan ett dokumentaggregat: varje läsning hämtar hela ordern med `LogItemsList`,
  `AttachmentList` och `SierraInfo`. All sökning, statistik och bulkdata går via `IOrderItemSearcher`
  (Elasticsearch i drift, `Isolated.InMemoryOrderItemSearcher` i isolerat läge), aldrig via `DbContext`.
- **Isolerat läge (fas 6/10):** `Chillin:Isolated=true` slår om ett antal DI-registreringar i
  `Bootstrapper.cs` till filbaserade/i-minnes-fejkar (sökning, medlemslagring, mall- och
  chillin-text-lagring m.fl.) för en avskuren testserver utan riktiga integrationer. Se
  `IsolationGuard` och "Fastställda designbeslut" ovan.
- `INotifier.ReportNewOrderItemUpdate` har bara `OrderItemModel`-overloaden kvar; `IContent`-overloaden
  är borttagen.

## TODO-lista

När en punkt i [TODO-remove-umbraco.md](TODO-remove-umbraco.md) eller
[TODO-remove-dotnet-framework.md](TODO-remove-dotnet-framework.md) är genomförd, kryssa i den (`[ ]` → `[x]`)
direkt, som en del av den commit som avslutar punkten (se [Arbetsrutiner](#arbetsrutiner) om
commit/push).
