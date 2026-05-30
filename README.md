# GeoTrack

GeoTrack je web aplikacija za istraživanje destinacija, mesta, smeštaja i događaja. Korisnička aplikacija je Angular frontend koji pokriva Explore, Map, listing stranice, detalje mesta/smeštaja/događaja, pretragu, profil, omiljene stavke, kolekcije, interesovanja, preporuke i korisničke doprinose.

Backend je ASP.NET Core API koji čuva katalog, korisnike, recenzije, kolekcije, doprinose, notifikacije, analitiku, mapne pretrage i AI/MCP integracije. Frontend ne koristi direktne pozive ka eksternim mapnim i AI provider servisima za glavne tokove: ti pozivi idu kroz backend.

Ask Agent je backend-managed AI funkcionalnost za pitanja nad GeoTrack katalogom. Angular ne prikazuje provider/model/key opcije i ne šalje korisničke API ključeve; chat se izvršava preko server-side Gemini key pool-a. GeoTrack MCP je zaseban remote HTTP JSON-RPC tool endpoint za spoljne AI alate koji podržavaju MCP.

## Korisnici za testiranje
- Korisnička aplikacija: `https://softeng.pmf.kg.ac.rs:10131`
- Administrativna aplikacija: `https://softeng.pmf.kg.ac.rs:10132`
- Backend: `https://softeng.pmf.kg.ac.rs:10133`

## Korisnici za testiranje

Svi korisnici koriste istu lozinku:

`Proba123!`

### Administrator

- Email: `zika@gmail.com`

### Hrvatska / HRV

- Teritorijalni menadžer: `mika@gmail.com`
- Regionalni menadžer za Dubrovnik-Neretva: `pera@gmail.com`

### Srbija / SRB

- Teritorijalni menadžer: `nikola@gmail.com`
- Regionalni menadžer za Šumadiju (`Sumadija District` u seed podacima): `serbia.regional@gmail.com`

### Content Moderator

- Email: `moderator@gmail.com`

## Lokalno pokretanje ukratko

Za lokalni rad obično treba:

- PostgreSQL baza sa PostGIS ekstenzijom;
- backend na `http://localhost:5003`;
- `user-app` na `http://localhost:4200`;
- `admin-app` na `http://localhost:4300`;
- lokalni geoBoundaries fajlovi ako testirate teritorijalne i regionalne granice.

U Development modu backend automatski primenjuje migracije i pokreće operational/demo seed ako je konfiguracija ostavljena kao u `appsettings.Development.json`.

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
- Gemini AI provider adapter sa backend-managed pool-om API ključeva
- Remote HTTP JSON-RPC MCP endpoint na `/api/mcp`

## Struktura projekta

- `user-app` - Angular korisnički frontend.
- `admin-app` - Angular Regional Manager/admin površina.
- `server/Geotrack` - .NET solution.
  - `Geotrack.Domain` - entiteti i enum-i.
  - `Geotrack.Application` - DTO-ovi, interfejsi servisa i opcije.
  - `Geotrack.Infrastructure` - EF Core `AppDbContext`, repozitorijumi, servisi, migracije i Agent/MCP alatni sloj.
  - `Geotrack.Server` - API kontroleri, DI/startup, middleware i statički `/media` fajlovi.
  - `Geotrack.IntegrationTests` - integracioni testovi.

## Pokretanje frontend-a lokalno

Komande se pokreću iz ovog `src` foldera:

```bash
cd user-app
npm install
npm run start
```

Angular dev server za korisničku aplikaciju je podrazumevano na `http://localhost:4200/`. `user-app/proxy.conf.json` prosleđuje `/api` i `/media` ka `http://localhost:5003`, pa lokalni frontend očekuje da backend radi na tom portu. U Windows PowerShell-u možete koristiti `npm.cmd` umesto `npm` ako je potrebno.

Admin aplikacija koristi isti backend i lokalno se obično pokreće na `http://localhost:4300/`:

```bash
cd admin-app
npm install
npm run start -- --port 4300
```

`admin-app/proxy.conf.json` takođe prosleđuje `/api` i `/media` ka `http://localhost:5003`. Backend Development CORS politika dozvoljava lokalne origin-e za korisničku aplikaciju na portu `4200` i admin aplikaciju na portu `4300`.

Build frontend-a:

```bash
cd user-app
npm run build
```

## Pokretanje bekenda

Preduslovi:

- .NET 8 SDK
- PostgreSQL baza
- instaliran PostGIS za PostgreSQL server

Osnovno podešavanje lokalnih tajni:

