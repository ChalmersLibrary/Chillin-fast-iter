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
- *Default-listan visar bara pending-statusar.* En order som sätts till Annullerad, Inköpt,
  Levererad m.fl. försvinner ur listan och nås bara via sökning — använd `list.find(referens)`,
  inte `list.reload()`. Att raden är borta betyder alltså inte att ändringen inte sparades.

### SMOKE — att appen lever


### LIST — orderlistan som lista

- [ ] LIST-007  Med fler än 50 ordrar syns sidbrytningen, och ”Gå till nästa sida” behåller sökningen.
  *(Kvar: seed-datan har 17 ordrar, och att skapa 34 till genom formuläret ett i taget tar minuter.
  Behöver troligen en egen app med fler seedade ordrar.)*

### ORD — en enskild order

- [ ] ORD-008  En logganteckning sparas och syns i händelselistan med rätt datum och användare
  (regression: datumen kom en gång ut som `/Date(…)/`).
- [ ] ORD-009  Händelselistan grupperas per datum utan att krascha (regression 5370fa7).
- [ ] ORD-010  Beställardata visar låntagarens namn, e-post och kortnummer.
- [ ] ORD-011  ”Skapa kopia” skapar en ny order med samma uppgifter och egen lås-status.
- [ ] ORD-012  Anonymisering tar bort låntagaruppgifterna men behåller ordern och dess historik.
- [ ] ORD-013  Ett sparande som misslyckas visar ett fel i stället för att se ut att lyckas
  (regression 1410e49) — skriv gärna fler sådana; det var den buggklassen som var vanligast.
- [ ] ORD-014  Orderdetaljvyn renderar utan JS-fel för en order i varje status (regression 279e039).

### MAIL — utgående mail

- [ ] MAIL-001  Som Administrator: ”Skicka mail” med en mall fyller i ämne och text, och det skickade
  mailet hamnar i utkorgen (`DataPath/mail/sentitems`).
- [ ] MAIL-002  Mallvariablerna expanderas — låntagarens namn och ordernumret står i mailet, inte
  platshållare.
- [ ] MAIL-003  Min kontosignatur kommer med i mailet.
- [ ] MAIL-004  Ursprunglig beställning / historik går att bifoga och följer med i mailet.
- [ ] MAIL-005  Automatiskt utskick: en order med status Utlånad och återlämning om exakt fem dagar ger
  ett artighetsmeddelande när utskicket körs.
- [ ] MAIL-006  Automatiskt utskick: ett missat datum skickas inte retroaktivt.

### DELIV — leveransflödet

- [ ] DELIV-001  ”Ta emot bok” sätter status Mottagen och loggar händelsen.
- [ ] DELIV-002  Vart och ett av de sju leveranssätten sätter rätt status och skickar rätt mail.
- [ ] DELIV-003  ”Kräv” loggar kravet och skickar mail till låntagaren.
- [ ] DELIV-004  ”Retur” sätter status Återsänd.
- [ ] DELIV-005  Lånetid mot låntagare går att sätta och syns på ordern.
- [ ] DELIV-006  Lånetid från utlånande bibliotek går att sätta.
- [ ] DELIV-007  Räknaren på Leverans-knappen visar antalet bifogade filer.

### INBOX — inkommande beställningar

- [ ] INBOX-002  Ursprunglig beställning sparas på ordern och går att läsa under ”Beställning”.
- [ ] INBOX-003  Ett mail som inte går att tolka ger inte en halvskapad order.

### IMPORT — uppladdade dokument

- [ ] IMPORT-001  En uppladdad PDF syns i Leverans-listan och går att öppna igen.
- [ ] IMPORT-002  En för stor fil avvisas med ett begripligt fel (gränsen ska stämma med App Service,
  se fas 10).
- [ ] IMPORT-003  Uppladdning kräver lås på ordern.

### SET — inställningar

- [ ] SET-001  Byt lösenord: det nya lösenordet fungerar vid nästa inloggning och det gamla slutar gälla.
- [ ] SET-002  Fel nuvarande lösenord ger ett felmeddelande.
- [ ] SET-003  En ändrad mall slår igenom i nästa mailutskick.
- [ ] SET-004  Mallfliken fungerar även när det inte finns några mallar (regression edbb71f).
- [ ] SET-005  En ändrad chillin-text syns där den används.
- [ ] SET-006  En ny leverantör går att lägga till och kan väljas under ”Beställning”.
- [ ] SET-007  Som superadmin: ett nytt konto går att skapa och kan logga in.
- [ ] SET-008  Ändrade roller på ett konto slår igenom direkt vid nästa inloggning.
- [ ] SET-009  Ett borttaget konto kan inte logga in.
- [ ] SET-010  Ett fel i kontoadmin släpper busy-animationen i stället för att låsa sidan
  (regression f1bd8bf).

### STAT — statistik

- [ ] STAT-001  ”Antal ordrar” per status ger en tabell vars siffror stämmer med seed-datan.
- [ ] STAT-002  Medelvärde och median av handläggningstid går att välja och räknas ut.
- [ ] STAT-003  Ett filter i en variabel begränsar resultatet.
- [ ] STAT-004  ”Ladda ner som CSV” ger en fil med samma siffror som tabellen.

### API — maskin-till-maskin

- [ ] API-001  QR-skanning vid utlåning (`BookCirculationSurface/Loaned`) sätter Utlånad **utan**
  inloggning (fas 0a släppte de här två ur globala `[Authorize]`).
- [ ] API-002  QR-skanning vid återlämning (`Returned`) sätter rätt status.
- [ ] API-003  Ett okänt ordernummer ger ett vettigt svar, inte en krasch.
- [ ] API-004  `PublicDataSurface/GetChillinDataForSierraPatron` svarar med låntagarens ordrar.

### RT — realtid

- [ ] RT-001  Som två användare: en statusändring i fönster A syns i fönster B utan omladdning.
- [ ] RT-002  Räknaren ”Beställningar” i menyn ökar när en ny order kommer in och minskar när den
  åtgärdas.
- [ ] RT-003  När WebSocket inte går igenom faller SignalR tillbaka till long polling utan att något
  går sönder (vakten släpper igenom just den varningen).

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
automatiskt test (ROLE-xxx). ROLE-001..010 är nu skrivna, och tabellen stämmer med dem —
~~ROLE-004~~ utgick när desk-rollen togs bort.

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
| viewer | Viewer | ja (läsa) | nej (stoppas, utom `[AllowViewer]`) | ja (läsa; lösenord är självbetjäning) | nej |
| roleless | *(inga)* | nej (stoppas helt) | nej | nej | nej |
