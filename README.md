# GroupPilot

**Versiune curentă** `0.00.19`

GroupPilot este o aplicație desktop nativă pentru Windows 11. Importă postări Facebook, organizează grupurile și pornește campanii de distribuire automată în browserul integrat. Autentificarea rămâne în Facebook.

## Dezvoltare locală

Folosește Windows 11, .NET SDK 10, Windows App Runtime și WebView2 Runtime. Proiectul WinUI țintește `net10.0-windows10.0.26100.0`. Activează **Developer Mode** pentru lansarea cu identitate de pachet. Poți deschide pagina din PowerShell cu `Start-Process "ms-settings:developers"`.

Din rădăcina repository-ului:

```powershell
dotnet restore GroupPilot.slnx
dotnet build GroupPilot.slnx
dotnet test tests/GroupPilot.Tests
dotnet run --project src/GroupPilot.App
```

Soluția include `GroupPilot.App` pentru UI-ul WinUI 3, `GroupPilot.Core` pentru modele și validare, `GroupPilot.Infrastructure` pentru persistența EF Core/SQLite și `GroupPilot.Tests` pentru testele xUnit. Aplicația aplică migrațiile la pornire. Directorul local `GroupPilot`, afișat în Settings, conține baza SQLite, profilul WebView2 și logurile Serilog. Windows poate redirecționa acest director în `LocalCache\Local` pentru aplicația lansată cu identitate MSIX.

## Implementat

- [x] Fereastră WinUI 3 cu Mica, bară nativă, navigare adaptivă și teme System, Light și Dark.
- [x] Dashboard, Post Library, Groups, Campaigns, Facebook Browser, Publishing History și Settings.
- [x] Soluție App/Core/Infrastructure/Tests, MVVM Toolkit, dependency injection, migrare SQLite inițială, loguri locale și teste xUnit.
- [x] Bibliotecă de postări existente pe Facebook, cu link, text și miniatură când sunt disponibile.
- [x] Grupuri cu editare, ștergere, căutare, categorii, note, reguli și stare activă. Importă CSV cu antetul `name,url` sau câte un URL pe rând. URL-urile echivalente sunt deduplicate.
- [x] Campanii cu postare salvată, selecție de grupuri active, dată și oră de planificare, stare per grup și reluarea țintelor în așteptare sau eșuate.
- [x] WebView2 cu profil persistent separat și navigare către postări și grupuri Facebook. Autentificarea are loc în interfața Facebook.
- [x] Publicare automată prin WebView2 la pornirea explicită a campaniei: pregătirea linkului, apăsarea Publică, verificarea rezultatului și pauză între grupuri. Rezultatele neclare opresc campania și cer verificare. Publicarea asistată rămâne disponibilă separat.
- [x] Notificare Windows pentru închiderea tuturor țintelor campaniei, unde API-ul este disponibil.

- [x] Pagini de lucru aliniate la stânga, formulare grupate și coloane care se rearanjează la redimensionare. Identitatea vizuală folosește iconul F în fereastră și în resursele Windows.

## Flux Facebook

1. Deschide **Facebook Browser** din meniul aplicației. Alege **Continue with this session** dacă ești deja autentificat în profilul GroupPilot sau autentifică-te direct în Facebook. **Open in default browser** folosește sesiunea browserului Windows, dacă există. Pentru a importa de acolo, deschide linkul postării în fereastra GroupPilot.
2. Deschide permalinkul unei postări și apasă **Import current post**. Verifică URL-ul, titlul și descrierea disponibilă, apoi salvează postarea în bibliotecă.
3. În **Groups**, selectează un grup și apasă **Review in Facebook**. Verifică regulile și dreptul contului de a posta, apoi folosește **Record group review** pentru a salva observațiile și data verificării. Accesul poate varia în funcție de cont și în timp.
4. Din campanie, alege **Run campaign now**. GroupPilot deschide pe rând grupurile selectate, introduce linkul și apasă **Post/Publică**. Salvează rezultatul pe baza unei confirmări vizibile Facebook sau a unei postări noi corelate cu sursa și un marcaj temporal recent. Dispariția editorului nu confirmă publicarea. **Stop campaign** oprește următoarele acțiuni. Păstrează aplicația deschisă.
5. Rezultatele automate sunt salvate în Publishing History cu dovada observată și URL-ul publicat când Facebook îl expune. **AwaitingApproval** înseamnă trimitere spre moderare. **NeedsReview** cere verificarea Facebook și înregistrarea manuală a rezultatului prin **Record campaign outcome** sau **Record selected outcome**. Pentru Published înregistrat manual este necesar URL-ul postării.
6. După o întrerupere, redeschide campania. Țintele InProgress și NeedsReview blochează retrimiterea automată până la verificare. Dacă postarea nu a fost trimisă, înregistrează Failed pentru a permite o reîncercare explicită; dacă există deja, înregistrează Published sau AwaitingApproval. Grupurile finalizate nu sunt repetate în aceeași rundă. **Prepare selected target** păstrează fluxul asistat pentru intervenții punctuale.

