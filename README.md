# GroupPilot

**Versiune curentă** `0.00.09`

GroupPilot este o aplicație desktop nativă pentru Windows 11. Pregătești postări, organizezi grupuri Facebook și urmărești local campaniile de distribuire. Trimiterea finală se face manual în interfața Facebook, pentru fiecare grup.

## Dezvoltare locală

Folosește Windows 11, .NET SDK 10, Windows App Runtime și WebView2 Runtime. Proiectul WinUI țintește `net10.0-windows10.0.26100.0`. Activează **Developer Mode** pentru lansarea cu identitate de pachet. Poți deschide pagina din PowerShell cu `Start-Process "ms-settings:developers"`.

Din rădăcina repository-ului:

```powershell
dotnet restore GroupPilot.slnx
dotnet build GroupPilot.slnx
dotnet test tests/GroupPilot.Tests
dotnet run --project src/GroupPilot.App
```

Soluția include `GroupPilot.App` pentru UI-ul WinUI 3, `GroupPilot.Core` pentru modele și validare, `GroupPilot.Infrastructure` pentru persistența EF Core/SQLite și `GroupPilot.Tests` pentru testele xUnit. Aplicația aplică migrațiile la pornire. Directorul local `GroupPilot`, afișat în Settings, conține baza SQLite, imaginile copiate din drafturi, profilul WebView2 și logurile Serilog. Windows poate redirecționa acest director în `LocalCache\Local` pentru aplicația lansată cu identitate MSIX.

## Implementat

- [x] Fereastră WinUI 3 cu Mica, bară nativă, navigare adaptivă și teme System, Light și Dark.
- [x] Dashboard, Post Library, Post Composer, Groups, Campaigns, Facebook Browser, Publishing History și Settings.
- [x] Soluție App/Core/Infrastructure/Tests, MVVM Toolkit, dependency injection, migrare SQLite inițială, loguri locale și teste xUnit.
- [x] Bibliotecă de drafturi cu trei fluxuri. Salvează linkul unei postări Facebook și titlul disponibil în browser, compune o postare nouă cu text, link și imagini sau duplică și editează un draft ca remix. Editorul validează și previzualizează conținutul înainte de salvare.
- [x] Grupuri cu editare, ștergere, căutare, categorii, note, reguli și stare activă. Importă CSV cu antetul `name,url` sau câte un URL pe rând. URL-urile echivalente sunt deduplicate.
- [x] Campanii cu postare salvată, selecție de grupuri active, dată și oră de planificare, stare per grup și reluarea țintelor în așteptare sau eșuate.
- [x] WebView2 cu profil persistent separat și navigare către postări și grupuri Facebook. Autentificarea are loc în interfața Facebook.
- [x] Publicare asistată. Campania copiază textul sau linkul, afișează imaginile de atașat și deschide grupul. După trimiterea manuală în Facebook, utilizatorul înregistrează rezultatul și URL-ul publicat. Data planificată rămâne informație pentru utilizator.
- [x] Notificare Windows pentru închiderea tuturor țintelor campaniei, unde API-ul este disponibil.

- [x] Pagini de lucru aliniate la stânga, formulare grupate și coloane care se rearanjează la redimensionare. Identitatea vizuală folosește iconul F în fereastră și în resursele Windows.

## Flux Facebook

1. Deschide **Facebook Browser** din meniul aplicației. Alege **Continue with this session** dacă ești deja autentificat în profilul GroupPilot sau autentifică-te direct în Facebook. **Open in default browser** folosește sesiunea browserului Windows, dacă există. Pentru a importa de acolo, deschide linkul postării în fereastra GroupPilot.
2. Deschide permalinkul unei postări și apasă **Import current post**. Verifică URL-ul, titlul și descrierea disponibilă, apoi salvează draftul.
3. În **Groups**, selectează un grup și apasă **Review in Facebook**. Verifică regulile și dreptul contului de a posta, apoi folosește **Record group review** pentru a salva observațiile și data verificării. Accesul poate varia în funcție de cont și în timp.
4. Din campanie, alege **Prepare selected target** sau **Resume next target**. GroupPilot copiază textul ori linkul și salvează ținta ca fiind în lucru. Lipește conținutul, atașează imaginile și confirmă manual trimiterea în Facebook.
5. Folosește **Record campaign outcome** în fereastra Facebook sau **Record selected outcome** în campanie. Selectează rezultatul observat. Pentru Published, confirmă URL-ul postării. Pentru AwaitingApproval, verifică ulterior moderarea înainte de a înregistra rezultatul final.
6. După o întrerupere, redeschide campania. Țintele rămase în lucru au prioritate la reluare. Verifică întâi dacă postarea a fost deja trimisă, apoi înregistrează rezultatul sau continuă publicarea.

## Ce urmează

- [ ] Verifică interactiv cele opt pagini, redimensionarea, temele și dialogurile native pe Windows 11.
- [ ] Verifică autentificarea manuală în WebView2, persistența sesiunii și fluxul complet de publicare cu un grup de test autorizat.
- [ ] Verifică afișarea notificării Windows și lansarea pe x86 și ARM64.

## Istoricul versiunilor

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

La fiecare modificare, incrementează ultima componentă a versiunii, sincronizează metadatele .NET și MSIX, adaugă aici ce s-a schimbat și actualizează bifele unde este cazul. Versiunea `0.00.09` corespunde metadatelor .NET `0.0.9` și MSIX `0.0.9.0`. Validează modificarea, apoi fă commit și push pe ramura proiectului.
