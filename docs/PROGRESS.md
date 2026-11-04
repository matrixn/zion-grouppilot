# Progres GroupPilot

## 0.00.18 · Editare, pornire manuală și instanță unică

- [x] Campanii fără dată de început și opțiune de rulare înaintea datei programate.
- [x] Editarea campaniilor cu păstrarea istoricului și a rundelor anterioare.
- [x] Eliminarea confirmării intermediare la pornirea unei ținte noi și mesaje vizibile pentru pregătirea distribuirii.
- [x] Instanță unică prin Windows App SDK, cu redirecționarea activării.
- [x] Build x64 fără avertismente sau erori; 37 de teste xUnit trecute; modelul EF corespunde migrărilor.
- [x] Lansare Windows verificată: a doua lansare se închide, iar procesul și fereastra inițiale rămân active.
- [x] Editorul Facebook a fost deschis în verificarea live. Bara de campanie a fost compactată și detectarea editorului extinsă ulterior.
- [ ] Reverificarea live a inserării linkului cu ultima variantă de detectare a editorului Facebook.
- [ ] Verificarea trimiterii efective într-un grup Facebook autorizat.

## 0.00.17 · Campanii recurente și formular organizat

- [x] Formular pe secțiuni numerotate, cu selectori delimitați și acțiuni de salvare vizibile permanent.
- [x] Recurențe zilnice, la 2–3 zile, săptămânale și de 1–4 ori pe lună, cu păstrarea istoricului rundelor.
- [x] Contor de grupuri și tooltip cu lista din campanie.
- [x] Pauză după distribuire între grupuri diferite și notificări pentru campaniile scadente.
- [x] Verificare vizuală în aplicația Windows a secțiunilor, selectorului de recurență și butoanelor fixe de salvare.
- [x] 34 de teste trecute, inclusiv date calendaristice, oră de vară, păstrarea istoricului și pauza după distribuire.

## 0.00.16 · Pauză între distribuiri

- [x] Pauză configurabilă per campanie, implicit 120 de secunde, cu următoarea disponibilitate salvată în SQLite pentru profilul local Facebook.
- [x] Numărătoare inversă cu oprire și pregătirea linkului în editorul grupului selectat.
- [ ] Verificare într-un grup Facebook autorizat, inclusiv previzualizarea linkului și trimiterea manuală.

## 0.00.15 · Carduri de grup și ștergere de campanii

- [x] Carduri cu miniaturi de grup, intervale generate din date și sortare după membri.
- [x] Ștergere de campanie cu păstrarea istoricului și a rezultatelor.
- [ ] Verificarea vizuală cu imagini dintr-o sesiune Facebook autentificată.

## 0.00.14 · Rulare imediată și selector vizual

- [x] Pornire imediată din lista de campanii, din detalii sau după salvare, cu trecere la următorul grup după înregistrarea rezultatului.
- [x] Selector de postare cu imagine, titlu, extras și link.
- [ ] Verificarea vizuală a selectorului și confirmarea distribuirii într-un grup de test.

## 0.00.13 · Distribuire din biblioteca Facebook

- [x] Interfață concentrată pe importul și distribuirea postărilor existente pe Facebook.
- [x] Imagini mai mari, afișate integral în carduri.

## 0.00.12 · Carduri și ștergere din bibliotecă

- [x] Carduri de postare cu text, imagine locală sau miniatură Facebook și acțiuni individuale.
- [x] Ștergere cu confirmare și arhivare pentru postările folosite în campanii.
- [ ] Verificarea miniaturilor Facebook dintr-o sesiune autentificată.

## 0.00.11 · Import din pagini publice Facebook

- [x] Detectarea permalinkurilor directe și a postărilor din carduri fără marcajul anterior.
- [ ] Verificare live în sesiunea WebView2 de pe pagina ZionFood.

## 0.00.10 · Versiune în bara de titlu

- [x] Versiunea este citită din metadatele buildului și afișată în titlul ferestrei.

## 0.00.09 · Import contextual de postari

- [x] Importul de postari alege intre lista profilului si postarea individuala, in functie de pagina deschisa.

## 0.00.08 · Import de postări