Automatizarea folosește interfața Facebook, fără API privat, export de cookie-uri sau backend. Pauza este o setare de ritm, nu o garanție împotriva restricțiilor. Programările declanșează notificări; campania pornește prin Run campaign now.

Pentru testele DOM locale (Node.js 20.19+):

```powershell
cd tests/FacebookPublishing.Tests
npm ci
npm test
```

Verificare locală în WebView2 real pe Windows, cu profil temporar și toate cererile servite din fixture, fără publicare Facebook:

```powershell
dotnet run --project tests/GroupPilot.BrowserSmoke
```

## Ce urmează

- [ ] Verifică interactiv cele opt pagini, redimensionarea, temele și dialogurile native pe Windows 11.
- [ ] Verifică autentificarea manuală în WebView2, persistența sesiunii și fluxul complet de publicare cu un grup de test autorizat.
- [ ] Verifică afișarea notificării Windows și lansarea pe x86 și ARM64.

## Istoricul versiunilor

### 0.00.19

- [x] Run campaign now rulează automat grupurile selectate, inclusiv inserarea linkului și apăsarea Publică; nu este necesar copy/paste.
- [x] Oprire accesibilă în browser, progres per grup, pauză persistentă și rezultate Published / AwaitingApproval / NeedsReview.
- [x] Marcaj SQLite salvat înainte de trimitere, apăsare unică per document și blocarea retrimiterii pentru rezultate incerte după oprire sau restart.
- [x] Oprire la autentificare, restricții, pagină schimbată, editor ocupat sau controale ambigue. Nu se ocolesc verificările Facebook.
- [x] Jurnal cu tipul automat/manual, dovada observată și permalink când este disponibil. Confirmarea explicită fără permalink se păstrează cu URL gol.
- [x] 45 de teste xUnit și 10 teste DOM; fixture local verificat și în WebView2 real.
- [ ] Verificare completă într-un grup Facebook de test: publicare reală, moderare și variantele interfeței Facebook. Automatizarea depinde de DOM și de limba EN/RO; se oprește dacă nu recunoaște interfața.

### 0.00.18

- [x] Campaniile se pot salva fără dată de început. Debifează **Set a start date and time**. Pentru recurențe, prima rulare efectivă stabilește data de referință.
- [x] **Run campaign now** permite pornirea imediată chiar dacă următoarea rulare este programată mai târziu. Dacă runda anterioară este încheiată, se cere confirmarea unei runde noi.
- [x] **Edit selected campaign** sau **Edit campaign** permite schimbarea postării, grupurilor, numelui, programării și pauzei. Istoricul grupurilor eliminate se păstrează. Schimbarea postării într-o campanie deja folosită deschide o rundă nouă.
- [x] Run deschide direct grupul pentru o țintă nouă și încearcă să pregătească linkul. Mesajul din browser arată dacă editorul este pregătit sau dacă trebuie deschis manual. Verifică previzualizarea și apasă **Post/Publică** în Facebook, apoi înregistrează rezultatul în GroupPilot.
- [x] Pornirile ulterioare ale aplicației sunt redirecționate către fereastra deja deschisă.
- [x] Build x64, 37 de teste trecute și verificare Windows a instanței unice prin două lansări consecutive.
- [ ] Verificare live finală a inserării linkului și a trimiterii manuale într-un grup autorizat.

