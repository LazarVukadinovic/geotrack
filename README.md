# GeoTrack

GeoTrack je web aplikacija za istraživanje destinacija, mesta, smeštaja i događaja. Korisnička aplikacija je Angular frontend koji pokriva Explore, Map, listing stranice, detalje mesta/smeštaja/događaja, pretragu, profil, omiljene stavke, kolekcije, interesovanja, preporuke i korisničke doprinose.

Backend je ASP.NET Core API koji čuva katalog, korisnike, recenzije, kolekcije, doprinose, notifikacije, analitiku, mapne pretrage i AI/MCP integracije. Frontend ne koristi direktne pozive ka eksternim mapnim i AI provider servisima za glavne tokove: ti pozivi idu kroz backend.

Ask Agent je backend-managed AI funkcionalnost za pitanja nad GeoTrack katalogom. Angular ne prikazuje provider/model/key opcije i ne šalje korisničke API ključeve; chat se izvršava preko server-side Gemini key pool-a. GeoTrack MCP je zaseban remote HTTP JSON-RPC tool endpoint za spoljne AI alate koji podržavaju MCP.

## Bezbedno inicijalno podešavanje administratora

Privilegovani nalozi nemaju podrazumevanu lozinku i ne kreiraju se automatski ni u
Development ni u Production okruženju. Za početnog administratora uključite oba
navedena prekidača i dostavite vrednosti iz zaštićenog deployment okruženja ili
postojećeg sistema za tajne:

```text
Seed__EnableOperationalSeed=true
Seed__OperationalUsers__Admin__Enabled=true
Seed__OperationalUsers__Admin__Email=<adresa-vlasnika-naloga>
Seed__OperationalUsers__Admin__Username=<jedinstveno-korisnicko-ime>
Seed__OperationalUsers__Admin__Password=<jedinstvena-nasumicna-lozinka>
```

Oznake u uglastim zagradama nisu stvarne vrednosti. Lozinka mora da ima 16–256
znakova, najmanje 10 različitih znakova, veliko i malo slovo, cifru i specijalni
znak. Ne sme da sadrži korisničko ime, celu email adresu, kontrolne znakove ili
`<` / `>`. Generišite je u menadžeru lozinki i čuvajte je odvojeno za svaki nalog.
Aplikacija ne skraćuje lozinku niti uklanja razmake. Ne unosite tajne u Git,
komandnu istoriju, CI izlaz ili deljene konfiguracione fajlove.

Za lokalni razvoj koristite .NET User Secrets za `Geotrack.Server`, sa istim
ključevima i `:` umesto `__`, ili promenljive okruženja procesa. User Secrets
služe za lokalni razvoj; produkcioni deployment mora bezbedno da dostavi tajne.
Backend ne učitava `.env` niti `appsettings.*.local.json` automatski.

Provera svih uključenih naloga prethodi migracijama i unosu korisnika. Prazna ili
neispravna konfiguracija prekida pokretanje bez rezervne lozinke. Novi nalog dobija
postojeći ASP.NET Identity password hash i odgovarajuću ulogu; vlasništvo nad
adresom mora da proveri operater jer inicijalni administratorski nalog ima potvrđenu
email adresu. Postojeći nalog sa istom adresom i očekivanom ulogom ostaje neizmenjen:
lozinka, profil, potvrda adrese, suspenzija, brisanje i dodeljeni regioni se ne
obnavljaju. Konflikt sa običnim nalogom, duplikat adrese ili zauzeto korisničko ime
prekida inicijalizaciju. Ponovno pokretanje ne menja postojeću lozinku.

Posle uspešnog prvog prijavljivanja postavite `Seed__EnableOperationalSeed=false`
i uklonite početnu lozinku iz deployment konfiguracije. Bez ovog koraka izričito
uključena inicijalizacija može ponovo da napravi fizički obrisan nalog. Oporavak
pristupa i promenu lozinke radite kroz odobrene administratorske tokove.

Po potrebi se na isti način mogu izričito uključiti odeljci `ContentModerator`,
`CroatiaTerritoryManager`, `CroatiaRegionalManager`, `SerbiaTerritoryManager` i
`SerbiaRegionalManager`. Svaki zahteva sopstvene `Enabled`, `Email`, `Username` i
`Password` vrednosti; adrese i korisnička imena moraju biti različiti. Novi menadžeri
dobijaju postojeće HRV/SRB teritorijalne ili regionalne dodele. Uobičajeno je dovoljno
inicijalizovati jednog administratora i ostale naloge voditi kroz administraciju.
Stari `Seed:Demo*` podaci nisu izvor produkcionih kredencijala.