- [x] Importul a până la zece postări afișate într-un profil sau într-o pagină Facebook, cu previzualizare și deduplicare.
- [ ] Verificare a detectării postărilor într-un profil Facebook autentificat.

## 0.00.07 · Browser integrat și scanare automată

- [x] WebView2 integrat în fereastra principală, cu profilul existent.
- [x] Detectare pagină de grupuri, derulare automată și previzualizare înainte de actualizarea directorului.
- [ ] Verificare a listei dinamice într-un cont Facebook autentificat.

## 0.00.06 · Metadate și sincronizare Facebook

- [x] Extragere metadate afișate în lista de grupuri, migrare SQLite și filtre de vizibilitate și membri.
- [x] Bară de navigare compactă și sincronizare cu previzualizare, confirmare și salvare tranzacțională.
- [ ] Verificare vizuală și validare a metadatelor pe lista reală a unui cont autentificat.

## 0.00.05 · Import de grupuri

- [x] Previzualizare și import din pagina de grupuri încărcată în WebView2, cu validarea linkurilor și deduplicare în SQLite.
- [ ] Verificare cu lista reală a unui cont Facebook autentificat.

## 0.00.04 · Facebook Integration & Workflow Validation

- [x] Fereastră WebView2 separată, cu profil reutilizat și opțiune pentru browserul implicit.
- [x] Import de postări pe baza permalinkului și a metadatelor disponibile în documentul deschis.
- [x] Verificare manuală a grupurilor cu reguli, acces și dată. Modificarea URL-ului invalidează verificarea.
- [x] Ținte persistente în lucru, reluare după redeschidere și rezultat separat pentru moderare.
- [x] Build x64 și 16 teste xUnit. Verificate lansarea ferestrei dedicate, inițializarea WebView2 și încărcarea paginii Facebook printr-o probă de pornire.
- [ ] Confirmare interactivă cu un cont Facebook și un grup de test autorizat, inclusiv importul unui permalink real și publicarea manuală. Helperul Windows nu a putut accesa fereastra.

## 0.00.03 · Layout și identitate vizuală

- [x] Layout comun aliniat la stânga, cu Dashboard pe carduri, secțiuni de formular și grile care se reașază la redimensionare.
- [x] Branding F în bara de titlu, iconul executabilului, resursele MSIX, splash și notificări.
- [x] Build x64, 8 teste xUnit și inițializarea Dashboardului după lansare.
- [ ] Verificare vizuală interactivă pe mai multe dimensiuni de fereastră. Helperul Windows a raportat că pipe-ul nativ este indisponibil.

## 0.00.02 · Native Windows Foundation

- [x] Migrare EF Core pentru conturi sociale, grupuri, categorii, postări, media, variante, campanii, ținte, încercări de publicare și preferințe.
- [x] Separare între modelele și validarea din Core, persistența SQLite din Infrastructure și UI-ul WinUI din App.
- [x] Fluxuri locale pentru drafturi și remixuri, grupuri și import CSV, campanii, reluarea țintelor și jurnalul de publicare.
- [x] Browser Facebook WebView2 cu director de profil separat. Browserul poate prelua URL-ul și titlul paginii curente pentru un draft de tip Existing. Publicarea și atașarea imaginilor se fac în Facebook, cu confirmarea utilizatorului.
- [x] Build Debug x64 al soluției, fără erori sau avertismente.
- [x] Opt teste xUnit trecute. Acoperă validarea URL-urilor și a drafturilor, aplicarea migrării, redeschiderea SQLite, actualizarea postărilor, importul CSV, reluarea după eșec și păstrarea istoricului la ștergerea unui grup.
- [x] Lansare cu `dotnet run`, fereastră GroupPilot identificată și inițializarea Dashboardului confirmată în log.
- [ ] Verificare interactivă a paginilor și dialogurilor. Helperul de automatizare Windows a raportat că pipe-ul nativ este indisponibil.
- [ ] Verificare WebView2 cu o sesiune Facebook autorizată, publicare manuală într-un grup de test și confirmarea notificării Windows.
- [ ] Build și lansare pe x86 și ARM64.
