# Scenarier för end-to-end-testerna

**Det här är filen du skriver i.** Varje rad är något som ska vara sant i appen. Du behöver inte
skriva steg eller selektorer — bara vad du förväntar dig att se. Claude gör om raderna till
körbara Playwright-test i `e2e/tests/`, och därefter körs de utan Claude (`npm test` i `e2e/`).

## Så skriver du ett scenario

```
- [ ] ORD-099  Som Administrator: när jag byter ordertyp syns den nya typen i listan direkt.
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
| `INBOX` | Inkommande beställningar: mail som blir ordrar när mailhämtningen körs | `SystemSurfaceController.Update`, `FileMailWebApi` (inkorgen är `DataPath/mail/inbox` i isolerat läge) |
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
| ORD-003 | Statusbyte syns direkt i listan | `tests/orders.spec.ts` |
| ORD-004 | Annullering kräver orsak, och orsaken sparas på ordern | `tests/orders.spec.ts` |
| ORD-005 | Inköpt kräver materialtyp | `tests/orders.spec.ts` |
| ORD-006 | Byte av leveransbibliotek syns i listans bibliotekskolumn | `tests/orders.spec.ts` |
| ORD-007 | Inköpsförslag kan få inköpsbibliotek, som syns i typkolumnen | `tests/orders.spec.ts` |
| ORD-008 | Logganteckning sparas och visas med datum och författare | `tests/orders.spec.ts` |
| ORD-009 | Alla 17 seed-ordrars paneler renderar utan undantag (regression 5370fa7) | `tests/orders.spec.ts` |
| ORD-010 | Beställardata visar namn, e-post och kortnummer | `tests/orders.spec.ts` |
| ORD-011 | ”Skapa kopia” ger en andra order med egen identitet | `tests/orders.spec.ts` |
| ORD-013 | Ett misslyckat sparande säger ifrån, och busy-overlayen släpper (regression 1410e49, f1bd8bf) | `tests/orders.spec.ts` |
| ORD-014 | Orderpanelen öppnas utan JS-fel för varje status (regression 279e039) | `tests/orders.spec.ts` |
| SET-001 | Nytt lösenord gäller vid nästa inloggning, gammalt slutar gälla | `tests/settings.spec.ts` |
| SET-002 | Fel nuvarande lösenord nekas, och det gamla fortsätter gälla | `tests/settings.spec.ts` |
| SET-004 | Mallfliken listar mallar och en ny mall går att skapa (regression edbb71f) | `tests/settings.spec.ts` |
| SET-005 | Ändrad chillin-text är kvar efter omladdning | `tests/settings.spec.ts` |
| SET-007 | Nytt konto kan logga in | `tests/settings.spec.ts` |
| SET-009 | Borttaget konto kan inte logga in | `tests/settings.spec.ts` |
| API-001 | QR-skanning av utlån sätter Utlånad | `tests/api.spec.ts` |
| API-002 | QR-skanning av återlämning sätter Transport | `tests/api.spec.ts` |
| API-003 | Okänt ordernummer besvaras i stället för att krascha | `tests/api.spec.ts` |
| API-004 | Cirkulations-endpointsen kräver inloggning | `tests/api.spec.ts` |
| API-005 | Sierra-endpointen svarar utan inloggning, med CORS | `tests/api.spec.ts` |
| STAT-001 | Antal ordrar per variabel ger en tabell med siffror | `tests/statistics.spec.ts` |
| STAT-002 | Medel- och medianvärde av handläggningstid går att välja och räknas ut | `tests/statistics.spec.ts` |
| STAT-004 | CSV-exporten innehåller samma siffror som tabellen | `tests/statistics.spec.ts` |
| RT-001 | Ändring i ett fönster syns i ett annat utan omladdning | `tests/realtime.spec.ts` |
| RT-002 | Räknaren i menyn följer en inkommande order | `tests/realtime.spec.ts` |
| RT-003 | Realtiden kommer tillbaka av sig själv efter serveromstart | `tests/realtime.spec.ts` |
| MAIL-001 | Mail till låntagaren hamnar verkligen i utkorgen, med ordernumret i ämnet | `tests/mail.spec.ts` |
| MAIL-002 | En mall fylls i med orderns egna uppgifter, inga platshållare kvar (täcker även SET-003) | `tests/mail.spec.ts` |
| MAIL-003 | Mailet går inte att skicka utan vald status, och inget skickas | `tests/mail.spec.ts` |
| DELIV-002 | Leverans av artikel mailar låntagaren och flyttar ordern vidare | `tests/delivery.spec.ts` |
| DELIV-004 | ”Retur” sätter status Återsänd | `tests/delivery.spec.ts` |
| INBOX-002 | Ordern bär låntagarens uppgifter och säger att den kom från mail | `tests/inbox.spec.ts` |
| INBOX-003 | Ett mail som inte är en beställning blir ingen halvskapad order | `tests/inbox.spec.ts` |
| IMPORT-001 | Uppladdat dokument bifogas ordern och räknas | `tests/import.spec.ts` |
| IMPORT-003 | Uppladdning kräver orderns lås | `tests/import.spec.ts` |
| LOCK-001 | En order som en användare öppnat visas som låst för en annan, med ”Överta låset” och utan åtgärdsknappar (regression c8d5ad8, e412340) | `tests/locks.spec.ts` |
| LOCK-002 | Omladdning släpper de lås sessionen håller (regression 556b30c) | `tests/locks.spec.ts` |
| LOCK-003 | ”Överta låset” flyttar låset, och den förra redaktören får veta det | `tests/locks.spec.ts` |
| LOCK-004 | Att stänga en order släpper dess lås | `tests/locks.spec.ts` |
| LOCK-005 | Servern nekar en ändring från den som inte håller låset (regression c8d5ad8) | `tests/locks.spec.ts` |
| ROLE-001 | Viewer kan läsa orderlistan men ingen ändringsknapp är användbar | `tests/roles.spec.ts` |
| ROLE-002 | Konto utan roller nekas på alla sidor | `tests/roles.spec.ts` |
| ROLE-003 | Viewer når Inställningar men kan inte ändra något där | `tests/roles.spec.ts` |
| ROLE-005 | Servern nekar en statusändring från Viewer | `tests/roles.spec.ts` |
| ROLE-006 | Åtkomst-nekad-sidan renderar, och utloggning fungerar därifrån (regression 6add6f9) | `tests/roles.spec.ts` |
| ROLE-007 | Inloggningssida och statiska filer nås utan inloggning (regression 6add6f9) | `tests/roles.spec.ts` |
| ROLE-008 | Viewer kan öppna en order och läsa detaljerna (`[AllowViewer]`) | `tests/roles.spec.ts` |
| ROLE-009 | Administrator utan SuperAdmin ser inte och når inte Konton | `tests/roles.spec.ts` |
| ROLE-010 | SuperAdmin ser Konton och får listan | `tests/roles.spec.ts` |
| INBOX-001 | Ett beställningsmail blir en order med status Ny när pollningen körs | `tests/inbox.spec.ts` |
| SMOKE-003 | Fel lösenord ger ett felmeddelande på inloggningssidan (regression 180134d) | `tests/smoke.spec.ts` |
| SMOKE-004 | Anonym besökare skickas till inloggningen från en skyddad sida | `tests/smoke.spec.ts` |
| SMOKE-005 | Utloggning avslutar sessionen och stänger orderlistan | `tests/smoke.spec.ts` |
| SMOKE-006 | Alla sidor i menyn renderar (vakten fångar 404 bakom dem) | `tests/smoke.spec.ts` |
| LIST-001 | Fritextsökning på låntagarnamn ger bara matchande ordrar | `tests/list.spec.ts` |
| LIST-002 | Fältsökning på status ger bara den statusen | `tests/list.spec.ts` |
| LIST-003 | Statusfiltret visar rätt rader och badgen stämmer | `tests/list.spec.ts` |
| LIST-004 | Biblioteksfiltret visar rätt rader | `tests/list.spec.ts` |
| LIST-005 | Status- och biblioteksfilter ger snittet | `tests/list.spec.ts` |
| LIST-006 | Sortering på typ och tillbaka på status återställer serverns ordning (regression: klientens viktlista var inte serverns) | `tests/list.spec.ts` |
| LIST-008 | Sök-dropdownens snabbval hittar de förlorade ordrarna | `tests/list.spec.ts` |
| LIST-009 | Sökning utan träffar säger det i stället för att fallera | `tests/list.spec.ts` |
| LIST-010 | Värde med kolon är sökbart inom citattecken | `tests/list.spec.ts` |

## Att automatisera (börja skriva här)

Allt nedanför är mina förslag, inte dina önskemål — stryk fritt. De är sorterade per område och
ordnade ungefär efter hur mycket de är värda i förhållande till vad de kostar att skriva.

**Så får ett scenario en egen order:** `createOrderThroughMail(page, "etikett")` i
`pages/StartPage.ts` fyller i ”NY BESTÄLLNING!”-formuläret på startsidan och klickar på
pollningsknappen (chilli-ikonen nere till vänster) — samma väg som en riktig beställning tar.
Den returnerar orderns referens. Använd den tillsammans med `ownApp`-fixturen, eftersom
pollningen också kör det dagliga underhållet.

**Två fällor som flera scenarier gick i:**

- *En ny order har ingen typ,* och Status-knappen finns inte förrän den fått en. Sätt typ först.
- *Default-listan visar bara pending-statusar — men först efter omladdning.* En order som sätts
  till Annullerad, Inköpt, Levererad m.fl. står kvar i listan med sin nya status, och panelen
  förblir öppen; det är först vid nästa sidladdning som raden inte kommer med. Ett scenario som
  laddar om måste därför använda `list.find(referens)` i stället för `list.reload()`.

### SMOKE — att appen lever


### LIST — orderlistan som lista

- [ ] LIST-007  Med fler än 50 ordrar syns sidbrytningen, och ”Gå till nästa sida” behåller sökningen.
  *(Kvar: seed-datan har 17 ordrar, och att skapa 34 till genom formuläret ett i taget tar minuter.
  Behöver troligen en egen app med fler seedade ordrar.)*

### ORD — en enskild order

- ~~ORD-012~~ *(anonymiseringen ska inte testas — Lars 2026-10-02.)*

### MAIL — utgående mail

- [ ] MAIL-004  Ursprunglig beställning / historik går att bifoga och följer med i mailet.
- [ ] MAIL-005  Automatiskt utskick: en order med status Utlånad och återlämning om exakt fem dagar ger
  ett artighetsmeddelande när utskicket körs.
- [ ] MAIL-006  Automatiskt utskick: ett missat datum skickas inte retroaktivt.

### DELIV — leveransflödet

- [ ] DELIV-001  ”Ta emot bok” sätter status Mottagen och loggar händelsen.
- ~~DELIV-003~~ *(går inte att skriva mot en ny order, och det är ett fynd i sig: ”Kräv”-knappen —
  och ”Lånetid mot låntagare” — visas bara om `Model.OrderItem.CreateDate <= 2021-05-16`, en
  hårdkodad gräns i `Chalmers.ILL.OrderItem.cshtml`. Funktionerna är alltså oåtkomliga för varje
  order som beställts de senaste fyra åren, i drift såväl som i test. Avsiktligt eller kvarglömt?)*
- ~~DELIV-005~~ *(samma datumspärr som DELIV-003.)*
- [ ] DELIV-006  Lånetid från utlånande bibliotek går att sätta.
- [ ] DELIV-007  Räknaren på Leverans-knappen visar antalet bifogade filer.

### INBOX — inkommande beställningar


### IMPORT — uppladdade dokument

- [ ] IMPORT-002  En för stor fil avvisas med ett begripligt fel.
  *(Kvar med flit: gränsen ska enligt fas 10 stämma med App Service och är inte satt än i isolerat
  läge. Ett test nu skulle spika fast fel gräns.)*

### SET — inställningar

- [ ] SET-006  En ny leverantör går att lägga till och kan väljas under ”Beställning”.
- [ ] SET-008  Ändrade roller på ett konto slår igenom direkt vid nästa inloggning.
- [ ] SET-010  Ett fel i kontoadmin släpper busy-animationen i stället för att låsa sidan
  (regression f1bd8bf).

### STAT — statistik

- [ ] STAT-003  Ett filter i en variabel begränsar resultatet.
  *(Kvar: filteralternativen byggs dynamiskt från sökindexet, vilket kräver mer utforskning än de
  tre andra STAT-scenarierna tillsammans.)*

### API — maskin-till-maskin

- ~~API-001~~ *(mitt utkast påstod att QR-skanningen sätter Utlånad **utan** inloggning. Det gör
  den inte: bara `SystemSurfaceController` och `PublicDataSurfaceController` är `[AllowAnonymous]`.
  Automatiserat i den form koden faktiskt har — se API-001..005 i tabellen ovan.)*

### RT — realtid


*(Lägg till egna rader här. Skriv hellre för många än för få — det går fort att stryka.)*

## Öppna frågor som testerna väckt

- ~~Var ska en inloggning landa?~~ **Besvarat 2026-10-02:** `Desk` används inte alls — alla som
  arbetar i systemet och ska kunna ändra något har `Administrator`. Grenen i
  `LoginSurfaceController.HandleLogin` som skickar `Desk`-konton till `/disk/` kan alltså aldrig
  träffa ett riktigt konto. Testkontona har inte längre `Desk` (`support/members.ts`), så de
  speglar verkligheten. Kvar som fråga, men mindre brådskande: ska den grenen och
  `ChalmersILLDiskPage` tas bort helt?
- **Åtkomst-nekad-sidan** använder huvudlayouten och anropar därmed `GetLocksForCurrentMember`,
  vilket är precis vad ett rollöst konto inte får. Varje visning avfyrar en 403 som ingenting
  reagerar på. Deklarerad i ROLE-002 och ROLE-006.
- **En egen `browser.newContext()` ärver testets `storageState`.** Ett scenario som vill vara
  anonymt måste ligga under `test.use({ role: "anonymous" })`. Utan det körs det som testets roll,
  och ett behörighetsprov blir grönt utan att bevisa något. Det fick mig att tro att orderlistan
  och QR-endpointen var öppna — de är de inte.
- **”Kräv” och ”Lånetid mot låntagare” är spärrade för allt beställt efter 2021-05-16** av en
  hårdkodad datumjämförelse i orderpanelen. Ingen ny order kan nå dem.
- **Mailvyns mallista är tom i isolerat läge.** De 13 seedade systemmallarna är alla `Automatic`,
  och mailvyn visar bara manuella (`GetManualTemplates`). Den som provar testservern för hand möter
  alltså en tom lista tills någon skapar en mall under Inställningar. MAIL-002 skapar sin egen.
- ~~Låsrutorna är på engelska~~ **Åtgärdat 2026-10-02:** alla låsmeddelanden från
  `OrderItemSurfaceController` och `chalmers.ill.js` är svenska nu.

## Det vakten fångar automatiskt i varje test

Du behöver inte skriva scenarier för dessa; **alla** test misslyckas om något av följande händer:

- ett ofångat JavaScript-fel i webbläsaren
- `console.error`
- ett anrop till appen som svarar 4xx/5xx (fel i en AJAX-rutt, saknad script/CSS-fil)
- ett anrop som avbryts/misslyckas
- en oväntad `alert`/`confirm`-ruta

Ett test som *avsiktligt* provocerar fram något sådant säger det uttryckligen med
`guard.allow(/mönster/)`. Behöver testet dessutom svara **OK** på en `confirm()` — vakten svarar
annars nej på allt, eftersom en öppen dialog låser webbläsaren — används `guard.accept(/mönster/)`. SignalR-reservvägen (WebSocket → long polling) är undantagen eftersom den är
ofarlig.

## Roll × område (utkast, granska!)

Detta är min läsning av `ViewerReadOnlyFilter` m.fl. — rätta det som är fel, så blir det ett
automatiskt test (ROLE-xxx). ROLE-001..010 är nu skrivna, och tabellen stämmer med dem —
~~ROLE-004~~ utgick när desk-rollen togs bort.

**Rollen `Desk` används inte** och finns inte på något testkonto. Alla som arbetar i systemet och
ska kunna ändra något har `Administrator` — vilket också är vad `ChalmersILL.cshtml` kräver för att
visa knapparna i en öppnad order (`<style>.editmode .btn { display:none }</style>` för alla utan
den rollen).

| Roll | Roller i `members.json` | Orderlista | Ändra order | Inställningar | Kontoadmin |
|------|-------------------------|------------|-------------|---------------|------------|
| superadmin | Administrator, SuperAdmin | ja | ja | ja | ja |
| admin | Administrator | ja | ja | ja | nej |
| viewer | Viewer | ja (läsa) | nej (stoppas, utom `[AllowViewer]`) | ja (läsa; lösenord är självbetjäning) | nej |
| roleless | *(inga)* | nej (stoppas helt) | nej | nej | nej |
