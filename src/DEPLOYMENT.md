# GeoTrack uputstvo za produkciono postavljanje

Ovaj dokument opisuje trenutni očekivani produkcioni raspored za GeoTrack i korake koje treba proveriti pre izmene portova, HTTPS-a ili načina serviranja aplikacije.

Trenutno izgleda da produkcija radi preko jednog javnog porta, za sada preko HTTP-a:

- user-app: `http://softeng.pmf.kg.ac.rs:10131/`
- admin-app: `http://softeng.pmf.kg.ac.rs:10131/admin`
- backend API: `http://softeng.pmf.kg.ac.rs:10131/api`

Željeni HTTPS cilj je:

- user-app: `https://softeng.pmf.kg.ac.rs:10131/`
- admin-app: `https://softeng.pmf.kg.ac.rs:10131/admin`
- backend API: `https://softeng.pmf.kg.ac.rs:10131/api`

Ako se sve servira sa istog javnog porta, produkcioni `apiUrl` za obe Angular aplikacije treba da bude:

```ts
apiUrl: '/api'
```

Ne koristiti `/admin/api`. Admin aplikacija jeste pod `/admin`, ali API ostaje pod `/api`.

## Prvo proveriti ko sluša port 10131

Tri različita procesa ne mogu u isto vreme da slušaju isti port. Ako user-app, admin-app i backend API deluju kao da rade na istom javnom portu, onda najverovatnije postoji jedan glavni proces ili proxy koji sluša port `10131`.

Taj glavni proces može biti:

- Node/Express koji servira Angular statičke fajlove i prosleđuje `/api` backendu
- .NET/Kestrel koji servira API i statičke fajlove
- neki spoljašnji reverse proxy

Pre izmene SSL-a ili portova, na serveru obavezno proveriti koji proces stvarno sluša port:

```bash
ss -ltnp | grep 10131
```

```bash
sudo lsof -i :10131
```

```bash
ps aux | grep -E "node|dotnet|10131"
```

```bash
screen -ls
```

Tumačenje rezultata:

- ako `node` sluša `10131`, Express je verovatno javni ulaz u aplikaciju
- ako `dotnet` sluša `10131`, Kestrel/.NET je verovatno javni ulaz u aplikaciju
- ako port sluša neki drugi proxy, pratiti konfiguraciju tog proxy-ja
- ako postoji više `screen` sesija, proveriti u kojoj sesiji je pokrenut javni proces

Ne pokretati drugi proces na portu `10131` dok se ne zna šta trenutno radi na tom portu.

## Trenutna struktura postavljanja

Repozitorijum ima tri dela:

- backend: `.NET` projekat `server/Geotrack/Geotrack.Server`
- user-app: Angular aplikacija u `user-app`
- admin-app: Angular aplikacija u `admin-app`

U produkciji postoje dva realna načina za isti javni port:

1. Express je javni proces na `10131`, servira obe Angular aplikacije i prosleđuje `/api` backendu koji radi na internom portu.
2. Kestrel/.NET je javni proces na `10131`, servira API i statičke Angular fajlove iz `wwwroot`.

Lokalni fajl `server.js` u korenu ovog direktorijuma samo pokreće `user-app/server.js`. Express logika je u `user-app/server.js`.

## Šta može da pripremi kod, a šta mora ručno na serveru

Kod i repozitorijum mogu da pripreme:

- produkcione komande za build
- Express rutiranje za `/api`, `/media`, `/admin` i Angular fallback
- CORS listu dozvoljenih origin-a
- `apiUrl` vrednosti za razvojno i produkciono okruženje
- dokumentovane environment promenljive

Na serveru ručno mora da se uradi:

- proveriti koji proces sluša `10131`
- pronaći stvarni `.pfx` sertifikat
- proveriti dozvole za čitanje sertifikata
- uneti lozinku sertifikata kao environment promenljivu ili kroz skladište tajni
- kopirati publish/build foldere na tačno mesto koje koristi javni proces
- pokrenuti odgovarajući proces u `screen` sesiji ili kroz servis
- proveriti HTTPS, CORS, login i rute posle osvežavanja stranice

Ne commit-ovati sertifikate, privatne ključeve ili lozinke.

## Produkcioni build

Komande ispod su date za koren repozitorijuma. Ako ste već u direktorijumu `src`, za backend komandu uklonite početno `src/`.

Backend:

```bash
dotnet publish src/server/Geotrack/Geotrack.Server/Geotrack.Server.csproj -c Release -o ./publish/server
```