### 0.00.17

- [x] Formularul de campanie este o pagină cu patru secțiuni numerotate, selecții delimitate, căutare în grupuri, contor de selecție și butoane de salvare vizibile permanent.
- [x] Recurențe disponibile la creare: o singură dată, zilnic, la 2 zile, la 3 zile, săptămânal, lunar, de 2, 3 sau 4 ori pe lună. Ora folosește fusul local, inclusiv schimbarea orei de vară.
- [x] Pentru lunar, se păstrează ziua selectată, cu ultima zi disponibilă în lunile mai scurte. Pentru 2–4 rulări pe lună, datele sunt distribuite uniform în luna calendaristică, la ora aleasă. Prima rulare folosește data selectată.
- [x] După încheierea unei runde, se calculează următoarea dată. **Run campaign now** pornește o nouă rundă când devine scadentă. Rulările ratate sunt sărite, iar rezultatele anterioare rămân în istoric. Țintele în așteptarea aprobării sau eșuate trebuie rezolvate înainte de runda următoare.
- [x] Aplicația afișează notificări pentru campaniile scadente cât timp este deschisă. Distribuirile se confirmă în Facebook.
- [x] Lista campaniilor arată numărul grupurilor și pictograma de informații cu lista grupurilor în tooltip.
- [x] Pauza începe după înregistrarea rezultatului unei distribuiri și se aplică înainte de deschiderea unui grup diferit.

### 0.00.16

- [x] Campaniile au pauză configurabilă între distribuiri, implicit 120 de secunde. Pauza se salvează și se respectă inclusiv după repornirea aplicației și la schimbarea campaniei. O poți modifica la crearea campaniei sau în **Pause between shares → Save pause**.
- [x] **Record and open next** afișează numărătoarea inversă și continuă la expirarea pauzei. **Stop** întrerupe așteptarea.
- [x] În grupul selectat, GroupPilot încearcă să deschidă editorul și să insereze linkul postării. Verifică previzualizarea și confirmă trimiterea în Facebook. Dacă editorul nu este identificat, deschide-l și apasă **Prepare selected share**.
- [ ] Validarea distribuirii într-un grup Facebook de test autorizat.

120 de secunde este o valoare aleasă pentru aplicație. Documentația Meta accesibilă la verificare nu oferă un interval garantat împotriva clasificării ca spam sau a restricțiilor.

### 0.00.15

- [x] Campaniile pot fi șterse din listă după confirmare. Istoricul publicărilor se păstrează.
- [x] Grupurile sunt afișate pe carduri cu imaginea disponibilă. Folosește **Sync groups** pentru a completa imaginile celor importate anterior.
- [x] Selectorul **Member range** generează intervale din numărul de membri al grupurilor salvate. Grupurile fără număr disponibil au opțiunea **Unknown**. Poți sorta crescător sau descrescător.

### 0.00.14

- [x] Selector vizual de postări pentru campanii, cu imagine, titlu, extras și link.
- [x] **Run campaign now** pornește distribuirea asistată imediat. **Save and run now** salvează o campanie nouă și o pornește. După confirmarea în Facebook, **Record and open next** salvează rezultatul și trece la următorul grup eligibil.

### 0.00.13

- [x] Biblioteca și campaniile noi folosesc postări importate din Facebook. Importul individual deschide o previzualizare pentru confirmarea salvării.
- [x] Cardurile au imagini mai mari, afișate integral, și acțiuni pentru deschiderea în Facebook sau ștergere.

### 0.00.12

- [x] **Post Library** afișează carduri cu imaginea disponibilă, un extras din text și acțiuni pentru fiecare postare. Poți reimporta postările Facebook pentru a actualiza miniatura. Când sursa imaginii nu este disponibilă, cardul afișează un substitut.
- [x] **Delete** elimină postarea din bibliotecă după confirmare. Postările asociate unei campanii sunt arhivate local pentru ca acea campanie să le poată folosi în continuare.

### 0.00.11

- [x] **Import posts** detectează și linkuri directe de postare pe pagini publice, inclusiv când Facebook afișează un feed fără carduri marcate explicit.

### 0.00.10

- [x] Bara de titlu afișează automat versiunea buildului, de exemplu `GroupPilot v0.00.10`.

