# Claudeborgen: design

Claudeborgen ger objektägaren makt att driva utvecklingen av sin tjänst tillsammans med Claude,
med snabb iteration och synliga resultat. IT-specialisten finns nära till hands, kan flika in när
problem uppstår och beslutar om vidare integrationstest och driftsättning.

Det byggs **först in i Chillin** (Chalmers.ILL). När det fungerar bra bryts det ut till en egen
komponent som går att knyta in i andra projekt på samma sätt.

Status: riktningen ändrades 2026-10-19 (se [Historik](#historik)). Den nya varianten är inte byggd än.

## Fastställda beslut

Avstämda med Lars. Ompröva inte utan ny avstämning.

| Område | Beslut | Datum |
|---|---|---|
| Arbetssätt | Objektägaren driver en uppgift som ett **samtal med Claude**. Varje meddelande ger ett varv: implementation → push → automatisk driftsättning till testservern → Claude svarar med länk. Hela samtalet är en Claude Code-session. | 2026-10-19 |
| Diskussion och planering | Sker **var man vill** (Teams, IRL …). Claude-integrationen är bara implementationsplaneringen, inget forum. | 2026-10-19 |
| Placering | **Inbyggt i projektets app**, i anslutning till dess changelog, synligt på valfri sida. Byggs först i Chillin och bryts sedan ut. | 2026-10-19 |
| Värdinstans | Integrationen körs på **Chillins integrerade testserver**, och senare i produktion. Lars driver ut migreringsgrenen till integrerad test för att det ska gå. | 2026-10-19 |
| Kanalen och testservern | Claude-integrationen körs **aldrig** på den isolerade testservern som snabbgrenen driftsätts till. Där är den avstängd, på samma sätt som `IsolationGuard`. Kanalen där fel rapporteras får inte kunna slås ut av en trasig push. | 2026-10-19 |
| Roller | Rollerna **`ObjektSpecialist`** och **`ObjektSpecialistIT`** i appens rollsystem. Övriga inloggade är läsare; deras önskemål går via objektspecialisten och de kanaler de redan har. | 2026-10-19 |
| Synlighet | **Ingenting är öppet.** Alla som har inloggning i appen kan se och läsa allt i integrationen; ingen annan ser något. | 2026-10-19 |
| Lagring | **Filbaserad**, som resten av Chillin: en JSON-fil per samtal under `DataPath/claude/`. Ingen databas (Chillin har ingen sedan fas 7). | 2026-10-19 |
| Changelog | En uppgift hamnar i changelogen när objektägaren säger **klar**. Chillins changelog är rubriken "Nytt i Chillin" i `Views/ChalmersILLStartPage.cshtml`. Ingen markering för produktion; ändringen syns i produktion när den driftsatts där. | 2026-10-19 |
| Externa integrationer | **Inga.** Inga Teams-webhooks, ingen mail. Det som behöver uppmärksamhet visas i integrationen själv. | 2026-10-19 |
| Tester | **Bygget före push, testerna efter.** Röda tester rättas av Claude i en ny commit inom samma samtal. | 2026-10-19 |
| Grön markering | Runnern flyttar git-taggen `test-green` till den senaste commit som har gröna tester. IT mergar vidare därifrån. | 2026-10-19 |
| Inloggning | Appens egen inloggning och roller återanvänds. Ingen egen inloggning. | 2026-10-19 |
| Claude körs | Claude Code i devcontainer på egen maskin. All trafik går **utåt** från devcontainern; appen kontaktar aldrig Claude. | 2026-10-14 |
| Snabbslinga | Claude pushar direkt till snabbgrenen **`test`** → automatisk driftsättning till isolerad testserver med fejkade integrationer. IT tar det vidare till integrerad test och produktion (Lars mergar `test` vidare till andra repon). | 2026-10-14/16 |
| Taggningar | En Claude Code-session per tråd (samtal), så att Claude minns tidigare svar. | 2026-10-19 |
| Status | Claudes status (ledig / jobbar på X + fas / offline) visas tydligt för alla som ser integrationen. | 2026-10-14 |

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
förblir röda, varv som kraschat och samtal som markerats klara men ännu inte tagits vidare. Det syns
tydligt för `ObjektSpecialistIT`, i anslutning till Claudes status.

**Två testregler.** Varven i samtalen följer "bygget före push, testerna efter" på grenen `test`.
Det manuella migreringsarbetet följer fortfarande `CLAUDE.md`, som säger "commit bara på gröna
tester". `CLAUDE.md` ska uppdateras så att den skiljer på de två innan första varvet körs.

Det finns bara en objektägare per projekt, så runnerns "ett samtal i taget" begränsar inte i praktiken.

## Arkitektur

```
 ┌───────────── Egen maskin ────────────────┐        ┌────────────── Azure ─────────────────────────┐
 │ devcontainer (per projekt)               │        │ Appen på integrerad test (senare produktion) │
 │  ├─ runner ──── HTTPS, utåt ─────────────────────►│  ├─ Claude-integrationen (sidor + agent-API) │
 │  │   └─ claude -p (session per samtal)   │        │  └─ changelog                                │
 │  ├─ projektrepo (/workspace)             │        │                                              │
 │  └─ ssh-agent ── git push ─────► GitHub (test) ──CI──► Isolerad testserver (integrationen AV)    │
 └──────────────────────────────────────────┘        └──────────────────────────────────────────────┘
```

### Claude-integrationen i appen

- Sidor: samtalen (lista + ett samtal), Claudes status och kö, changelog. Kan visas på valfri sida
  i appen.
- Changelog: samtal som markerats klara visas överst under "Nytt i Chillin" på startsidan
  (`Views/ChalmersILLStartPage.cshtml`). De handskrivna versionsposterna ligger kvar under dem som historik.
- Liveuppdatering av status och samtal (SignalR). Chillin kör redan SignalR på en instans, och
  integrationen ärver samma enkelinstansförutsättning.
- Agent-API med projektspecifik token som bara ger åtkomst till samtalen, inget annat i appen.
  Agent-API:t autentiseras med tokenen, inte med inloggningscookien. Det måste därför undantas
  från det globala `[Authorize]` och från `ViewerReadOnlyFilter` på samma sätt som Chillins
  befintliga maskin-till-maskin-endpoints.
- Egen filbaserad lagring: en JSON-fil per samtal under `DataPath/claude/`, med låsning inom
  processen som `FileOrderItemManager`.

### Agent-API (skiss)

| Anrop | Syfte |
|---|---|
| `POST /api/claude/heartbeat` | `{state, conversationId?, phase?}`. Grunden för statusvisningen. |
| `GET  /api/claude/work` | Samtal som väntar på Claude, äldst först. |
| `POST /api/claude/conversations/{id}/claim` | Ta samtalet (→ Claude arbetar). Ett i taget. |
| `GET  /api/claude/conversations/{id}` | Rubrik, meddelanden, tidigare varv (commits, sessions-id, testresultat). |
| `POST /api/claude/conversations/{id}/reply` | `{body, commits[], sessionId, costUsd?, durationSeconds?}`, Claudes svar efter ett varv. |
| `POST /api/claude/conversations/{id}/tests` | `{commit, green, summary}`, testresultatet efter push. |

Faser i statusen: läser in · implementerar · bygger · pushar · kör tester · rättar.

### Runnern

Ligger i claudeborgen-repot och körs i devcontainern, i en loop:

1. Hjärtslag var 20:e sekund, även mitt i ett varv.
2. Tar nästa samtal som väntar på Claude och kör `claude -p`, med `--resume` på samtalets session.
3. `BUILD_COMMAND`. Om det är rött får Claude rätta det innan push.
4. Commit och push till `test`. Svar i samtalet.
5. `TEST_COMMAND`. Gröna tester flyttar `test-green`, röda ger ett rättningsvarv eller en notis till IT.

Bygg- och testkommandon, budgettak och tidstak per varv står i `projects/<namn>/project.env` på
claudeborgen-sidan, så att Claude inte kan ändra vad som räknas som grönt.

### Devcontainern

- **Bas:** Claude Code, brandvägg (`init-firewall.sh`) med en grundlista över tillåtna domäner,
  och en dedikerad ssh-agent per projekt via `initializeCommand`, så att bara projektets
  deploy-nyckel syns i containern.
- **Projekttillägg** (`projects/<namn>/`): extra verktyg i imagen (för Chillin .NET SDK och
  Chromium), extra domäner i brandväggen (för Chillin `api.nuget.org` och appens adress).
- Värdens claudeborgen-utcheckning monteras **skrivskyddad**. Claude kan inte ändra sin sandlåda
  eller runnern.

## Säkerhet

- **Git-behörighet:** deploy-nyckeln får bara skriva till projektrepot. Grenskydd hindrar Claude från
  att pusha till annat än `test` (och taggen `test-green`).
- **Testservern saknar riktiga hemligheter helt**, och Claude-integrationen är avstängd där. Den
  isolerade konfigurationen ska vara säker av sig själv (för Chillin: `IsolationGuard`).
- **Bara fejkad data i snabbslingan**, så att inga riktiga personuppgifter skickas till Claudes API.
- **Agent-API:t i produktionsappen** är en ny ingång till en app med riktig data. Tokenen ger bara
  åtkomst till samtalen; appen sparar bara tokenens hash. IT godkänner att den öppnas.
- **Det objektägaren skriver i samtalen skickas till Claude.** Inga personuppgifter i samtalen.
- **Sandlådan kan inte ändra sig själv.** Devcontainer-konfigurationen och runnern monteras
  skrivskyddade och uppdateras bara manuellt efter granskning.
- **Kostnad:** budgettak per varv (`--max-budget-usd`) och tidstak. Kostnad per varv sparas.

## Gränsen mot värdappen

Byggs från början så att utbrytningen blir mekanisk:

- Egen mapp och eget namnområde, t.ex. `Chalmers.ILL/ClaudeIntegration/`.
- Egen lagring (egen underkatalog i datakatalogen).
- Inget i appen beror på integrationen, och integrationen beror inte på appens övriga kod. Undantaget
  är en enda inkoppling: changelog-komponenten på startsidan.
- Ett litet gränssnitt som värdappen implementerar:
  - vem som är inloggad och vilken roll den har (objektägare / IT / läsare)
  - om integrationen är på (av på testservern)
  - datakatalogen och layouten

Inloggning krävs alltid. Det är inte konfigurerbart.

Utbrytning: integrationen blir ett **Razor Class Library** (NuGet-paket) som andra .NET-projekt lägger
till och kopplar in med gränssnittet ovan.

## Ordning

1. **Devcontainern för Chillin** från claudeborgen (`projects/chillin/`), så att sessionerna körs i den.
2. **Integrationen i Chillin** (grenen `claudeborgen`): samtal, status, agent-API, changelog,
   rollerna `ObjektSpecialist`/`ObjektSpecialistIT`, avstängd på testservern.
3. **Chillin på integrerad test** med integrationen påslagen (Lars).
4. **Runnern** anpassad till samtal: bygge före push, tester efter, `test-green`, markeringen "behöver IT".
5. **CI för `test`** till Chillins isolerade testserver (Chillins fas 10).
6. **Utbrytning** till ett Razor Class Library när arbetssättet har satt sig.

## Öppna frågor

- Hur förhåller sig `test` till `remove-dotnet-framework` och `claudeborgen`? Vad grenas ut från vad,
  och vem mergar åt vilket håll?
- Vilken CI driftsätter snabbgrenen `test` (GitHub Actions?), och hur snabbt kan det gå?
- Var ska claudeborgens egen kod (devcontainer + runner) bo när repot `claudeborgen-fast-iter` stängs?
- Budgettak: rimliga nivåer per varv och per månad.
- Hur många rättningsförsök vid röda tester innan IT får en notis?
- Ska man kunna pausa (t.ex. under en demo)?

## Historik

**2026-10-19: anpassning till Chillin.** Integrationen placeras på Chillins integrerade testserver,
eftersom produktion fortfarande är Umbraco-versionen och ingen annan instans kan bära den. Lagringen blev
filbaserad, eftersom Chillin saknar databas sedan fas 7. Rollerna blev `ObjektSpecialist` och
`ObjektSpecialistIT`, och allt ligger bakom inloggning. Teams-notiserna togs bort; det som behöver
uppmärksamhet visas i integrationen. Changelogen kopplas till "Nytt i Chillin" på startsidan.

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
