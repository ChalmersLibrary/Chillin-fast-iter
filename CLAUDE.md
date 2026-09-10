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

## Testrutiner

Kör alltid testerna **innan** och **efter** kodändringar för att säkerställa att befintligt beteende
inte brutits. Kör bygge och tester som **två separata PowerShell-anrop** — båda är förhandsgodkända
i `.claude/settings.local.json`:

**1. Bygg:**
```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" "Chalmers.ILL.Tests\Chalmers.ILL.Tests.csproj" /p:Configuration=Debug /v:minimal
```

**2. Kör tester (bara om bygget lyckades):**
```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe" "Chalmers.ILL.Tests\bin\Debug\Chalmers.ILL.Tests.dll"
```

Alla tester ska vara gröna innan arbetet rapporteras klart.

**OBS:** dessa kommandon gäller de nuvarande `.csproj`/`packages.config`-baserade projekten. När
`Chalmers.ILL.Tests.csproj` konverteras till SDK-style som en del av .NET Framework-borttagningen
(se [TODO-remove-dotnet-framework.md](TODO-remove-dotnet-framework.md)) byts dessa ut mot `dotnet build`/
`dotnet test` — uppdatera den här sektionen och `.claude/settings.local.json`s vitlista i samma steg som
den konverteringen görs.

## Arkitekturnoter

Följande gäller läget efter Umbraco-borttagningen, dvs. utgångspunkten för .NET Framework-borttagningen:

- `IUmbracoWrapper`/`UmbracoWrapper` är borttagna. Inga klasser använder längre detta interface.
- `ChillinOrderConfiguration`/`IChillinOrderConfiguration` ligger kvar i namnrymden `Chalmers.ILL.UmbracoApi`
  men är **inte** Umbraco-typer — de är appkonfiguration. `Bootstrapper.cs` importerar fortfarande
  `using Chalmers.ILL.UmbracoApi` av den anledningen.
- Den primära `IOrderItemManager` är `EntityFrameworkOrderItemManager` (EF6 mot SQL Server).
  **Den ska ersättas av en filbaserad implementation i fas 7** — interfacet behålls, implementationen
  byts. Skriv inte ny kod som förutsätter en databas.
- Datamodellen är redan ett dokumentaggregat: varje läsning hämtar hela ordern med `LogItemsList`,
  `AttachmentList` och `SierraInfo`. All sökning, statistik och bulkdata går via `IOrderItemSearcher`
  (Elasticsearch), aldrig via `DbContext`.
- `INotifier.ReportNewOrderItemUpdate` har bara `OrderItemModel`-overloaden kvar; `IContent`-overloaden
  är borttagen.

## TODO-lista

När en punkt i [TODO-remove-umbraco.md](TODO-remove-umbraco.md) eller
[TODO-remove-dotnet-framework.md](TODO-remove-dotnet-framework.md) är genomförd, kryssa i den (`[ ]` → `[x]`)
direkt. Committa aldrig kod eller ändringar utan att användaren explicit ber om det.
