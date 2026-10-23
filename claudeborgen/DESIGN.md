# Claudeborgen: design

Claudeborgen ger objektägaren makt att driva utvecklingen av sin tjänst tillsammans med Claude,
med snabb iteration och synliga resultat. IT-specialisten finns nära till hands, kan flika in när
problem uppstår och beslutar om vidare integrationstest och driftsättning.

Det byggs **först in i Chillin** (Chalmers.ILL), men allt ligger redan samlat under `claudeborgen/` utan
beroenden till Chillin, så att det kan brytas ut till ett eget repo och NuGet-paket genom att flytta
katalogen.

Status: byggt 2026-10-19/20, inte driftsatt. Se [Komma igång](#komma-igång) och [Ordning](#ordning).

## Fastställda beslut

Avstämda med Lars. Ompröva inte utan ny avstämning.

| Område | Beslut | Datum |
|---|---|---|
| Arbetssätt | Objektägaren driver en uppgift som ett **samtal med Claude**. Varje meddelande ger ett varv: implementation → push → automatisk driftsättning till testservern → Claude svarar med länk. Hela samtalet är en Claude Code-session. | 2026-10-19 |
| Diskussion och planering | Sker **var man vill** (Teams, IRL …). Claude-integrationen är bara implementationsplaneringen, inget forum. | 2026-10-19 |
| Placering | **Inbyggt i projektets app, där dess changelog ligger**: i Chillin på startsidan, direkt ovanför "Nytt i Chillin". Ett samtal öppnas på en egen sida. Ingen menylänk, inget på appens övriga sidor. Byggs först i Chillin och bryts sedan ut. | 2026-10-19 |
| Värdinstans | Integrationen körs på **Chillins integrerade testserver**, och senare i produktion. Lars driver ut migreringsgrenen till integrerad test för att det ska gå. | 2026-10-19 |
| Kanalen och testservern | Den **riktiga** kanalen (agent-API:t mot riktiga Claude) körs **aldrig** på den isolerade testservern som snabbgrenen driftsätts till. Kanalen där fel rapporteras får inte kunna slås ut av en trasig push. | 2026-10-19 |
| Fejkad Claude | På den isolerade testservern är integrationens gränssnitt **på**, men Claude ersätts av en **fejkad Claude i processen**, på samma sätt som Chillins övriga fejkade integrationer. Agent-API:t är avstängt där. Se [Fejkad Claude](#fejkad-claude). | 2026-10-19 |
| Grenar | `remove-umbraco` ⊂ `remove-dotnet-framework` ⊂ `claudeborgen` → mergas in i **`test`**. Därefter tas övriga grenar bort och allt arbete sker mot `test`. Repot är `ChalmersLibrary/Chillin-fast-iter`; Lars för ändringar vidare till andra repon. | 2026-10-19 |
| Roller | Rollerna **`ObjektSpecialist`** och **`ObjektSpecialistIT`** i appens rollsystem. Övriga inloggade är läsare; deras önskemål går via objektspecialisten och de kanaler de redan har. | 2026-10-19 |
| Synlighet | **Ingenting är öppet.** Alla som har inloggning i appen kan se och läsa allt i integrationen; ingen annan ser något. | 2026-10-19 |
| Lagring | **Filbaserad**, som resten av Chillin: en JSON-fil per samtal under `DataPath/claudeborgen/conversations/`. Ingen databas (Chillin har ingen sedan fas 7). | 2026-10-19 |
| Changelog | **En fil i repot** (Chillin: `Chalmers.ILL/changelog.json`) med både handskrivna poster och de som runnern committar när objektspecialisten säger **klar**. Posten följer koden genom alla miljöer, och visningen sorteras efter datum oavsett vem som skrev posten. Ett klart samtal som ännu inte finns i den driftsatta filen visas med "(på väg)". På snabbtestservern committas inget, och posten visas med "(fejk)". | 2026-10-19/21 |
| Externa integrationer | **Inga.** Inga Teams-webhooks, ingen mail. Det som behöver uppmärksamhet visas i integrationen själv. | 2026-10-19 |
| Tester | **Bygget före push, testerna efter.** Röda tester rättas av Claude i en ny commit inom samma samtal. | 2026-10-19 |
| Grön markering | Runnern flyttar git-taggen `test-green` till den senaste commit som har gröna tester. IT mergar vidare därifrån. | 2026-10-19 |
| Inloggning | Appens egen inloggning och roller återanvänds. Ingen egen inloggning. | 2026-10-19 |
| Claude körs | Claude Code i devcontainer på egen maskin, startat av **runnern** (Node, i `screen`). All trafik går **utåt** från devcontainern; appen kontaktar aldrig Claude. `devcontainer exec claude` finns kvar som reserv. | 2026-10-14/20 |
| Runner | **Node**, alltid, oavsett appens plattform. Appdelen finns per plattform (NuGet för .NET, npm för Node). Kontraktet är `PROTOCOL.md`. Runnern ligger i projektrepot (`claudeborgen/runner/`) men monteras skrivskyddad. | 2026-10-20 |
| Snabbslinga | Claude pushar direkt till snabbgrenen **`test`** → automatisk driftsättning till isolerad testserver med fejkade integrationer. IT tar det vidare till integrerad test och produktion (Lars mergar `test` vidare till andra repon). | 2026-10-14/16 |
| Taggningar | En Claude Code-session per tråd (samtal), så att Claude minns tidigare svar. | 2026-10-19 |
| Status | Claudes status (ledig / jobbar på X + fas / offline) visas tydligt **i integrationen** (startsidan och samtalssidorna), inte på appens övriga sidor. | 2026-10-14/19 |

## Roller

| Roll | Kan |
|---|---|
| Objektägare (`ObjektSpecialist`) | Starta samtal (uppgifter), skriva i dem, markera som klar eller avbruten. |
| IT-specialist (`ObjektSpecialistIT`) | Skriva i samtalen, se det som behöver uppmärksamhet, besluta om integration och produktion. |
| Läsare (alla övriga inloggade) | Se status, kö, samtal, leveranser och changelog. |
| Claude | Läsa och svara i samtal som väntar på Claude, via agent-API:t. Rapportera status. |

Rollerna hämtas från värdappen genom ett litet gränssnitt (se [Gränsen mot värdappen](#gränsen-mot-värdappen)).
Behörigheten kontrolleras på servern, inte bara genom att dölja knappar.

**I Chillin:** `ObjektSpecialist` och `ObjektSpecialistIT` blir vanliga tilldelningsbara roller, som
`Desk` och `Viewer` (satta via medlemsadministrationen). "Alla som har inloggning" följer Chillins
befintliga tvåstegsmodell: konton med minst en roll, `Viewer` inräknat, kan läsa. Konton helt utan roll
blockeras från allt, även här (`ViewerReadOnlyFilter`). Integrationens läsande actions måste därför
märkas `[AllowViewer]`.

## Flödet

```
objektägaren skriver ──► väntar på Claude ──► Claude arbetar ──► väntar på objektägaren ──┐
        ▲                                         │                                        │
        └──────────────── nästa meddelande ───────┴────────────────────────────────────────┘
                                                                     │
                                                         objektägaren: "klar" ──► changelog
```

Ett varv:

1. Objektägaren skriver i samtalet, t.ex. "Lägg till en exportknapp på listan".
2. Runnern tar samtalet. Claude implementerar och återupptar samtalets session.
3. **Bygget** körs. Om det är rött rättar Claude det innan något pushas.
4. Runnern committar (`#<id>: <rubrik>`) och pushar till `test`. CI driftsätter till testservern.
5. Claude svarar i samtalet: vad som ändrats och en länk för att prova.
6. **Testerna** körs medan driftsättningen pågår:
   - **Gröna:** `test-green` flyttas till commiten.
   - **Röda:** Claude rättar i en ny commit i samma samtal och berättar vad som hände. Om det inte går
     efter ett par försök markeras samtalet **behöver IT**.
7. Objektägaren provar och skriver nästa önskemål, eller säger **klar**. Då hamnar uppgiften i changelogen.

En liten ändring tar uppskattningsvis 3–10 minuter från meddelande till synligt resultat. Det mesta
av tiden är driftsättningen.

**Det som behöver uppmärksamhet** visas i integrationen, inte via externa kanaler: samtal där testerna
förblir röda, varv som kraschat och varv som fastnat för att Claude gick offline mitt i dem. Det syns
tydligt för `ObjektSpecialistIT`, i anslutning till Claudes status.

**Två testregler.** Varven i samtalen följer "bygget före push, testerna efter" på grenen `test`.
Det manuella migreringsarbetet följer fortfarande `CLAUDE.md`, som säger "commit bara på gröna
tester". `CLAUDE.md` ska uppdateras så att den skiljer på de två innan första varvet körs.

Det finns bara en objektägare per projekt, så runnerns "ett samtal i taget" begränsar inte i praktiken.

## Arkitektur

```
 ┌──────────── Egen maskin: devcontainern ─────────────┐        ┌───────────── Azure ──────────────────────┐
 │ runner (Node, i screen) ─── HTTPS + token, utåt ──────────────►│ Appen på integrerad test (senare prod)   │
 │   │  hjärtslag, hämta arbete, svara                 │        │  ├─ startsidan: Claude + changelog       │
 │   ▼                                                 │        │  └─ /api/claude/... (PROTOCOL.md)        │
 │ claude -p --resume <session>  (bygger i /workspace) │        │                                          │
 │   │                                                 │        │                                          │
 │   ▼ commit + push (deploy-nyckel) ─► GitHub: test ──CI──────►│ Snabbtestservern (isolerad, fejkad data) │
 └─────────────────────────────────────────────────────┘        └──────────────────────────────────────────┘
```

Allt går **utåt** från devcontainern. Appen anropar aldrig runnern och startar aldrig Claude.

### Kod

```
claudeborgen/                     hela verktyget, utan beroenden till Chillin
  DESIGN.md                       det här dokumentet
  PROTOCOL.md                     agent-API:t: kontraktet mellan runnern och appdelen
  runner/                         runnern (Node, inga beroenden), med tester (node --test)
  dotnet/Claudeborgen.Web/        appdelen för .NET (Razor Class Library → NuGet-paket)
  dotnet/Claudeborgen.Tests/      appdelens tester, bl.a. protokolltester mot en minimal värd
claudeborgen.json                 Chillins inställningar för runnern
Chalmers.ILL/ClaudeborgenHosting/ Chillins limkod: roller → åtkomst, sidskal, [AllowViewer]-konvention
```

Runnern är skriven i Node och körs alltid så, oavsett vad appen är skriven i. Det är bara appdelen
som finns per plattform: idag .NET, och en npm-version om en Node-app ska använda det. Det som binder
ihop dem är `PROTOCOL.md`.

### Appdelen (Claudeborgen.Web)

- **Startsidan:** `Views/Claudeborgen/_StartPageSection.cshtml`, som värden renderar där dess
  changelog ligger. I Chillin ligger den direkt ovanför "Nytt i Chillin". Den visar Claudes status,
  kö, "Behöver IT", pågående samtal och (för objektspecialisten) "Nytt samtal". Klara samtal hamnar i
  changelogen via `_Changelog.cshtml`.
- **Ett samtal:** `/Claude/Conversation/{id}`. Innehållet är `_ConversationPage.cshtml`, och värden
  lägger sin layout runt det (`IClaudeborgenHost.ConversationPage`). `/Claude` skickar vidare till
  startsidan.
- **Live:** SignalR-hubben `/claudeborgenHub` skickar bara "något ändrades", och
  `_content/Claudeborgen.Web/claude.js` hämtar om delarna. Sidorna hämtas också om var 15:e sekund,
  eftersom offline är frånvaron av hjärtslag och inte en händelse som kan skickas ut.
- **Skicka utan omladdning:** chattrutorna (idérutan och svarsrutan i ett samtal, märkta
  `data-claude-send`) postas av `claude.js` med huvudet `X-Claudeborgen-Send`. Då svarar
  kontrollern utan redirect (204 för ett svar, idérutan som HTML för en ny idé, 409 med texten vid
  fel), och skriptet tömmer rutan och hämtar om de levande delarna. Utan skriptet fungerar samma
  formulär som vanliga poster.
- **Agent-API:** `/api/claude/...`, se `PROTOCOL.md`. Det använder camelCase-JSON oavsett värdens
  inställningar.
- **Lagring:** en JSON-fil per samtal under `<DataPath>/claudeborgen/conversations/`, med ett enda
  lås i processen. Det förutsätter en instans, precis som Chillin.
- **Inkoppling i en värd:** `services.AddClaudeborgen(settings)`, en `IClaudeborgenHost` (roller och
  sidskal), `app.MapClaudeborgen()`, och rendering av startsidesavsnittet där changelogen ligger.
  Huruvida något registreras som "skrivskyddat" i värdens behörighetsmodell är värdens sak; i Chillin
  sköts det av `ClaudeborgenAllowViewerConvention`.

### Idéer

Att skriva rubrik och meddelande känns som en beställning, och det gör att man skriver väldigt noga.
Därför finns en lösare väg in: en **chattruta på startsidan**, "Bolla en idé med Claude".

- **Ingen rubrik.** Man skriver bara, och rubriken tas från första meddelandet.
- **Claude svarar som i en chatt:** kort, i samtalston och gärna med en motfråga. Claude hjälper till
  att avgöra om idén är värd att bygga, ungefär hur stor den är och vilka alternativ som finns.
  Claude får läsa koden för att svara konkret, men kan inte ändra något: runnern kör `claude -p`
  utan att kringgå behörigheter och med `Edit`, `Write`, `MultiEdit`, `NotebookEdit` och `Bash`
  avstängda.
- **Chatten fortsätter på startsidan** och uppdateras live, med "Claude skriver…" medan Claude tänker.
  Andra pågående idéer listas under "Idéer", och alla med inloggning kan läsa dem. Alla som får skriva
  i samtal (`ObjektSpecialist`, `ObjektSpecialistIT`) kan bolla idéer.
- **"Gå vidare och bygg detta"** (objektspecialisten) gör idén till ett vanligt arbete.
  Objektspecialistens "Nu bygger vi det här: …" blir ett meddelande i samtalet, och
  nästa varv bygger det i *samma* Claude-session, alltså med hela idédiskussionen i minnet.
- **"Rensa diskussion"** tar bort idén. Idéer som legat orörda i 14 dagar (`IdeaRetention`) tas bort
  automatiskt, med Claude-sessionen, enligt samma princip som i [Samtal tas bort](#samtal-tas-bort).
- **Idéer väntar inte på arbeten.** Runnern har en egen idéloop som svarar parallellt med ett
  pågående implementationsvarv. Det går att göra
  eftersom idéer inte rör några filer. Det är högst ett idésvar och ett arbete åt gången, och
  idéerna har egna, generösare tak mot loopar (`maxIdeaRepliesPerConversationPerHour` 30,
  `maxIdeaRepliesPerHour` 60).
- **"Nytt arbete direkt"**, med rubrik och meddelande, finns kvar ihopfälld för den som redan vet vad
  som ska göras.
- Fejken i appen svarar på idéer före väntande arbeten, och fejkrunnern svarar också.

### Changelog

- `changelogFile` i `claudeborgen.json` pekar ut filen i repot. Värden pekar ut samma fil där appen
  är driftsatt (`ClaudeborgenSettings.ChangelogPath`).
- **Handskrivna poster** läggs direkt i filen. Visningen sorterar efter datum, så det spelar ingen
  roll var i filen de hamnar.
- **Klar:** samtalet väntar på en changelog-commit. Runnern lägger in posten överst i filen,
  committar `Changelog: #<id> <text>` till grenen och rapporterar commiten. Ingen Claude och inget
  bygge, eftersom det bara är data som appen läser. Ett misslyckande loggas och görs om vid nästa
  förfrågan.
- **Startsidan** visar filens poster och dessutom klara samtal som ännu inte finns i den driftsatta
  filen, märkta "(på väg)". Posten flyttar alltså inte och försvinner inte mellan Klar och driftsättning.
  Bara ett klart samtal som ännu inte committats har länk. Poster i filen länkar aldrig, eftersom
  samtalet är borttaget och samma nummer kan vara ett annat samtal på en annan server.
- **Snabbtestservern** committar aldrig något. Fejken i appen och fejkrunnern låtsas, och posten visas
  med "(fejk)".

### Driftsättning

Snabbtestservern driftsätts via **Azure App Service "Local Git"**: appen har ett eget git-repo hos
Azure, och en push dit bygger (App Service Build Service) och driftsätter. **Runnern pushar dit
efter varje push till `test`** (`deployGitUrlFile` i `claudeborgen.json`).

Varför inte GitHub-webhooken eller GitHub Actions (avstämt med Lars 2026-10-22):
- **Webhooken från GitHub fungerar inte:** GitHub skickar API-adressen
  (`https://api.github.com/repos/…`) som `repository.url` i push-payloaden, och Azures byggtjänst
  kör `git fetch` mot den och får 403. En manuell synk fungerar, eftersom den använder den adress
  som är inställd i Deployment Center.
- **GitHub Actions** drar minuter från en kvot om repot blir privat, och runnern pushar ofta.
- **Local Git beter sig likadant oavsett om GitHub-repot är publikt eller privat**, kräver ingen
  webhook och inga Actions-minuter.

Detaljer:
- Pushen till Azure görs med `--force`. Azure-repot är ett mål för driftsättning, inte en källa, så
  det får exakt det `test` har. Grenen där är `master` (`deployGitBranch`), som Azure driftsätter
  som standard.
- Om pushen till Azure misslyckas loggas det, men varvet misslyckas inte, eftersom ändringen redan
  finns på GitHub. Nästa push driftsätter allt.
- Lösenordet skrivs aldrig ut. Adresser med uppgifter maskeras i loggarna.
- **Alla andra som pushar** driftsätter med samma kommando som runnern använder:
  `node claudeborgen/runner/deploy.mjs`. Det gäller till exempel en interaktiv Claude-session (se
  projektets `CLAUDE.md`) eller en människa i devcontainern. Kommandot pushar den aktuella commiten
  till Azure, och vägrar commitar som inte finns på GitHub än, så att snabbtestservern aldrig kör
  kod som ingen annan kan se. Den som inte har uppgifterna kan trycka Sync i Deployment Center.
- Azure sätter `SCM_COMMIT_ID` när den bygger, och commit-stämpeln använder den (se nedan).

### Driftsatt

Objektspecialisten ska kunna se när en ändring faktiskt går att prova, inte bara när den är pushad.
Ingen koppling till Azure eller CI behövs:

1. **Appen vet sin commit.** `build/Claudeborgen.Web.targets` skriver vid bygget commiten till
   `claudeborgen-version.txt` bredvid appen. Commiten tas, i den ordningen, från
   `CLAUDEBORGEN_COMMIT`, `SCM_COMMIT_ID` (Azure App Service), `GITHUB_SHA` (GitHub Actions),
   `BUILD_SOURCEVERSION` (Azure DevOps) eller `git rev-parse HEAD`. `GET /claudeborgen/version` visar den, utan inloggning.
2. **Runnern bevakar.** Vid varje förfrågan hämtar den de varv som väntar på driftsättning, frågar
   snabbtestservern (`testServerUrl` + `claudeborgen/version`) och kontrollerar med
   `git merge-base --is-ancestor` om varvets commit ingår. Det gäller även när CI hoppat över
   versioner. Bevakningen blockerar inget, och den överlever en omstart eftersom appen håller reda
   på vad som väntar.
3. **Samtalet visar det.** Varvstabellen visar "Driftsätts…" och sedan klockslaget, och
   "Prova"-länken visas med "(inte ute än)" fram till dess. När den senaste ändringen är ute kommer
   meddelandet "Ändringen är ute på snabbtestservern och går att prova".

Förutsättningar: snabbtestservern måste vara nåbar från devcontainern (brandväggen), och CI:s bygge
måste känna till commiten, antingen genom att bygga i repot eller via en av variablerna ovan.
Fejken i appen låtsas att driftsättningen tar `FakeDeployDelayMilliseconds` (standard 8 s), och
fejkrunnern rapporterar ändringen som ute direkt.

### Pausa utvecklingen

ObjektSpecialistIT kan slå av utvecklingen en stund med knappen **"Pausa utvecklingen"** i
statuspanelen, till exempel när någon behöver arbeta i grenen för hand utan risk för
mergekonflikter.

- **Det styrs i appen**, inte i runnern, och kan därför inte missas: medan utvecklingen är pausad
  lämnar appen inte ut något som pushar till git. Det gäller arbetsvarv (`GET work?kind=task` är tom
  och `claim` ger `409`), changelog-commitar och borttagning av avbrutna arbeten. Ett varv som redan
  pågår avslutas först, eftersom det inte går att avbryta mitt i på ett säkert sätt.
- **Idéchatten fungerar som vanligt**, eftersom idéer inte rör git. Meddelanden och nya arbeten går
  att skriva och väntar i kön.
- **Alla ser det:** en gul banderoll säger vem som pausade och sedan när. Bara ObjektSpecialistIT
  har knappen **"Återuppta utvecklingen"**, och den väcker runnern direkt.
- Pausen sparas i datakatalogen (`development-paused.json`) och klarar en omstart av appen.

### Avbryta ett arbete

Det finns en knapp: **"Avbryt och ta bort arbete"**. Allt arbetet har ändrat ska bort, eftersom
det inte finns något läge där man vill behålla ändringarna från ett avbrutet arbete.

- **Har arbetet pushat något** får det tillståndet "Tas bort…". Runnern reverterar alla dess commitar,
  nyaste först, i *en* commit (`#12: Avbrutet – ändringarna borttagna (rubrik)`). Den bygger först,
  pushar, kör testerna och flyttar `test-green` om de är gröna, så att `test-green` inte längre
  pekar på något med det avbrutna arbetet i. Ingen Claude är inblandad. Därefter tas arbetet bort
  som vanligt, med sessionerna.
- **Om det inte går**, till exempel vid en konflikt med senare arbete som bygger på samma kod eller ett
  rött bygge, pushas ingenting, arbetskatalogen lämnas ren och arbetet hamnar under "Behöver IT".
  Ett nytt tryck på knappen försöker igen. Har någon redan tagit bort ändringarna för hand finns
  inget kvar att revertera, och då räknas det som klart.
- **Har arbetet inte pushat något** tas det bort direkt.
- Ändringar som IT redan har fört vidare från `test` till ett annat repo påverkas inte.

### Samtal tas bort

Samtalen kan innehålla känsliga uppgifter. De sparas därför inte längre än de behövs, och de
committas aldrig till repot.

- **Klart samtal:** när runnern har committat changelog-posten raderas samtalet från datakatalogen.
  Kvar finns changelog-posten i repot (utan länk), commitarna och deras meddelanden (`#12: <rubrik>`).
- **Avbrutet samtal:** raderas direkt.
- **Claude Codes sessioner** i devcontainern innehåller samma diskussion. Runnern raderar dem för
  borttagna samtal (`GET cleanup`). Det gäller `projects/*/<id>.jsonl`, katalogen med samma namn,
  `session-env/<id>` och `file-history/<id>` under `CLAUDE_CONFIG_DIR`. Bara exakta sessions-id:n
  raderas, så interaktiva sessioner, minnet och inställningar rörs inte.
- **Kvar i appen** finns bara en kvittering per borttaget samtal (`removed.json`): nummer, datum,
  changelog-text och sessions-id:n, men ingen diskussion. Den behövs så att posten kan visas
  "(på väg)" tills den driftsatta filen har den, och så att runnern vet vilka sessioner som ska
  raderas. Kvitteringen tas bort när båda delarna är klara.
- **Samtalsnummer återanvänds aldrig** (`next-id`), så att `#12` alltid betyder samma samtal.
- Det som skickats till Claude omfattas av Anthropics egen datalagring enligt avtalet. Det här gäller
  bara det som sparas hos oss.

### Runnern (claudeborgen/runner)

En loop i devcontainern (`node claudeborgen/runner/runner.mjs`). Den är medvetet dum och gör det som
inte får bero på modellen:

1. Hjärtslag var 20:e sekund, även mitt i ett varv, och direkt vid varje fasbyte.
   Runnern frågar efter arbete med **long polling** (`GET work?wait=25`). Appen håller kvar frågan
   tills något kommer in, så ett nytt meddelande tas upp direkt. När inget händer blir det ungefär
   5 förfrågningar i minuten för båda looparna tillsammans (mätt), plus hjärtslagen.
   `pollSeconds` och `ideaPollSeconds` används bara mot en app utan long polling och under paus.
2. Committar väntande changelog-poster (se [Changelog](#changelog)), raderar sessionerna för
   borttagna samtal (se [Samtal tas bort](#samtal-tas-bort)) och kontrollerar vilka ändringar som är
   ute på snabbtestservern (se [Driftsatt](#driftsatt)).
3. Frågar efter arbete (`GET work`) och tar ett samtal i taget (`claim`).
4. **Skydd mot loopar** innan Claude startas. De räknar varv, inte pengar, och gäller vilket konto
   Claude än kör på. Om något av dem slår till rapporteras varvet som kraschat:
   - inget nytt meddelande sedan Claudes senaste svar, utom i rättningsvarv
   - fler än `maxRoundsPerConversationPerHour` varv i samtalet den senaste timmen (standard 8)
   - fler än `maxRoundsPerHour` varv totalt den senaste timmen, över alla samtal (standard 20)
   - *valfritt*, bara meningsfullt med API-nyckel: `maxBudgetUsdPerDay` per UTC-dygn och
     `maxBudgetUsdPerRound` per varv. Båda är avstängda som standard.
5. `git pull --rebase` på grenen. En arbetskatalog som inte är ren rörs inte, och varvet kraschar
   i stället.
6. `claude -p --output-format json --dangerously-skip-permissions --max-budget-usd <per varv>`,
   med `--resume` på samtalets session. Claude får en systemprompt om att inte committa själv och om
   att svaret läses av objektspecialisten.
7. Om något ändrats körs `buildCommand`. Om bygget är rött får Claude rätta det i samma session,
   högst `maxBuildFixAttempts` gånger. Förblir det rött pushas ingenting, ändringarna läggs i
   `git stash` och varvet rapporteras som kraschat.
8. Commit `#<id>: <rubrik>`, push till grenen (vid krock: `pull --rebase` och ett nytt försök), och
   sedan `reply` med commit och länk till snabbtestservern.
9. `testCommand` körs, resultatet skickas med `tests`, och om det är grönt flyttas `test-green`.

Tidsgränser finns per Claude-anrop (`roundTimeoutMinutes`) och per kommando
(`commandTimeoutMinutes`). Alla inställningar står i `claudeborgen.json`.

**Frågor.** Claude kan inte få svar mitt i ett varv, så frågor går via samtalet:
- *Små oklarheter:* Claude gör ett rimligt val, bygger och berättar i svaret vilket val det blev.
- *Stora oklarheter*, där en felgissning skulle kasta bort mycket arbete: Claude bygger ingenting och
  inleder svaret med `FRÅGA:`. Runnern tar bort markeringen och skickar svaret som en fråga
  (`question: true`). Samtalet får tillståndet **"Claude har en fråga"**, som syns i listan på
  startsidan, i samtalet och i varvstabellen ("Frågade"). Svaret skrivs som ett vanligt meddelande,
  och nästa varv återupptar samma session.
- Ett meddelande som bara är en fråga *till* Claude besvaras utan ändringar, och det räknas inte som
  en fråga tillbaka.

**När kontots kvot tar slut** blir det paus, inte krasch. Runnern känner igen felet från
`claude -p` (HTTP 429 eller text om användningsgräns). Den sparar det Claude hunnit med i
`git stash`, lämnar tillbaka samtalet till kön (`release`) och skickar hjärtslag med
`paused` tills tiden Claude angav, eller `pauseRetryMinutes` (standard 30). Startsidan visar
"Claude är pausad, försöker igen kl. 14:00". Inget markeras "behöver IT".

**Modell:** `model` och `fallbackModel` i `claudeborgen.json` skickas som `--model` och
`--fallback-model`. Chillin använder `opus`, med `sonnet` som reserv när opus är överbelastad. Alias
som `opus` och `sonnet` pekar alltid på den senaste versionen. Ett fullständigt modellnamn låser
versionen, och `opus[1m]` ger 1M-kontext. Utan inställningen används containerns standard
(`~/.claude/settings.json`), och den följer i tysthet det senaste `/model`-valet i en interaktiv session.

**Konto:** runnern kör `claude` med den inloggning som finns i devcontainern. Det är troligen
ObjektSpecialistIT:s eget konto, eller en API-nyckel via `ANTHROPIC_API_KEY`. Med ett personligt konto
delar runnern kvot med personens eget arbete. Kontrollera att kontots villkor tillåter obevakade
körningar som andra personer startar.

**Bygget** körs utan delad kompileringsserver (`/p:UseSharedCompilation=false`). En kompileringsserver
som hamnat i ett trasigt tillstånd gav 2026-10-21 röda byggen av korrekt kod, och då hade Claude
försökt "rätta" något som inte var fel.

**Claude får inte ändra sin egen runner eller vad som räknas som grönt.** Därför monteras
`claudeborgen/runner/` och `claudeborgen.json` skrivskyddade i devcontainern, se
[Komma igång](#komma-igång). En följd är att runnerns `git pull` misslyckas om någon ändrar de
filerna på grenen. Sådana ändringar görs från värden, och där körs också `git pull`.

## Säkerhet

- **Git-behörighet:** deploy-nyckeln får bara skriva till projektrepot. Grenskydd ska hindra pushar
  till annat än `test` och taggen `test-green`.
- **Snabbtestservern saknar riktiga hemligheter**, och den riktiga kanalen (läge `Agent`) kan inte
  vara öppen där. `IsolationGuard` stoppar uppstarten i så fall. I läge `FakeRunner` tar
  agent-API:t bara emot en runner som deklarerar fejkläge, och runnern vägrar själv att köra fejk mot
  `Agent` eller på riktigt mot `FakeRunner`.
- **Bara fejkad data i snabbslingan**, så att inga riktiga personuppgifter skickas till Claudes API.
  Det objektspecialisten skriver i samtalen skickas däremot till Claude, så det får inte innehålla
  personuppgifter. Det står också vid skrivfälten.
- **Agent-API:t på integrerad test och i produktion** är en ny ingång till en app med riktig data.
  Tokenen ger bara åtkomst till samtalen, och appen sparar bara tokenens hash. IT godkänner att den
  öppnas.
- **Sandlådan kan inte ändra sig själv:** `.devcontainer`, runnern och `claudeborgen.json` monteras
  skrivskyddade.
- **Loopar och kostnad:** runnern begränsar antalet varv, inte pengar (se Runnern). Dollartak finns som
  tillval för den som kör med API-nyckel. Vid ett e2e-test 2026-10-20 körde ett tillståndsfel 62 varv
  i rad innan det stoppades (Claude Code rapporterade cirka 27 USD i listpris, förbrukat från ett
  abonnemang). Skydden infördes då.

## Fejkad Claude

Syfte: gränssnittet och flödet ska gå att prova på snabbtestservern utan riktiga Claude, utan
kostnad och utan att något pushas. Snabbtestservern kör i isolerat läge och väljer fejk med
`Claudeborgen:IsolatedMode`:

| `IsolatedMode` | Vad som svarar | Testar |
|---|---|---|
| `InAppFake` (standard) | `FakeClaude`, en bakgrundstjänst i appen | Gränssnittet och tjänstelagret. Fungerar utan att något annat körs. |
| `FakeRunner` | Runnern med `--fake`, i devcontainern | Hela kedjan: runner, agent-API, token, hjärtslag över nätet. |

Båda styrs av markeringar i meddelandet:
- `[röd]`: röda tester, ett rättningsvarv och sedan grönt.
- `[röd!]`: testerna förblir röda, och samtalet markeras **behöver IT**.
- `[krasch]`: varvet kraschar.
- `[offline]`: hjärtslagen stannar en stund, så att statusen går till offline.
- `[fråga]`: Claude ställer en motfråga i stället för att bygga, och samtalet visas som "Claude har en
  fråga".
- `[kvot]`: kvoten "tar slut" en gång. Samtalet går tillbaka till kön, Claude visas som pausad en
  stund, och sedan körs varvet.
- Utan markering blir varvet grönt och lyckat.

Svaren, commit-id:na och sidan är tydligt märkta som fejkade.

## Komma igång

**`~/.claudeborgen/<projekt>/` på värden** håller det som hör till claudeborgen men inte får ligga i
repot: agent-tokenen (`token`) och Azure-adressen med uppgifter (`azure-deploy-url`). Det finns en
underkatalog per projekt, och bara projektets egen monteras skrivskyddad i dess devcontainer, på
`/home/node/.claudeborgen`. Varje container ser alltså bara sina egna hemligheter, och sökvägarna i
`claudeborgen.json` (`~/.claudeborgen/token` och så vidare) är desamma för alla projekt.

### Prova på snabbtestservern med fejken i appen
1. Driftsätt grenen till snabbtestservern (isolerat läge). Ingen extra konfiguration behövs, eftersom
   `InAppFake` är standard.
2. Ge konton rollerna `ObjektSpecialist` och/eller `ObjektSpecialistIT` i medlemsadministrationen
   (kommaseparerade, som övriga roller). Logga ut och in igen.
3. Startsidan visar avsnittet "Claude" ovanför "Nytt i Chillin".

### Prova hela kedjan på snabbtestservern med fejkrunnern
1. Skapa en token på värden och räkna ut hashen:
   ```sh
   mkdir -p ~/.claudeborgen/chillin && chmod 700 ~/.claudeborgen ~/.claudeborgen/chillin
   openssl rand -hex 32 > ~/.claudeborgen/chillin/token && chmod 600 ~/.claudeborgen/chillin/token
   printf %s "$(cat ~/.claudeborgen/chillin/token)" | sha256sum | cut -d' ' -f1
   ```
2. App Settings på snabbtestservern: `Claudeborgen__IsolatedMode=FakeRunner` och
   `Claudeborgen__AgentTokenSha256=<hashen>`.
3. I devcontainern, med snabbtestserverns adress tillåten i brandväggen:
   ```sh
   CLAUDEBORGEN_API_URL=https://<snabbtestservern>/ node claudeborgen/runner/runner.mjs --fake
   ```

### Driftsättning av snabbtestservern via Local Git
1. Deployment Center för snabbtestservern: koppla bort GitHub och välj **Local Git** (App Service
   Build Service). Under *Local Git/FTPS credentials* finns användarnamn och lösenord. **Ta dem
   under *Application scope*** (användarnamnet ser ut som `$<app>`). De gäller bara den här appen
   och kan återställas för just den. Använd *aldrig* *User scope*: de uppgifterna hör till ditt
   Azure-konto och gäller alla appar du har åtkomst till. Uppgifterna ger också tillgång till appens
   Kudu-sida (konsol, filer, inställningar). Det är acceptabelt för snabbtestservern, som saknar
   riktiga hemligheter, men lägg aldrig in sådana uppgifter för integrerad test eller produktion.
   SCM Basic Auth behöver vara påslaget, under *Configuration → General settings*, men bara för
   den här appen.
2. På värden: skriv appens Local Git-adress *med* uppgifterna i en fil som bara du kan läsa:
   ```sh
   echo 'https://<användare>:<lösenord>@<app>.scm.azurewebsites.net/<app>.git' > ~/.claudeborgen/chillin/azure-deploy-url
   chmod 600 ~/.claudeborgen/chillin/azure-deploy-url
   Ta adressen exakt som Deployment Center visar den. Nyare appar har ett värdnamn med region, och
   då ligger `scm` *före* regionen: `<app>-<hash>.scm.<region>.azurewebsites.net` (till exempel
   `…hsckeffcaeafacfv.scm.northeurope-01.azurewebsites.net`), inte `<app>.scm.azurewebsites.net`.
   Fel värdnamn ger "Could not resolve host".
   ```
   Om användarnamnet börjar med `$`, som Azures automatiska uppgifter gör, ska det stå som det är.
   Enkelfnuttarna hindrar skalet från att tolka det.
3. Montera katalogen i devcontainern (skrivskyddad) och tillåt Azure i brandväggen. Ändringarna görs
   på värden, eftersom `.devcontainer` är skrivskyddad inifrån:
   - `devcontainer.json` under `mounts`:
     `"source=${localEnv:HOME}/.claudeborgen/chillin,target=/home/node/.claudeborgen,type=bind,readonly"`
   - `init-firewall.sh`, i listan med domäner: `"<app>.scm.azurewebsites.net"` (driftsättning) och
     `"<app>.azurewebsites.net"` (`/claudeborgen/version`)

   Bygg sedan om containern med `devcontainer up --workspace-folder . --remove-existing-container`.
4. Prova: `node claudeborgen/runner/deploy.mjs` i devcontainern.
5. Kontrollera efter första driftsättningen att `https://<app>.azurewebsites.net/claudeborgen/version`
   visar rätt commit.

### Riktig drift mot integrerad test
1. App Settings på integrerad test: `Claudeborgen__Enabled=true` och
   `Claudeborgen__AgentTokenSha256=<hashen>`. Använd en annan token än snabbtestserverns.
2. Fyll i `apiUrl` och `testServerUrl` i `claudeborgen.json`.
3. Ändringar i `.devcontainer/devcontainer.json` på värden:
   ```jsonc
   "mounts": [
     // … befintliga …
     "source=${localWorkspaceFolder}/claudeborgen/runner,target=/workspace/claudeborgen/runner,type=bind,readonly",
     "source=${localWorkspaceFolder}/claudeborgen.json,target=/workspace/claudeborgen.json,type=bind,readonly",
     "source=${localEnv:HOME}/.claudeborgen/chillin,target=/home/node/.claudeborgen,type=bind,readonly"
   ]
   ```
   Lägg dessutom till adresserna till integrerad test och snabbtestservern i `init-firewall.sh`.
   Bygg sedan om containern med `devcontainer up --workspace-folder . --remove-existing-container`.
4. Grenen `test` måste finnas på `origin`, och arbetskatalogen ska ha den utcheckad och ren.
5. Starta runnern i `screen`:
   ```sh
   devcontainer exec --workspace-folder . node claudeborgen/runner/runner.mjs
   ```
   Den loggar till terminalen. Startsidan visar "Claude ledig" inom 20 sekunder.

## Gränsen mot värdappen

- `claudeborgen/` har inga beroenden till Chillin. `Claudeborgen.Web` är ett eget projekt utan
  referens till Chillin, så kompilatorn håller gränsen.
- Det enda värden måste tillhandahålla är `IClaudeborgenHost` (vem får vad, och sidskalet runt ett
  samtal), `ClaudeborgenSettings` (från konfigurationen och värdens isolerat-läge), och rendering av
  startsidesavsnittet där changelogen ligger.
- Inloggning krävs alltid. Det är inte konfigurerbart.
- Utbrytning: flytta `claudeborgen/` till ett eget repo, publicera `Claudeborgen.Web` som NuGet-paket,
  och byt Chillins projektreferens mot en paketreferens. Runnern kan köras direkt från en utcheckning
  eller publiceras som npm-paket.

## Ordning

1. ~~Integrationen i Chillin~~ *(2026-10-19)*
2. ~~Utbrytning till `claudeborgen/` med `Claudeborgen.Web`, protokoll och runner~~ *(2026-10-20)*
3. **Snabbtestservern**: grenen driftsatt, prova med fejken i appen och sedan med fejkrunnern (Lars).
4. **Chillin på integrerad test** med `Claudeborgen__Enabled=true` (Lars).
5. **Devcontainern**: skrivskyddade monteringar, token och brandvägg (Lars, på värden).
6. **CI för `test`** till snabbtestservern (Chillins fas 10).
7. **Övergång**: Chillin utvecklas via samtalen, med `devcontainer exec claude` som reserv.

## Öppna frågor

- Vilken CI driftsätter `test` (GitHub Actions?), och hur snabbt kan det gå?
- Vilket konto runnern kör på: ObjektSpecialistIT:s eget (om villkoren tillåter) eller en API-nyckel.
- Ska man kunna pausa runnern från appen (t.ex. under en demo)?
- `.devcontainer` är gitignorerad i Chillin. Ska den versionshanteras nu när allt ska bo i Chillin?

## Historik

**2026-10-21: changelogen blev en fil i repot.** Tidigare låg Claudes changelog-poster i
datakatalogen på servern där samtalet fördes. De följde därför inte med koden till andra miljöer,
och de hamnade alltid ovanför handskrivna poster oavsett datum.

**2026-10-20: utbrutet och runnern byggd.** Allt flyttades till `claudeborgen/` med
`Claudeborgen.Web` som eget projekt, agent-API:t blev ett kontrakt (`PROTOCOL.md`, camelCase),
runnern skrevs från grunden i Node, och fejkrunner-läget lades till. Skydd mot loopar och kostnad
infördes efter ett e2e-test där ett tillståndsfel gav 62 varv i rad.

**2026-10-19: anpassning till Chillin.** Integrationen placeras på Chillins integrerade testserver,
eftersom produktion fortfarande är Umbraco-versionen och ingen annan instans kan bära den. Lagringen blev
filbaserad, eftersom Chillin saknar databas sedan fas 7. Rollerna blev `ObjektSpecialist` och
`ObjektSpecialistIT`, och allt ligger bakom inloggning. Teams-notiserna togs bort; det som behöver
uppmärksamhet visas i integrationen. Changelogen kopplas till "Nytt i Chillin" på startsidan.
Grenstrategin fastställdes (allt mergas in i `test`). En fejkad Claude lades till, så att
gränssnittet och flödet kan provas på snabbtestservern.

**2026-10-14–18: egen plattform.** Först byggdes en fristående webbapp per projekt (repot
`ChalmersLibrary/claudeborgen-fast-iter`, grenen `test`): egen inloggning med passkeys och
inbjudningar, intressenter som skrev förslag och röstade, en kö där ansvariga släppte uppgifter,
och en runner som körde dem en i taget med tester före push. Riktningen ändrades eftersom målet är
objektägarens självbestämmande och snabb iteration. En egen plattform blev ett forum och ett
ärendesystem som ingen bad om, och diskussionen hör hemma i de kanaler man redan har.

Kod i det repot som går att återanvända:

| Del | Fil | Status |
|---|---|---|
| Devcontainer (bas + projekttillägg, ssh-agent per projekt) | `devcontainer/`, `projects/` | Behålls nästan som den är |
| Runner (hjärtslag, `claude -p`, sessioner, budget, push, git-hantering) | `runner/runner.mjs` + tester | Anpassas: samtal, bygge före / tester efter, `test-green`, notiser |
| Claudes synfält, claim, status från hjärtslag, session per tråd | `platform/.../Domain/AgentService.cs` | Flyttas in i integrationen |
| Agent-autentisering med token-hash | `platform/.../Auth/AgentAuthenticationHandler.cs` | Flyttas in |
| Statusraden och liveuppdatering | `Pages/Shared/Components/StatusBar/`, `Live/`, `wwwroot/js/live.js` | Flyttas in |
| Leveranser, kostnad per varv, månadsöversikt | `Data/Entities.cs` (`Delivery`), `Pages/Costs.*` | Flyttas in, i förenklad form |
| Inloggning, passkeys, inbjudningar, roller, förslag, röster | `Auth/`, `Pages/Login`, `Pages/Invite`, `Pages/Admin` | Utgår, ersätts av appens inloggning |
