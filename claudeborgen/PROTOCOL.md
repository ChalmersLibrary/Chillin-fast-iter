# Claudeborgen agent-API, protokoll version 1

Kontraktet mellan **runnern** (`claudeborgen/runner/`, Node, körs i devcontainern) och **appdelen**
(för .NET: `claudeborgen/dotnet/Claudeborgen.Web`). En appdel för en annan plattform ska följa det
här dokumentet. Runnern förutsätter inget annat.

All trafik går från runnern till appen. Appen anropar aldrig runnern.

## Allmänt

- Bas-URL: appens adress, till exempel `https://chillin-test.example/`. Alla sökvägar nedan ligger
  under `/api/claude/`.
- **Autentisering:** `Authorization: Bearer <token>` på varje anrop. Appen sparar bara SHA-256-hashen
  av tokenen, som hex, och jämför i konstant tid.
- **Fejkrunner:** en runner i fejkläge skickar `X-Claudeborgen-Fake-Runner: true` på varje anrop.
- **JSON:** camelCase-fältnamn och enum-värden som camelCase-strängar (`"waitingForClaude"`). Det gäller
  oavsett hur värdappen i övrigt serialiserar JSON. Okända fält ska ignoreras av båda sidor.
- **Fel:** felkoder med kroppen `{ "message": "<svensk text som kan visas>" }`.

| Status | Betydelse |
|---|---|
| `200` | OK, med JSON-kropp |
| `204` | OK, utan kropp |
| `401` | Fel eller saknad token |
| `403` | Servern tar inte emot den här runnern (fejkrunner-läge utan fejkhuvudet) |
| `404` | Agent-API:t är stängt, eller samtalet finns inte |
| `409` | Anropet stämmer inte med samtalets tillstånd (se `message`) |

## Lägen

Appen är i ett av fyra lägen, som `GET info` returnerar:

| Läge | Agent-API:t |
|---|---|
| `Off` | Stängt (404) |
| `Agent` | Öppet för en riktig runner. Den riktiga kanalen, aldrig på en isolerad testserver. |
| `InAppFake` | Stängt (404). Appen kör en fejkad Claude i processen. |
| `FakeRunner` | Öppet, men bara för en runner som skickar fejkhuvudet (annars 403). |

En runner i normalläge ska vägra köra mot `FakeRunner`, och en runner i fejkläge ska vägra köra mot
`Agent`. Fejksvar hör inte hemma i riktiga samtal.

## Anrop

### `GET info`
```json
{ "protocolVersion": 1, "mode": "Agent", "maxFixAttempts": 2 }
```

### `POST heartbeat` → `204`
```json
{ "activity": "idle" | "working" | "paused", "conversationId": 12, "phase": "implementerar",
  "pausedUntil": "2026-10-21T14:00:00Z" }
```
Skickas regelbundet (runnern: var 20:e sekund) och vid varje fasbyte. Appen visar Claude som offline
när inget hjärtslag kommit på `OfflineAfter` (standard 60 s). `conversationId` och `phase` gäller bara
`working`. Faserna är fritext; runnern använder: `läser in`, `implementerar`, `bygger`, `rättar bygget`,
`pushar`, `kör tester`, `uppdaterar changelogen`.

`paused` betyder att runnern lever men inte kan använda Claude just nu, till exempel för att kontots
kvot är slut. `phase` är då orsaken och `pausedUntil` är när runnern försöker igen. Hjärtslagen
fortsätter under pausen, och utan dem visas Claude som offline som vanligt.

### `GET work` → `200`
`?kind=idea` eller `?kind=task` begränsar till idéer respektive arbeten, eftersom runnern har en loop
för varje. Utan parameter returneras båda. Samtal att arbeta med, i den ordning de ska tas. Ett samtal med ett pågående varv (`claudeWorking`)
kommer först, så att en omstartad runner tar upp det igen. Därefter kommer samtal som väntar på Claude,
äldst först.
```json
[ { "id": 12, "kind": "task" | "idea", "title": "Exportknapp", "state": "waitingForClaude", "sessionId": "…" } ]
```
En **idé** besvaras som i en chatt: runnern kör Claude utan rätt att ändra filer eller köra kommandon,
och svarar med `reply` utan commits. En idé blir ett arbete i appen ("Gör det här till ett arbete"),
och samma samtal, med samma `sessionId`, dyker sedan upp som `kind: "task"`. Högst ett idésvar och ett
arbetsvarv kan pågå samtidigt (`claim` ger `409` per sort).
```json
```

### `GET conversations/{id}` → `200`
Hela samtalet:
```json
{
  "id": 12, "title": "Exportknapp", "createdBy": "agneta", "createdAt": "…",
  "state": "waitingForClaude" | "claudeWorking" | "waitingForOwner" | "questionForOwner" | "done" | "cancelled",
  "sessionId": "…",
  "messages": [ { "authorKind": "human" | "claude" | "system", "author": "agneta", "body": "…", "at": "…",
                  "isQuestion": false } ],
  "rounds": [ { "number": 1, "startedAt": "…", "finishedAt": "…", "outcome": "delivered" | "crashed" | null,
                "commits": ["abc1234"], "links": ["https://…"], "sessionId": "…",
                "tests": "pending" | "green" | "red", "testSummary": "…",
                "costUsd": 0.42, "durationSeconds": 120, "isFixRound": false } ],
  "consecutiveRedRounds": 0, "needsIt": false, "needsItReason": null, "…": "…"
}
```

