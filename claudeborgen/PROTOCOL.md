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
{ "activity": "idle" | "working", "conversationId": 12, "phase": "implementerar" }
```
Skickas regelbundet (runnern: var 20:e sekund) och vid varje fasbyte. Appen visar Claude som offline
när inget hjärtslag kommit på `OfflineAfter` (standard 60 s). `conversationId` och `phase` gäller bara
`working`. Faserna är fritext; runnern använder: `läser in`, `implementerar`, `bygger`, `rättar bygget`,
`pushar`, `kör tester`.

### `GET work` → `200`
Samtal att arbeta med, i den ordning de ska tas. Ett samtal med ett pågående varv (`claudeWorking`)
kommer först, så att en omstartad runner tar upp det igen. Därefter kommer samtal som väntar på Claude,
äldst först.
```json
[ { "id": 12, "title": "Exportknapp", "state": "waitingForClaude", "sessionId": "…" } ]
```

### `GET conversations/{id}` → `200`
Hela samtalet:
```json
{
  "id": 12, "title": "Exportknapp", "createdBy": "agneta", "createdAt": "…",
  "state": "waitingForClaude" | "claudeWorking" | "waitingForOwner" | "done" | "cancelled",
  "sessionId": "…",
  "messages": [ { "authorKind": "human" | "claude" | "system", "author": "agneta", "body": "…", "at": "…" } ],
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
  "sessionId": "…", "costUsd": 0.42, "durationSeconds": 120 }
```
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