Javni seed kreira katalog bez korisnika. Demo sadržaj sa običnim probnim korisnicima
ograničen je na Development; `Seed:RunInProduction` ne može da omogući demo korisnike
u produkciji. Koristite odvojenu lokalnu bazu, nikada produkcionu bazu sa Development
okruženjem. Privilegovane test fixture lozinke postoje samo u test projektu.

Ranije objavljene lozinke nisu promenjene ovim izmenama. Ovlašćeni operater mora da
proveri postojeće privilegovane naloge, rotira izložene kredencijale baze i JWT
ključeve i zasebno razreši izlaganje u Git istoriji. Rotacija JWT ključa poništava
stare access tokene, ali sama po sebi ne opoziva refresh tokene.

## Lokalno pokretanje ukratko

Za lokalni rad obično treba:

- Node.js kompatibilan sa Angular CLI (`^20.19.0`, `^22.12.0` ili `>=24.0.0`) i npm 11.9.0;
- .NET SDK koji podržava `net8.0` i .NET 8 / ASP.NET Core 8 runtime;
- PostgreSQL sa ekstenzijama `postgis`, `pg_trgm` i `unaccent`;
- backend na `http://localhost:5003`;
- `user-app` na `http://localhost:4200`;
- `admin-app` na `http://localhost:4300`;
- lokalni geoBoundaries fajlovi ako testirate teritorijalne i regionalne granice.

U Development modu backend automatski primenjuje migracije i pokreće demo sadržaj. Privilegovani bootstrap je isključen dok se izričito ne konfiguriše.

## Lokalni boundary / GeoJSON fajlovi

Granice se lokalno učitavaju iz geoBoundaries CGAZ fajlova preuzetih sa:

https://www.geoboundaries.org/globalDownloads.html

Ovi fajlovi su potrebni za import granica država/teritorija (`ADM0`) i administrativnih regiona (`ADM1`). Trenutni backend podržava i `ADM2` kandidate, ali trenutni region sync koristi `ADM1`.

Podrazumevana putanja iz backend konfiguracije je:

- iz root-a repozitorijuma: `src/server/Geotrack/Geotrack.Server/Data/Boundaries`
- relativno od `Geotrack.Server` projekta: `Data/Boundaries`

Očekivani fajlovi:

- `geoBoundariesCGAZ_ADM0.geojson`
- `geoBoundariesCGAZ_ADM1.geojson`
- `geoBoundariesCGAZ_ADM2.geojson` ako koristite ADM2 import

Direktorijum `src/server/Geotrack/Geotrack.Server/Data/Boundaries` ima lokalni `.gitignore` koji ignoriše sve osim samog `.gitignore` fajla. GeoJSON fajlovi su veliki i namerno se ne commit-uju u Git.

Attribution/licenca za boundary podatke: geoBoundaries / gbOpen / CC BY 4.0.

## Import boundary podataka

Ako u bazi fale boundary podaci, prvo pokrenite backend na `http://localhost:5003`, zatim se prijavite kao Administrator i uzmite JWT/access token iz aplikacije/dev tools-a ili iz trenutnog admin auth toka (`POST /api/admin/auth/login`). U komandama koristite placeholder, ne realan token:

`<ADMIN_BEARER_TOKEN>`

Import `ADM0` granica država/teritorija:

```powershell
Invoke-RestMethod `
  -Method POST `
  -Uri "http://localhost:5003/api/admin/territories/boundaries/import-local-adm0?force=false" `
  -Headers @{ Authorization = "Bearer <ADMIN_BEARER_TOKEN>" } `
  -TimeoutSec 180
```

Import `ADM1` administrativnih granica:

```powershell
Invoke-RestMethod `
  -Method POST `
  -Uri "http://localhost:5003/api/admin/territories/boundaries/import-local-adm1?force=false" `
  -Headers @{ Authorization = "Bearer <ADMIN_BEARER_TOKEN>" } `
  -TimeoutSec 1800
```

Za filtrirani ili batch import `ADM1` podataka možete koristiti query parametre koje backend podržava:

```powershell
Invoke-RestMethod `
  -Method POST `
  -Uri "http://localhost:5003/api/admin/territories/boundaries/import-local-adm1?force=false&countryIso3=HRV&take=500" `
  -Headers @{ Authorization = "Bearer <ADMIN_BEARER_TOKEN>" } `
  -TimeoutSec 1800
```

Preporuka:

- `force=false` koristite za normalan import/resume.
- `force=true` koristite samo kada namerno osvežavate ili rebuild-ujete postojeće boundary podatke.
- `ADM1` import traje duže jer obrađuje mnogo više feature-a od `ADM0`.
- Na serverima sa malo RAM-a koristite filtriranje/batch import preko `countryIso3` i `take`.

## ADM1 sync regiona

Tok za region management je trenutno:

- `ADM0` import kreira ili ažurira granice država/teritorija.
- `ADM1` import čuva administrativne granice u `AdministrativeBoundaryCandidates`.
- Territory Manager sync inicijalizuje stvarne app `Regions` iz `ADM1` kandidata.
- Territory Manager zatim dodeljuje Regional Manager-e postojećim regionima.
- Territory Manager u normalnom toku ne treba ručno da kreira regione jedan po jedan.

Trenutno ponašanje UI-ja:

- prvi setup za Territory Manager-a prikazuje modal sa dugmetom `Get Started`;
- `Regional Management` prikazuje `Update Regions` kada se `ADM1` podaci promene;
- `Region Map` prikazuje sinhronizovane regione za aktivnu teritoriju.

## Tehnologije

**Frontend**

- Angular 21.x
- TypeScript 5.9
- SCSS, uz Tailwind v4 gde je već uveden
- MapLibre GL za mapu i map picker tokove
- RxJS

**Backend**

- .NET 8 / ASP.NET Core Web API
- EF Core 8
- PostgreSQL preko Npgsql
- PostGIS i NetTopologySuite za prostorne upite
- JWT bearer autentikacija i HttpOnly refresh cookie
- Gemini AI provider adapter sa backend-managed pool-om API ključeva i fallback modelima
- Remote HTTP JSON-RPC MCP endpoint na `/api/mcp`

## Struktura projekta

```text
geotrack/
├── scripts/                       # održavanje seed slika, npm manifest i lockfile
├── src/
│   ├── admin-app/                  # Angular administracija
│   │   ├── public/                 # statički resursi
│   │   ├── src/app/                # core, features, shared; testovi uz izvorni kod
│   │   └── server.js               # Express server za produkcioni build
│   ├── user-app/                   # Angular korisnička aplikacija
│   │   ├── public/
│   │   ├── src/app/                # core, features, shared; testovi uz izvorni kod
│   │   └── server.js
│   └── server/Geotrack/
│       ├── Geotrack.Domain/        # entiteti i enum-i
│       ├── Geotrack.Application/   # DTO-ovi, interfejsi i opcije
│       ├── Geotrack.Infrastructure/ # EF Core, migracije, repozitorijumi i servisi
│       ├── Geotrack.Server/        # API, konfiguracija, Data/ i media/
│       ├── Geotrack.IntegrationTests/
│       └── Geotrack.sln
├── .gitignore
└── README.md
```

Angular konfiguracija, manifesti i lockfile-ovi ostaju uz svoje aplikacije.
.NET projekti i integracioni testovi ostaju u zajedničkom solution-u.
Repozitorijum trenutno nema Docker konfiguraciju ni CI/CD workflow.

## Instalacija i lokalno pokretanje

Sve naredne komande koriste **koren repozitorijuma** kao radni direktorijum,
osim gde je drugačije navedeno. Nije potrebna globalna instalacija Angular CLI-ja.
U Windows PowerShell-u možete koristiti `npm.cmd` umesto `npm` ako je potrebno.

```bash
npm --prefix src/user-app ci
npm --prefix src/admin-app ci
dotnet restore src/server/Geotrack/Geotrack.sln
```

