# End-to-end-tester (Playwright)

Webbläsartester som kör hela appen på riktigt: Kestrel i isolerat läge (se `Chillin__Isolated`),
riktig Chromium, riktiga inloggningar. Scenarierna du vill ha testade skrivs i
[scenarios/SCENARIOS.md](scenarios/SCENARIOS.md); filerna i `tests/` är den körbara versionen.

Testerna körs **utan Claude** — ren deterministisk Playwright. Claude skriver och underhåller dem.

## Köra

```bash
cd e2e
npm install                         # första gången
npm test                            # bygger appen, startar den, kör alla scenarier
npm test -- tests/orders.spec.ts    # en fil
npm test -- -g ORD-001              # ett scenario (på ID)
npm run test:headed                 # med synlig webbläsare (kräver skärm)
npm run report                      # öppna HTML-rapporten (trace, skärmdump vid fel)
```

Miljövariabler:

| Variabel | Betydelse |
|----------|-----------|
| `E2E_SKIP_BUILD=1` | hoppa över `dotnet build` (om appen redan är byggd) |
| `E2E_KEEP_DATA=1` | behåll den temporära datamappen (inkl. `app.log`) efter körning |
| `E2E_WORKERS=n` | antal parallella workers (standard 1; varje worker startar en egen app) |
| `CHROMIUM_EXECUTABLE_PATH` | använd en installerad Chromium (devcontainern: `/usr/bin/chromium`) |

Utan nätverk till Playwrights CDN (som i devcontainern) behövs `CHROMIUM_EXECUTABLE_PATH`.
I devcontainern (2 kärnor, ~3,8 GB RAM) tar hela sviten ett par minuter inklusive bygge, och ett
enskilt scenario några sekunder mot en varm app. Timeouterna i `playwright.config.ts` är satta för
en betydligt klenare maskin (1 kärna) och är med flit tilltagna i överkant — de är inte en
uppskattning av hur lång tid något *ska* ta.

## Hur det hänger ihop

- `playwright.config.ts` — inställningar; `testIdAttribute` är `data-testid`.
- `fixtures.ts` — `app` (en app per worker), inloggade sessioner per roll (`test.use({ role: "viewer" })`),
  `newSession(role)` för flera användare i samma scenario, och **vakten** (`guard`) som får varje test
  att misslyckas vid JS-fel, 4xx/5xx, misslyckade anrop eller oväntade dialoger.
- `support/` — `app.ts` startar/stoppar appen med en tom temporär datamapp, `members.ts` skriver
  `members.json` med ett konto per roll (lösenord `e2e-password`).
- `pages/` — sidobjekt (`OrderListPage`): allt som vet hur HTML:en ser ut bor här, inte i testerna.
- `tests/` — scenarierna, märkta med ID från SCENARIOS.md.

## Konventioner

- **Element hittas via `data-testid`** (satta i Razor-vyerna), inte via text eller CSS. Text används bara
  när texten *är* det som testas. Ändras HTML:en måste `data-testid` följa med — annars säger testet
  tydligt vilket element som saknas.
- **Varje scenario äger sin demo-order** (`ref-ny-001`, `ref-atgarda-002`, …). Låsen och sökindexet
  ligger i appens minne och delas inom en worker, så två scenarier får inte ändra samma order.
  Behöver ett scenario en orörd app: använd `startApp(label)` själv.
- Dialoger avvisas automatiskt och noteras av vakten; vänta aldrig på `networkidle` (SignalR håller
  anslutningar öppna). Behöver ett scenario svara **OK** på en `confirm()` används
  `guard.accept(/mönster/)`.
- **En kontext som skapas med `browser.newContext()` ärver testets `use`-inställningar, inklusive
  `storageState`.** Ett test som vill ha en genuint anonym kontext måste alltså ligga under
  `test.use({ role: "anonymous" })` — annars är den inloggad som testets roll, och ett
  behörighetsprov bevisar ingenting. Det kostade en lång felsökning: en "anonym" sond som i själva
  verket var inloggad fick orderlistan och QR-endpointen att se helt öppna ut.
- Ett scenario som medvetet provocerar fram ett fel deklarerar det med `guard.allow(/mönster/)`.