### 0.00.09

- [x] Comenzile de import din browser aleg automat fluxul. Pe lista de postari, deschid selectia de pana la zece postari. Pe linkul unei postari individuale, deschid editorul cu datele acelei postari.

### 0.00.08

- [x] **Facebook Browser → Import posts** preia până la zece postări detectate, în ordinea afișată în fila Posts a unui profil sau a unei pagini Facebook. Verifică selecția și apasă **Save selected**. GroupPilot salvează linkul și textul disponibil în **Post Library**, unde postările pot fi alese pentru campanii.

### 0.00.07

- [x] Browserul Facebook este integrat în fereastra principală și păstrează pagina deschisă când schimbi secțiunea.
- [x] **Open my groups → Sync groups** scanează lista de sus până jos, continuă cât timp apar grupuri și afișează previzualizarea înainte de salvare.
- [x] Importul și sincronizarea sunt disponibile doar pe pagina **Grupurile tale**. Dacă pagina se schimbă sau scanarea stagnează timp de 45 de secunde, sincronizarea se oprește înainte de salvare.

### 0.00.06

- [x] Salvate metadatele vizibile pe cardurile Facebook, inclusiv numărul de membri, vizibilitatea, activitatea, textul cardului și data scanării. Numerele prescurtate sunt aproximative. Câmpurile absente rămân necunoscute.
- [x] Adăugate filtre de vizibilitate și număr minim de membri în Groups.
- [x] Bara Facebook folosește pictograme de navigare, adresa se deschide cu Enter, iar comenzile secundare sunt în meniul cu trei puncte.
- [x] **Groups → Facebook sync** deschide lista Facebook. Derulează până la final, apoi apasă **Sync groups** și verifică lista de ștergeri. Sincronizarea adaugă grupurile noi, actualizează metadatele celor păstrate și șterge grupurile absente din lista încărcată. Istoricul publicărilor se păstrează, iar țintele care referă grupuri șterse devin indisponibile.

### 0.00.05

- [x] Import local al grupurilor încărcate în fereastra Facebook. Autentifică-te în WebView2, apasă **Open my groups**, derulează pentru a încărca lista, apoi apasă **Import loaded groups**. Verifică selecția înainte de salvare. Poți repeta importul după ce apar alte grupuri.

### 0.00.04

- [x] Fereastră Facebook dedicată, profil WebView2 persistent și opțiune de deschidere în browserul implicit.
- [x] Import de permalink, titlu și descriere disponibilă, cu verificarea paginii înainte de transfer.
- [x] Verificare manuală a regulilor și accesului pentru fiecare grup, cu dată de verificare în SQLite.
- [x] Recuperare după întrerupere, stare de moderare și înregistrarea rezultatului din contextul țintei.

### 0.00.03

- [x] Repoziționat conținutul paginilor de lucru lângă navigație, cu spațiere și ierarhie vizuală comune.
- [x] Reorganizat Dashboardul, câmpurile și acțiunile în secțiuni cu coloane adaptive. Browserul intern își păstrează layoutul.
- [x] Aplicat iconul F în bara de titlu, executabil, resursele MSIX, ecranul de pornire și notificarea campaniei.

### 0.00.02

- [x] Adăugate fundația WinUI, persistența SQLite, biblioteca și editorul de postări, gestionarea grupurilor, campaniile și browserul pentru publicare asistată.
- [x] Adăugate testele de validare și persistență, cu detalii în [jurnalul de progres](docs/PROGRESS.md).
- [x] Build x64 și lansare cu identitate de pachet. Inițializarea Dashboardului a fost confirmată în log.

### 0.00.01

- [x] Documentate fundația WinUI 3 inițială, pașii de build și lansare și etapele MVP.
- [x] Stabilită versiunea de bază `0.00.01`.

La fiecare modificare, incrementează ultima componentă a versiunii, sincronizează metadatele .NET și MSIX, adaugă aici ce s-a schimbat și actualizează bifele unde este cazul. Versiunea `0.00.19` corespunde metadatelor .NET `0.0.19` și MSIX `0.0.19.0`. Validează modificarea, apoi fă commit și push pe ramura proiectului.