User aplikacija:

```bash
cd src/user-app
npm install
npm run build -- --configuration production
```

Admin aplikacija pod `/admin`:

```bash
cd src/admin-app
npm install
npm run build -- --configuration production --base-href /admin/
```

Za trenutni očekivani raspored `https://softeng.pmf.kg.ac.rs:10131/admin`, admin aplikacija mora biti build-ovana sa `--base-href /admin/`. U `admin-app/angular.json` je produkcioni `baseHref` već podešen na `/admin/`.

Ako se admin-app kasnije postavlja na poseban port u root-u, na primer `https://softeng.pmf.kg.ac.rs:<admin-port>/`, tada `--base-href /admin/` obično nije potreban.

Tipični Angular izlazni folderi su:

- user-app: `src/user-app/dist/user-app/browser`
- admin-app: `src/admin-app/dist/admin-app/browser`

## Express postavljanje na jednom javnom portu

Express se koristi za serviranje statičkih Angular fajlova tako da osvežavanje Angular ruta ne vraća `404`.

Ako jedan Express server servira user-app i admin-app na istom portu, mora da radi ovim redosledom:

1. `/api/*` mora da ide u backend API ili da se proxy-uje na backend
2. `/media/*` mora da ide u backend ako backend servira upload/seed fajlove
3. `/admin/*` mora da servira statičke fajlove admin aplikacije
4. `/admin/*` bez ekstenzije mora da vrati `admin-app/index.html`
5. sve ostale frontend rute, kao `/map`, `/explore` i `/profile`, moraju da vrate `user-app/index.html`
6. `/api` ne sme da bude progutan Angular fallback rutom

Rute koje moraju raditi i posle direktnog osvežavanja u pregledaču:

- `https://softeng.pmf.kg.ac.rs:10131/map`
- `https://softeng.pmf.kg.ac.rs:10131/explore`
- `https://softeng.pmf.kg.ac.rs:10131/admin`
- `https://softeng.pmf.kg.ac.rs:10131/admin/dashboard`

U ovom repozitorijumu `user-app/server.js` trenutno:

- prvo obrađuje `/api` i `/media`
- zatim servira admin statičke fajlove pod `/admin`
- zatim servira user statičke fajlove
- za `/admin` Angular rute vraća admin `index.html`
- za ostale Angular rute vraća user `index.html`
- ne dozvoljava da `/api` i `/media` padnu u frontend fallback
- čita `HTTPS_PFX_PATH`, `HTTPS_PFX_PASSWORD`, `API_PROXY_TARGET`, `USER_APP_DIST`, `ADMIN_APP_DIST` i `PORT`

Ako Express ne čita `HTTPS_PFX_PATH`, samo exportovanje `HTTPS_PFX_PATH` neće uključiti HTTPS. `server.js` mora eksplicitno da napravi HTTPS server iz tog `.pfx` fajla.

Primer kada je Express javni HTTPS proces na `10131`, a backend radi interno:

