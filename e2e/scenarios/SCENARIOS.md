# Scenarier för end-to-end-testerna

**Det här är filen du skriver i.** Varje rad är något som ska vara sant i appen. Du behöver inte
skriva steg eller selektorer — bara vad du förväntar dig att se. Claude gör om raderna till
körbara Playwright-test i `e2e/tests/`, och därefter körs de utan Claude (`npm test` i `e2e/`).

## Så skriver du ett scenario

```
- [ ] ORD-012  Som Administrator: när jag byter ordertyp syns den nya typen i listan direkt.
```

- **ID:** prefix efter område (se [Områden](#områden)) + löpnummer. Återanvänd aldrig ett ID; stryk
  en rad (`~~…~~`) om den inte längre gäller.
- **Roll:** börja med vem som gör det (`Som Administrator`, `Som Viewer`, `Som två användare`). Finns:
  superadmin, admin, viewer, roleless (se roll-tabellen längst ned).
- **Förväntat resultat** i en mening. Skriv gärna *varför* om felet är ett tidigare fel du hittat
  (”regression för 0762fd9”) — då vet vi att raden inte får tas bort.
- `[ ]` = ännu inte automatiserad, `[x]` = automatiserad (filen och testets ID står i kolumnen nedan).
- Du behöver inte veta om det går att testa. Skriv det du vill veta; Claude säger till om något inte går.

## Områden

Prefixen följer appens ytor. De är inte heliga — hittar du en bättre indelning när du skriver, så
ändrar vi.

| Prefix | Vad som hör hit | Var i appen |
|--------|-----------------|-------------|
| `SMOKE` | Att appen alls lever: inloggning funkar, orderlistan renderar, inga 404 på script/CSS | `/ChalmersILLLoginPage`, `/bestaellningar` |
| `ORD` | En enskild order: typ, status, referens, leveransbibliotek, inköpsbibliotek, beställardata, logganteckning, kopia, anonymisering | panelen som fälls ut när du klickar en rad (`Chalmers.ILL.OrderItem.cshtml` + ~20 `OrderItem*SurfaceController`) |
| `LIST` | Orderlistan *som lista*: fritextsök, statusfilter, biblioteksfilter, sortering på typ/status, sidbrytning vid >50 ordrar, siffrorna i filterknapparna | `ChalmersILLOrderListPage.cshtml`, `OrderItemSearchSurfaceController` |
| `LOCK` | Samtidighet: vem som håller låset, vad andra ser, överta lås, lås som släpps | `EditedBy`, `LockOrderItem`/`UnlockOrderItem`, `RequiresOrderLockFilter` |
| `ROLE` | Behörighet: vad varje roll får se och göra, och att servern faktiskt stoppar — inte bara att knappen är dold | `ViewerReadOnlyFilter`, `[AllowViewer]`, globala `[Authorize]` |
| `MAIL` | Utgående mail: mallar, signatur, historik, att rätt mail skickas vid rätt leveranstyp | `Chalmers.ILL.Action.Mail.cshtml`, `Partials/DeliveryType/*` (7 leveranstyper), `OrderItemMailSurfaceController` |
| `DELIV` | Leveransflödet: ta emot bok, lämna ut, återsända, kräva, återlämningsdatum för låntagare och leverantör | `Action.ReceiveBook`/`Return`/`Claim`/`PatronReturnDate`/`ProviderReturnDate` |
| `SET` | Inställningssidan: mallar, chillin-texter, leverantörsdata, byt lösenord, kontoadministration | `ChalmersILLSettingsPage.cshtml`, `Partials/Settings/*` |
| `STAT` | Statistiksidan: urval, datumintervall, exporter | `ChalmersILLStatisticsPage.cshtml`, `StatisticsSurfaceController` |
| `IMPORT` | Uppladdning av dokument till en order | `ImportDocumentSurfaceController`, `MediaItemSurfaceController` |
| `API` | Maskin-till-maskin, utan inloggad användare: QR-skanning vid ut-/återlämning och data till Sierra | `BookCirculationSurfaceController` (`Loaned`/`Returned`), `PublicDataSurfaceController` — de två som fas 0a släppte ur globala `[Authorize]` |
| `RT` | Realtid: att en ändring i ett fönster syns i ett annat via SignalR | `notificationHub`, `updateStream` i `chalmers.ill.js` |

Två ytor är medvetet utelämnade:

- **`ChalmersILLDiskPage`** är en återvändsgränd — sidan säger bara ”Diskapp används ej längre”.
- **Desk-rollen** används inte längre, se roll-tabellen längst ned.

Och en sak att veta om `RT`: `Notifier` skickar via `Clients.All`, vilket bara når klienter anslutna
till *samma* instans. Ett RT-scenario bevisar alltså att realtid funkar inom en instans — vilket är
allt som gäller så länge appen körs på en enda instans, men inte mer än så.

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
- [ ] ROLE-003  Som Viewer: Inställningar-sidan är dold/stoppad. Som Administrator: den syns.
- [ ] ORD-003  Som Administrator: statusbyte (t.ex. Ny → Åtgärda) syns direkt i listan.

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

**Desk-rollen testas inte** — den används inte längre. Kontot finns kvar i `members.json` så att
det går att logga in som ett rent Desk-konto om frågan skulle dyka upp, men inga scenarier använder
det. (Värt att veta om du ändå provar: `ChalmersILL.cshtml` lägger in
`<style>.editmode .btn { display:none }</style>` för alla konton utan rollen `Administrator`, så ett
rent Desk-konto ser inga knappar alls i en öppnad order — gammalt beteende, enbart kosmetiskt.)

| Roll | Roller i `members.json` | Orderlista | Ändra order | Inställningar | Kontoadmin |
|------|-------------------------|------------|-------------|---------------|------------|
| superadmin | Desk, Administrator, SuperAdmin | ja | ja | ja | ja |
| admin | Desk, Administrator | ja | ja | ja | nej |
| desk | Desk | *(används inte längre — testas inte)* | | | |
| viewer | Viewer | ja (läsa) | nej (stoppas, utom `[AllowViewer]`) | nej | nej |
| roleless | *(inga)* | nej (stoppas helt) | nej | nej | nej |