```bash
dotnet user-secrets init --project server/Geotrack/Geotrack.Server
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=geotrack;Username=<korisnik>;Password=<lozinka>" --project server/Geotrack/Geotrack.Server
dotnet user-secrets set "Jwt:Key" "<random-string-od-najmanje-32-karaktera>" --project server/Geotrack/Geotrack.Server
```

`Jwt:Issuer` i `Jwt:Audience` imaju netajne podrazumevane vrednosti u `appsettings.json`.

Restore, build i pokretanje:

```bash
dotnet restore server/Geotrack/Geotrack.sln
dotnet build server/Geotrack/Geotrack.sln
dotnet run --project server/Geotrack/Geotrack.Server
```

Backend u razvojnom profilu sluša na `http://localhost:5003`. U Development modu pri startu proverava PostGIS, automatski primenjuje EF migracije i pokreće seed podataka.

Ako migracije želite da primenite ručno preko `dotnet-ef`, iz `src` foldera koristite:

```bash
dotnet ef database update --project server/Geotrack/Geotrack.Infrastructure --startup-project server/Geotrack/Geotrack.Server
```

## Konfiguracija baze

GeoTrack koristi PostgreSQL sa PostGIS ekstenzijom. `AppDbContext` registruje `postgis`, koristi `UseNetTopologySuite()` i ima `geography(point)` kolone i GIST indekse za destinacije, lokacije, mesta i događaje.

Connection string se podešava kroz user-secrets, environment variable ili lokalni `appsettings.*.json`. Ne stavljajte lozinke u git. Design-time `AppDbContextFactory` može da pročita `ConnectionStrings__DefaultConnection` iz environment-a ili user-secrets vrednost iz `Geotrack.Server` projekta.

U Development modu server pokušava:

```sql
CREATE EXTENSION IF NOT EXISTS postgis;
```

Ako korisnik baze nema dozvolu za to, ekstenziju napravite ručno nad bazom pre pokretanja aplikacije.

## Ask Agent

Ask Agent je backend-driven AI funkcionalnost. Angular ne zove Gemini, OpenAI, Claude niti druge model providere direktno. Frontend učitava opcije preko:

- `GET /api/agent/providers`

Glavni chat ide preko:

- `POST /api/agent/chat`

Legacy endpoint `POST /api/agent/message` još postoji zbog kompatibilnosti, ali novi tok koristi `/api/agent/chat`.

Gemini je jedini provider za user-app Ask Agent tok. Model i API ključevi se podešavaju isključivo na backend-u. Frontend ne bira provider/model, ne čuva API key u browser storage-u i ne šalje privremene ili sačuvane korisničke ključeve. Ako jedan Gemini key dostigne kvotu ili rate-limit, backend privremeno hladi taj key i pokušava sledeći konfigurisani key.

## Konfiguracija Gemini API ključeva za Ask Agent

Za lokalno testiranje možete napraviti Gemini API key u Google AI Studio-u. Nemojte ga stavljati u Angular, source kod, `environment` fajlove frontenda ili commit-ovane konfiguracije.

Podešavanje preko user-secrets:

```bash
dotnet user-secrets set "Gemini:ApiKeys:0" "<GEMINI_API_KEY_1>" --project server/Geotrack/Geotrack.Server
dotnet user-secrets set "Gemini:ApiKeys:1" "<GEMINI_API_KEY_2>" --project server/Geotrack/Geotrack.Server
dotnet user-secrets set "Gemini:Model" "gemini-3.5-flash-lite" --project server/Geotrack/Geotrack.Server
dotnet user-secrets set "Gemini:KeyCooldownSeconds" "300" --project server/Geotrack/Geotrack.Server
```

Ista podešavanja kao environment variable imena:

```text
Gemini__ApiKeys__0=<GEMINI_API_KEY_1>
Gemini__ApiKeys__1=<GEMINI_API_KEY_2>
Gemini__Model=gemini-3.5-flash-lite
Gemini__KeyCooldownSeconds=300
```

Podrazumevani model je `gemini-3.5-flash-lite`. Free quota i uslovi korišćenja zavise od Google AI Studio/Gemini pravila. Za produkciju moraju da se kontrolišu kvote, rate-limit-i i monitoring backend key pool-a.

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
- Notification center postoji, ali `/settings/notifications` trenutno preusmerava nazad na `/settings`.
- `admin-app` ima lokalni Regional Manager tok; produkcioni packaging/deployment treba posebno proveriti.
- Angular build ima stroge bundle/style budget-e u `user-app/angular.json`; veće SCSS izmene mogu da izazovu budget upozorenja.