Backend na internom portu:

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://127.0.0.1:5003
dotnet Geotrack.Server.dll
```

Express na javnom portu:

```bash
export NODE_ENV=production
export PORT=10131
export HTTPS_PFX_PATH=/path/to/certificate.pfx
export HTTPS_PFX_PASSWORD='PASSWORD_IF_NEEDED'
export API_PROXY_TARGET=http://127.0.0.1:5003
node server.js
```

Ako postoje odvojeni Express serveri za user-app i admin-app, moraju koristiti različite portove. Ako jedan Express server servira obe aplikacije na istom portu, mora pravilno da obradi `/admin` i `/api` rute.

Ako su build folderi kopirani na drugo mesto, podesiti:

```bash
export USER_APP_DIST=/path/to/user-app/browser
export ADMIN_APP_DIST=/path/to/admin-app/browser
```

## Kestrel/.NET postavljanje na jednom javnom portu

Ako `dotnet` proces sluša `10131`, onda je Kestrel/.NET verovatno javni proces. Tada backend prima `/api`, a statički Angular fajlovi treba da budu u `wwwroot`.

Primer kopiranja build izlaza u publish folder:

```bash
cp -r src/user-app/dist/user-app/browser/* publish/server/wwwroot/
mkdir -p publish/server/wwwroot/admin
cp -r src/admin-app/dist/admin-app/browser/* publish/server/wwwroot/admin/
```

Kestrel mora da vrati:

- `/api/*` kao backend API
- `/media/*` kao backend media fajlove
- `/admin/*` kao admin aplikaciju ili admin Angular fallback
- `/map`, `/explore`, `/profile` i ostale user rute kao user Angular fallback

Backend kod trenutno ima fallback koji ne treba da proguta `/api` i `/media`, i ima poseban fallback za `/admin` Angular rute ako postoji `wwwroot/admin/index.html`.

Primer pokretanja Kestrel HTTPS servera sa `.pfx` sertifikatom:

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=https://0.0.0.0:10131
export ASPNETCORE_Kestrel__Certificates__Default__Path=/path/to/certificate.pfx
export ASPNETCORE_Kestrel__Certificates__Default__Password='PASSWORD_IF_NEEDED'
dotnet Geotrack.Server.dll
```

Ako je Express javni server na `10131`, backend obično ne treba da sluša `10131`, nego interni port kao `http://127.0.0.1:5003`, a Express treba da proxy-uje `/api` ka njemu.

## HTTPS i .pfx sertifikat

Pronaći `.pfx` sertifikat na serveru:

```bash
find / -type f -name "*.pfx" 2>/dev/null
```

Primer moguće putanje:

```bash
/home/zenit/.dotnet/corefx/cryptography/x509stores/my/neki_naziv.pfx
```

Proveriti dozvole:

```bash
ls -l /path/to/certificate.pfx
```

Važna pravila:

- ne commit-ovati `.pfx`
- ne commit-ovati `.key`
- ne commit-ovati `.pem`
- ne commit-ovati lozinku sertifikata
- sertifikat mora da važi za `softeng.pmf.kg.ac.rs`
- ako je sertifikat self-signed ili nije poverljiv, pregledač može prikazati upozorenje
- proces koji pokreće aplikaciju mora imati pravo da čita `.pfx`
- port na kome se očekuje HTTPS mora stvarno biti pokrenut kao HTTPS, ne kao HTTP

## CORS origin-i

CORS origin obuhvata samo protokol, domen i port. Ne uključuje `/admin`, `/api`, `/map` ili `/explore`.

Backend CORS se podešava kroz konfiguraciju:

- razvojno okruženje: `server/Geotrack/Geotrack.Server/appsettings.Development.json`
- produkcija: `server/Geotrack/Geotrack.Server/appsettings.Production.json`
- environment override je moguć kroz standardne .NET promenljive, na primer `Cors__AllowedOrigins__0`

Razvojni CORS treba da dozvoli:

```text
http://localhost:4200
http://127.0.0.1:4200
http://localhost:4300
http://127.0.0.1:4300
```

Produkcioni CORS za isti origin/isti port treba da dozvoli:

```text
https://softeng.pmf.kg.ac.rs:10131
```

Ako se HTTP privremeno još koristi, dodati i:

```text
http://softeng.pmf.kg.ac.rs:10131
```

U produkcionom `appsettings.Production.json` trenutno je podešen HTTPS origin:

```json
"Cors": {
  "AllowedOrigins": [
    "https://softeng.pmf.kg.ac.rs:10131"
  ]
}
```

Ako user-app i admin-app kasnije budu na različitim produkcionim portovima, oba origin-a moraju biti dodata u backend CORS. Primer:

```text
https://softeng.pmf.kg.ac.rs:<user-app-port>
https://softeng.pmf.kg.ac.rs:<admin-app-port>
```

Ne koristiti wildcard CORS (`*`) zajedno sa credential-ima. Backend trenutno koristi `AllowCredentials()`, zato lista origin-a mora biti eksplicitna.

## Frontend apiUrl

Frontend `apiUrl` se podešava u Angular environment fajlovima:

- user-app razvojno: `user-app/src/environments/environment.ts`
- user-app produkcija: `user-app/src/environments/environment.production.ts`
- admin-app razvojno: `admin-app/src/environments/environment.ts`
- admin-app produkcija: `admin-app/src/environments/environment.production.ts`

Razvojno okruženje:

```ts
apiUrl: 'http://localhost:5003/api'
```

Produkcija, isti origin i isti port:

```ts
apiUrl: '/api'
```

Produkcija, backend na posebnom javnom portu:

```ts
apiUrl: 'https://softeng.pmf.kg.ac.rs:<backend-port>/api'
```

Za admin-app pod `/admin`, API treba i dalje da bude `/api`, ne `/admin/api`.

Ako se promeni `apiUrl`, obavezno ponovo uraditi build i postaviti frontend. Stari build može nastaviti da šalje pozive na staru adresu, na primer `http://localhost:5003/api`.

## Pokretanje u screen sesijama

Backend primer:

```bash
screen -S geotrack-backend
```

Zatim u toj sesiji podesiti environment promenljive i pokrenuti backend, na primer:

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://127.0.0.1:5003
dotnet Geotrack.Server.dll
```

Frontend/Express primer:

```bash
screen -S geotrack-frontend
```

Zatim u toj sesiji:

```bash
export NODE_ENV=production
export PORT=10131
export HTTPS_PFX_PATH=/path/to/certificate.pfx
export HTTPS_PFX_PASSWORD='PASSWORD_IF_NEEDED'
export API_PROXY_TARGET=http://127.0.0.1:5003
node server.js
```

Rad sa `screen` sesijama:

- detach iz sesije: `Ctrl + A`, zatim `D`
- povratak u backend sesiju: `screen -r geotrack-backend`
- povratak u frontend sesiju: `screen -r geotrack-frontend`
- lista sesija: `screen -ls`

## Ručna lista za postavljanje

- uraditi backend build lokalno
- uraditi user-app build lokalno
- uraditi admin-app build lokalno sa ispravnim `base-href`
- kopirati backend publish folder na server
- kopirati user-app `dist` na server
- kopirati admin-app `dist` na server
- proveriti koji proces sluša `10131`
- pronaći `.pfx`
- proveriti dozvole nad `.pfx`
- pokrenuti backend
- pokrenuti Express/static server ili Kestrel javni server, zavisno od izabrane arhitekture
- otvoriti `https://softeng.pmf.kg.ac.rs:10131/`
- otvoriti `https://softeng.pmf.kg.ac.rs:10131/admin`
- osvežiti `https://softeng.pmf.kg.ac.rs:10131/map`
- osvežiti `https://softeng.pmf.kg.ac.rs:10131/admin/dashboard`
- proveriti da `/api/...` stiže do backend-a
- proveriti da nema CORS grešaka
- proveriti da nema mixed content grešaka
- proveriti da login radi
- proveriti da API pozivi koriste HTTPS
- proveriti u Network tabu da nema poziva na `localhost`

## Rešavanje problema

### CORS greška

Mogući uzroci:

- produkcioni CORS nema tačan origin
- koristi se `http` umesto `https`
- port nije isti kao u CORS konfiguraciji
- koristi se credential auth sa wildcard origin-om
- frontend poziva pogrešan backend URL
- frontend build je star i još koristi staru `apiUrl` vrednost

Proveriti u kartici Network u pregledaču vrednost `Origin` header-a i uporediti je sa `Cors:AllowedOrigins`.

### Greška mešanog sadržaja (Mixed Content)

Mogući uzroci:

- stranica je otvorena preko HTTPS-a, ali API URL je HTTP
- produkcioni environment još koristi `http://localhost:5003/api`
- postavljen je stari frontend build
- backend ili proxy vraća redirect na HTTP

Rešenje je da produkcioni frontend poziva `/api` ili HTTPS backend adresu.

### 401 posle login-a

Mogući uzroci:

- token ili cookie nije sačuvan u pregledaču
- frontend poziva pogrešan API origin
- CORS blokira `Authorization` header ili cookie-je
- JWT konfiguracija nedostaje u `Production`
- cookie ima pogrešan `SameSite` ili `Secure` režim
- HTTPS stranica pokušava da koristi stari HTTP API URL

Proveriti karticu Network, `Authorization` header, response status i backend logove.

### SSL greška

Mogući uzroci:

- `.pfx` putanja je pogrešna
- proces nema dozvolu da pročita `.pfx`
- lozinka sertifikata nedostaje ili je pogrešna
- sertifikat ne važi za `softeng.pmf.kg.ac.rs`
- server i dalje pokreće HTTP na portu gde pregledač očekuje HTTPS
- drugi proces već sluša `10131`

Prvo proveriti `ss -ltnp | grep 10131`, zatim putanju i dozvole sertifikata.

### Refresh rute vraća 404

Mogući uzroci:

- Express ili Kestrel fallback nije podešen
- `/admin/*` ne vraća admin `index.html`
- `/map` ne vraća user-app `index.html`
- server tretira Angular rute kao stvarne fajlove
- admin build nema ispravan `base-href /admin/`
- statički fajlovi nisu kopirani na putanju koju koristi javni proces

Rute koje obavezno testirati posle svakog postavljanja:

```text
/map
/explore
/admin
/admin/dashboard
```
