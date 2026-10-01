# Scenarier för end-to-end-testerna

**Det här är filen du skriver i.** Varje rad är något som ska vara sant i appen. Du behöver inte
skriva steg eller selektorer — bara vad du förväntar dig att se. Claude gör om raderna till
körbara Playwright-test i `e2e/tests/`, och därefter körs de utan Claude (`npm test` i `e2e/`).

## Så skriver du ett scenario

```
- [ ] ORD-012  Som Desk: när jag byter ordertyp syns den nya typen i listan direkt.
```

- **ID:** prefix efter område (`SMOKE`, `ORD`, `LOCK`, `ROLE`, `SET` …) + löpnummer. Återanvänd aldrig
  ett ID; stryk en rad (`~~…~~`) om den inte längre gäller.
- **Roll:** börja med vem som gör det (`Som Desk`, `Som Viewer`, `Som två användare`). Finns: superadmin,
  admin, desk, viewer, roleless (se roll-tabellen längst ned).
- **Förväntat resultat** i en mening. Skriv gärna *varför* om felet är ett tidigare fel du hittat
  (”regression för 0762fd9”) — då vet vi att raden inte får tas bort.
- `[ ]` = ännu inte automatiserad, `[x]` = automatiserad (filen och testets ID står i kolumnen nedan).
- Du behöver inte veta om det går att testa. Skriv det du vill veta; Claude säger till om något inte går.

## Automatiserade

| ID | Förväntat | Fil |
|----|-----------|-----|
| SMOKE-001 | Inloggad superadmin hamnar på orderlistan och ser ordrar | `tests/smoke.spec.ts` |
| SMOKE-002 | Orderlistan har rubrik och de förväntade demo-ordrarna | `tests/smoke.spec.ts` |
| ORD-001 | Byte av ordertyp (Bok → Artikel) syns i orderlistan efter omladdning (regression 0762fd9) | `tests/orders.spec.ts` |
| ORD-002 | Ändrad Referens sparas och visas i listan (regression 556b30c) | `tests/orders.spec.ts` |
| LOCK-001 | En order som en användare öppnat visas som låst för en annan, med ”Överta låset” och utan åtgärdsknappar (regression c8d5ad8, e412340) | `tests/locks.spec.ts` |

## Att automatisera (börja skriva här)

- [ ] LOCK-002  Som en användare: när jag laddar om sidan efter att ha öppnat en order är ordern inte
  längre låst för andra (regression 556b30c).
- [ ] LOCK-003  Som två användare: ”Överta låset” flyttar låset, och den förra redaktören får veta det.
- [ ] ROLE-001  Som Viewer: orderlistan går att läsa men ingen knapp som ändrar något syns eller fungerar.
- [ ] ROLE-002  Som roleless: alla sidor stoppar (ingen orderlista, inga data).
- [ ] ROLE-003  Som Desk: Inställningar-sidan är dold/stoppad. Som Administrator: den syns.
- [ ] ROLE-004  Som Desk: en öppnad order visar inga åtgärdsknappar alls (Typ, Status, Referens,
  ”Överta låset” …). Som Administrator: de syns. Döljningen är ren CSS och stoppar inte servern.
- [ ] ORD-003  Som Desk: statusbyte (t.ex. Ny → Åtgärda) syns direkt i listan.
  *(Går inte som skrivet — se ROLE-004: Desk ser ingen Status-knapp. Byt till Administrator,
  eller behåll Desk om det är just den spärren du vill testa.)*

*(Lägg till egna rader här. Skriv hellre för många än för få — det går fort att stryka.)*

## Det vakten fångar automatiskt i varje test

Du behöver inte skriva scenarier för dessa; **alla** test misslyckas om något av följande händer:

- ett ofångat JavaScript-fel i webbläsaren
- `console.error`
- ett anrop till appen som svarar 4xx/5xx (fel i en AJAX-rutt, saknad script/CSS-fil)
- ett anrop som avbryts/misslyckas
- en oväntad `alert`/`confirm`-ruta

Ett test som *avsiktligt* provocerar fram något sådant säger det uttryckligen med
`guard.allow(/mönster/)`. SignalR-reservvägen (WebSocket → long polling) är undantagen eftersom den är
ofarlig.

## Roll × område (utkast, granska!)

Detta är min läsning av `ViewerReadOnlyFilter` m.fl. — rätta det som är fel, så blir det ett
automatiskt test (ROLE-xxx).

Notera raden för **desk**: `ChalmersILL.cshtml` lägger in `<style>.editmode .btn { display:none }</style>`
för alla konton utan rollen `Administrator`, så ett rent Desk-konto ser *inga* knappar i en öppnad
order. Det är gammalt beteende (fanns långt före migreringen) och är enbart kosmetiskt — servern
stoppar inte anropen. Är det så det ska vara, eller ska Desk kunna arbeta i ordrar?

| Roll | Roller i `members.json` | Orderlista | Ändra order | Inställningar | Kontoadmin |
|------|-------------------------|------------|-------------|---------------|------------|
| superadmin | Desk, Administrator, SuperAdmin | ja | ja | ja | ja |
| admin | Desk, Administrator | ja | ja | ja | nej |
| desk | Desk | ja | knapparna dolda (kosmetiskt), servern tillåter | dolt (kosmetiskt) | nej |
| viewer | Viewer | ja (läsa) | nej (stoppas, utom `[AllowViewer]`) | nej | nej |
| roleless | *(inga)* | nej (stoppas helt) | nej | nej | nej |