Lokalne tajne podesite za postojeći `Geotrack.Server` UserSecretsId:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=geotrack;Username=<korisnik>;Password=<lozinka>" --project src/server/Geotrack/Geotrack.Server
dotnet user-secrets set "Jwt:Key" "<random-string-od-najmanje-32-karaktera>" --project src/server/Geotrack/Geotrack.Server
```

`Jwt:Issuer` i `Jwt:Audience` imaju netajne podrazumevane vrednosti u `appsettings.json`.
`Jwt__Key` mora biti zasebna, nasumično generisana tajna (preporučeno najmanje 32
nasumična bajta kodirana kao Base64); proverava se minimum od 32 UTF-8 bajta i
odbijaju prazne, jednolične vrednosti i placeholder-i. Baza se konfiguriše preko
`ConnectionStrings__DefaultConnection`, bez podrazumevane lozinke.
U odvojenim terminalima pokrenite:

```bash
dotnet run --project src/server/Geotrack/Geotrack.Server --launch-profile http
npm --prefix src/user-app start
npm --prefix src/admin-app start
```

Backend sluša na `http://localhost:5003`, user-app na `http://localhost:4200`,
a admin-app na `http://localhost:4300`. Oba `proxy.conf.json` fajla prosleđuju
`/api` i `/media` ka backend-u; Development CORS dozvoljava oba frontend origin-a.
Development startup automatski primenjuje migracije i konfigurisan seed.

Za ručne migracije, uz instaliran `dotnet-ef` alat kompatibilan sa EF Core 8,
iz korena repozitorijuma koristite:

```bash
dotnet ef database update --project src/server/Geotrack/Geotrack.Infrastructure --startup-project src/server/Geotrack/Geotrack.Server
```

## Konfiguracija baze

GeoTrack koristi PostgreSQL sa PostGIS ekstenzijom. `AppDbContext` registruje `postgis`, koristi `UseNetTopologySuite()` i ima `geography(point)` kolone i GIST indekse za destinacije, lokacije, mesta i događaje.

Connection string se podešava kroz user-secrets ili environment variable; praćeni `appsettings*.json` fajlovi moraju ostati bez tajni. Ne stavljajte lozinke u git. Design-time `AppDbContextFactory` može da pročita `ConnectionStrings__DefaultConnection` iz environment-a ili user-secrets vrednost iz `Geotrack.Server` projekta.

U Development modu server pokušava:

```sql
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS unaccent;
```

Ako korisnik baze nema dozvolu za to, ekstenziju napravite ručno nad bazom pre pokretanja aplikacije.

## Provera i produkcioni build

```bash
npm --prefix src/user-app run test:ci
npm --prefix src/admin-app run test:ci
dotnet test src/server/Geotrack/Geotrack.sln
npm --prefix src/user-app run build:production
npm --prefix src/admin-app run build:production
dotnet publish src/server/Geotrack/Geotrack.Server -c Release -o artifacts/server
```

Frontend testovi koriste Vitest. Backend testovi koriste xUnit i kreiraju pa brišu
privremene PostgreSQL baze; lokalna konekcija se čita iz user-secrets ili
`ConnectionStrings__DefaultConnection`. Test infrastruktura prihvata samo lokalni PostgreSQL (`localhost`, `127.0.0.1` ili `::1`). Test korisnik baze mora da ima dozvolu za
kreiranje baza i uključivanje `postgis`, `pg_trgm` i `unaccent` ekstenzija.
Nisu konfigurisane posebne lint, format-check ili e2e komande. Angular build
proverava TypeScript i Angular šablone; `dotnet build` proverava C# projekte.

## Produkciono pokretanje

Frontend build-ovi su u `src/user-app/dist/user-app/browser` i
`src/admin-app/dist/admin-app/browser`. Postojeći Express serveri se pokreću sa:

```bash
npm --prefix src/user-app run serve:static
npm --prefix src/admin-app run serve:static
```

Za produkciju podesite `NODE_ENV=production`, `HTTPS_PFX_PATH` i po potrebi
`HTTPS_PFX_PASSWORD`. Podrazumevani portovi su `10131` i `10132`; `PORT` ih menja.
`BACKEND_ORIGIN` je obavezan u produkciji i određuje odredište `/media` preusmeravanja.
Za lokalni rad podrazumevana backend adresa je `http://localhost:5003`. Express ne prosleđuje
`/api`: frontend produkcioni URL-ovi su u
`src/app/environments/environment.production.ts` svake aplikacije i izmene tih
URL-ova zahtevaju novi build. Praćene konfiguracije sadrže samo primere sa domenom
`example.invalid`; stvarne adrese postavite u privatnom deployment/build koraku,
bez njihovog commit-ovanja.

Backend produkcione adrese podesite kroz promenljive okruženja:

```text
Cors__AllowedOrigins__0=<origin-korisnicke-aplikacije>
Cors__AllowedOrigins__1=<origin-administrativne-aplikacije>
AllowedHosts=<hostname-backenda>
Mcp__PublicBaseUrl=<javni-URL-backenda>
Email__FrontendBaseUrl=<javni-URL-korisnicke-aplikacije>
```