### `POST conversations/{id}/claim` → `200`
Tar samtalet och startar ett nytt varv. Svaret är varvet (se `rounds` ovan). `isFixRound: true` betyder
att varvet startades av röda tester och inte av ett nytt meddelande. Att ta ett samtal som redan har ett
pågående varv returnerar det varvet, vilket är det en omstartad runner behöver. `409` om samtalet inte
väntar på Claude, eller om Claude redan arbetar i ett annat samtal (ett i taget).

### `POST conversations/{id}/reply` → `204`
Avslutar varvet som levererat.
```json
{ "body": "Svaret till objektspecialisten", "commits": ["<sha>"], "links": ["https://…"],
  "sessionId": "…", "costUsd": 0.42, "durationSeconds": 120, "question": false }
```
`question: true` betyder att Claude behöver svar innan arbetet fortsätter. Samtalet går då till
`questionForOwner` i stället för `waitingForOwner`, och nästa meddelande startar ett nytt varv.
`commits` får vara tom om varvet inte ändrade något, till exempel när Claude bara svarade på en fråga.
Länkar som inte är `http(s)://` eller börjar med `/` tas bort. Om någon har skrivit i samtalet under
varvet går samtalet direkt tillbaka till "väntar på Claude".

### `POST conversations/{id}/tests` → `204`
Testresultatet för en levererad commit, som skickas efter `reply`.
```json
{ "commit": "<sha>", "green": false, "summary": "1 test rött: …" }
```
Röda tester skickar samtalet tillbaka till Claude för ett rättningsvarv, högst `maxFixAttempts` gånger i
rad. Därefter markeras samtalet "behöver IT".

### `POST conversations/{id}/release` → `204`
Runnern tog samtalet men kunde inte köra Claude, till exempel för att kvoten tog slut. Samtalet läggs
tillbaka i kön utan spår: varvet som aldrig kördes tas bort, och samtalet markeras inte "behöver IT".
`409` om Claude inte arbetar i samtalet.

### `POST conversations/{id}/crash` → `204`
Varvet kunde inte slutföras, till exempel för att Claude kraschade, bygget förblev rött eller push
misslyckades. Inget har levererats. Samtalet markeras "behöver IT".
```json
{ "summary": "Bygget är rött efter 2 rättningsförsök. Inget har pushats." }
```

### `GET changelog/pending` → `200`
Samtal som markerats klara men vars changelog-post ännu inte är committad. Runnern lägger in posten
överst i projektets changelog-fil, committar och pushar den, och rapporterar sedan med anropet nedan.
```json
[ { "conversationId": 12, "date": "2026-10-21", "text": "Exportknapp på beställningslistan" } ]
```

### `POST conversations/{id}/changelog` → `204`
Changelog-posten är committad.
```json
{ "commit": "<sha>" }
```
`409` om samtalet inte är klart.

### `GET deployments/pending` → `200`
Levererade varv med commit vars ändring ännu inte setts på snabbtestservern (senaste dygnet).
```json
[ { "conversationId": 12, "round": 2, "commit": "<sha>" } ]
```

### `POST conversations/{id}/rounds/{round}/deployed` → `204`
Varvets commit finns i det snabbtestservern kör. Samtalet får meddelandet "Ändringen är ute på
snabbtestservern och går att prova", men bara för den senaste ändringen.

### `GET /claudeborgen/version` på snabbtestservern → `200`
Ligger *utanför* `/api/claude/` och kräver varken token eller inloggning, så att runnern kan fråga
vilken commit servern är byggd från. Det fungerar oavsett läge, utom `Off`.
```json
{ "commit": "<sha>" }
```
`commit` är `null` om bygget inte visste sin commit. Då kan inga ändringar bekräftas som ute.

### `GET cleanup` → `200`
Samtal som tagits bort (klara med committad changelog-post, eller avbrutna) vars Claude Code-sessioner
runnern ska radera på sin sida.
```json
[ { "conversationId": 12, "sessionIds": ["4fffa765-…"] } ]
```

### `POST conversations/{id}/cleaned` → `204`
Sessionerna är raderade.

Samtalsnummer återanvänds aldrig, inte heller efter att ett samtal tagits bort. När
`POST …/changelog` har tagits emot finns samtalet inte längre (`GET conversations/{id}` → `404`).

## Changelog-filen

En JSON-lista i projektrepot. Sökvägen anges i runnerns `claudeborgen.json` som `changelogFile`, och
appen läser samma fil där den är driftsatt. Nyaste posten ligger först, men visningen sorterar ändå
efter datum.
```json
[
  { "date": "2026-10-21", "items": ["Exportknapp på beställningslistan"], "conversationId": 12 },
  { "date": "2025-09-09", "version": "v2.6.3", "items": ["Handskriven post"] }
]
```
`conversationId` finns bara på poster som runnern har skrivit. Appen använder den för att se vilka
klara samtal som redan finns i den driftsatta filen.

## Ett varv, sett från runnern

```
heartbeat(working, läser in) → claim → GET conversation
→ claude -p (implementerar) → bygg (rätta vid behov) → commit + push
→ reply → heartbeat(working, kör tester) → tests → heartbeat(idle)
```