Origin obuhvata protokol, hostname i port ako se koristi nestandardni port, bez
putanje i završne kose crte. `AllowedHosts` sadrži samo hostname, bez protokola i
porta; za više hostname-ova koristite `;`. Produkcija nema podrazumevane spoljne
CORS adrese. `Mcp__PublicBaseUrl` podesite kada uključujete MCP, a
`Email__FrontendBaseUrl` kada uključujete slanje emaila.

Backend pokrenite iz direktorijuma `artifacts/server` komandom
`dotnet Geotrack.Server.dll`, uz `ASPNETCORE_ENVIRONMENT=Production`, connection
string, JWT ključ i HTTPS/Kestrel ili reverse-proxy podešavanja okruženja.
Sačuvajte `media/seed`, `Data/Agent/agent-taxonomy.json`, potrebne boundary fajlove,
trajni `media/uploads` i Data Protection ključeve pri deployment-u.
`appsettings.Production.json` uključuje automatske migracije i public-content seed
bez kreiranja korisnika. Privilegovani bootstrap je podrazumevano isključen.
Pregledajte konfiguraciju pre pokretanja nad postojećom bazom.
`/health/live` proverava proces, a `/health/ready` dostupnost baze.

## Email konfiguracija

Backend koristi eksterni SMTP provider za potvrdu email adrese i reset lozinke.
Podesite sledeća imena preko environment-a ili odgovarajućih user-secrets ključeva:

```text
Email__Enabled=true
Email__Host=smtp.example.com
Email__Port=587
Email__UseSsl=true
Email__UserName=<smtp-korisnik>
Email__Password=<smtp-lozinka>
Email__FromEmail=no-reply@example.com
Email__FromName=GeoTrack
Email__FrontendBaseUrl=<javni-URL-korisnicke-aplikacije>
```

Kada je `Email:Enabled=false` (podrazumevano), poruke se ne šalju. Log sadrži samo
režim isporuke i status `Suppressed`; adrese, naslovi, sadržaj i autentikacioni linkovi
se ne beleže. Za registraciju, potvrdu email adrese i reset lozinke stvarnih korisnika
podesite i uključite SMTP. Za lokalno pregledanje poruka koristite izdvojeni lokalni
SMTP prijemnik sa probnim nalozima. Ne preusmeravajte sadržaj poruka u logove.

## Održavanje seed slika

`scripts/convert-images.mjs` pretvara lokalne originale iz
`src/server/Geotrack/Geotrack.Server/media/source` u WebP fajlove u susednom
`media/seed` direktorijumu (maksimalna širina 1400 px, kvalitet 82):

```bash
npm --prefix scripts ci
npm --prefix scripts run convert
```

Skripta rešava putanje u odnosu na sopstvenu lokaciju, nezavisno od radnog
direktorijuma. Originali su ignorisani; potrebne seed slike se commit-uju i
referenciraju kroz `SeedWebp("naziv-fajla")` u `SeedData.cs`.

## Ask Agent

Ask Agent je backend-driven AI funkcionalnost. Angular ne zove Gemini, OpenAI, Claude niti druge model providere direktno. Frontend učitava opcije preko:

- `GET /api/agent/providers`

Glavni chat ide preko:

- `POST /api/agent/chat`

Legacy endpoint `POST /api/agent/message` više nije dostupan. Aktuelni tok koristi `/api/agent/chat`.

Gemini je jedini provider za user-app Ask Agent tok. Model i API ključevi se podešavaju isključivo na backend-u. Frontend ne bira provider/model, ne čuva API key u browser storage-u i ne šalje privremene ili sačuvane korisničke ključeve. Ako jedan Gemini key dostigne kvotu ili rate-limit, backend privremeno hladi taj key i pokušava sledeći konfigurisani key.

## Konfiguracija Gemini API ključeva za Ask Agent

Za lokalno testiranje možete napraviti Gemini API key u Google AI Studio-u. Nemojte ga stavljati u Angular, source kod, `environment` fajlove frontenda ili commit-ovane konfiguracije.

Podešavanje preko user-secrets:

```bash
dotnet user-secrets set "Gemini:ApiKeys:0" "<GEMINI_API_KEY_1>" --project src/server/Geotrack/Geotrack.Server
dotnet user-secrets set "Gemini:ApiKeys:1" "<GEMINI_API_KEY_2>" --project src/server/Geotrack/Geotrack.Server
dotnet user-secrets set "Gemini:Model" "gemini-3.1-flash-lite" --project src/server/Geotrack/Geotrack.Server
dotnet user-secrets set "Gemini:FallbackModels:0" "gemini-3.5-flash" --project src/server/Geotrack/Geotrack.Server
dotnet user-secrets set "Gemini:KeyCooldownSeconds" "300" --project src/server/Geotrack/Geotrack.Server
```

Ista podešavanja kao environment variable imena:

```text
Gemini__ApiKeys__0=<GEMINI_API_KEY_1>
Gemini__ApiKeys__1=<GEMINI_API_KEY_2>
Gemini__Model=gemini-3.1-flash-lite
Gemini__FallbackModels__0=gemini-3.5-flash
Gemini__KeyCooldownSeconds=300
```

Podrazumevani model iz konfiguracije je `gemini-3.1-flash-lite`, sa `gemini-3.5-flash` kao fallback modelom. Ako se `Gemini:Model` ne podesi u konfiguraciji, aplikativni default u `GeminiOptions` je `gemini-3.5-flash`. Free quota i uslovi korišćenja zavise od Google AI Studio/Gemini pravila. Za produkciju moraju da se kontrolišu kvote, rate-limit-i i monitoring backend key pool-a.

## MCP konektor

GeoTrack MCP je remote HTTP JSON-RPC endpoint:

- `GET /api/mcp` vraća bezbedan status.
- `POST /api/mcp` obrađuje MCP JSON-RPC zahteve.

Ovo je tool server, ne chatbot. Spoljni MCP hostovi kao Codex, Claude ili ChatGPT-compatible klijenti odlučuju kada pozivaju GeoTrack alate. Trenutni MCP alati su public, read-only i ograničeni; ne izlažu privatne korisničke podatke, write operacije, admin operacije, moderation operacije, SQL, fajlove, tokene ili provider API ključeve.

Trenutni allowlist:

- `geotrack_search_places`
- `geotrack_get_place_details`
- `geotrack_search_stays`
- `geotrack_get_stay_details`
- `geotrack_search_events`
- `geotrack_get_event_details`
- `geotrack_resolve_area`
- `geotrack_search_catalog`
- `geotrack_search_by_area`
- `geotrack_get_area_overview`
- `geotrack_get_category_options`
- `geotrack_find_nearby_public_content`
- `geotrack_get_map_filters`

Lokalno povezivanje u Codex aplikaciji:

- prvo pokrenite backend;
- koristite backend port, ne Angular port, osim ako ste posebno proverili proxy;
- MCP URL: `http://localhost:5003/api/mcp`
- Transport: `Streamable HTTP`
- Bearer token: prazno za trenutni public read-only MVP
- Headers: prazno

Primeri zahteva koje možete dati Codex-u:

```text
Use GeoTrack MCP to search places in Dubrovnik.
Call geotrack_search_places with location Dubrovnik and query places.
Call geotrack_search_stays with location Dubrovnik and query hotels.
```

Za javni ChatGPT/Claude connector potreban je javni HTTPS URL, ne `localhost`. Private MCP tools nisu implementirani dok se ne završi auth dizajn i review.

## Bezbednosne napomene

- Ne commitovati API ključeve, lozinke, connection string lozinke ili provider tokene.
- Gemini API key ne sme da ide u frontend kod, browser storage, network payload iz user-app-a ili logove.
- Precizna GPS lokacija ne sme da se čuva u `localStorage`; trenutni kod koristi `sessionStorage` za `geotrack_location` i briše staru `localStorage` kopiju.
- MCP trenutno ne izlaže write, admin, moderation ili private user tools.
- Backend endpoint-i treba da zadrže postojeću validaciju i rate-limit politike.

## Poznata ograničenja i budući rad

- MCP private user tools nisu dostupni dok se ne uradi auth dizajn i security review.
- OpenAI i Claude provider adapteri nisu aktivni; trenutno su nedostupni scaffolding provideri.
- Produkcioni deployment treba da podesi trajno čuvanje Data Protection ključeva pre oslanjanja na sačuvane korisničke AI ključeve.
- Javni MCP connector zahteva HTTPS deployment.
- Angular build ima stroge bundle/style budget-e u `user-app/angular.json`; veće SCSS izmene mogu da izazovu budget upozorenja.
