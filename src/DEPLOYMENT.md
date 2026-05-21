# GeoTrack deployment - HTTPS, CORS i API konfiguracija

Ovaj dokument opisuje trenutno podrzanu produkcionu arhitekturu. Ne razdvaja
backend i frontend na vise javnih portova.

## Trenutna produkciona arhitektura

Jedan javni origin/port je ocekivan:

- user-app: `https://softeng.pmf.kg.ac.rs:10131/`
- admin-app: `https://softeng.pmf.kg.ac.rs:10131/admin`
- backend API: `https://softeng.pmf.kg.ac.rs:10131/api`

Produkcioni Angular build koristi `apiUrl: "/api"`. Ne koristiti `/admin/api`.
CORS origin je samo `scheme + host + port`, bez `/admin`, `/api`, `/map` ili
drugih putanja.

Samo jedan javni proces moze da slusa port `10131`. Ako `node` slusa port,
Express/Node je javni entrypoint. Ako `dotnet` slusa port, Kestrel je javni
entrypoint. Ako slusa reverse proxy, pratiti njegovu konfiguraciju.

## Provera procesa na portu 10131

```bash
ss -ltnp | grep 10131
sudo lsof -i :10131
ps aux | grep -E "node|dotnet|10131"
screen -ls
```

Tumačenje:

- `node` na `10131`: Express/Node mora da servira `/`, `/admin` i da prosledi
  `/api` ka backendu.
- `dotnet` na `10131`: Kestrel direktno servira HTTPS, staticke Angular fajlove
  i API.
- proxy na `10131`: proveriti proxy pravila za `/`, `/admin`, `/api` i TLS.

`/api` ne sme da bude progutan Angular SPA fallback pravilom.

## Development konfiguracija

Lokalno okruzenje ostaje HTTP i ne zahteva PFX:

- user-app: `http://localhost:4200`
- admin-app: `http://localhost:4300`
- backend: `http://localhost:5003`
- frontend API URL: `http://localhost:5003/api`

Dozvoljeni development CORS origins:

- `http://localhost:4200`
- `http://127.0.0.1:4200`
- `http://localhost:4300`
- `http://127.0.0.1:4300`

Pokretanje:

```bash
dotnet run --project src/server/Geotrack/Geotrack.Server/Geotrack.Server.csproj --launch-profile http
cd src/user-app
npm install
npm start
cd ../admin-app
npm install
npm start
```

## Production konfiguracija

Produkcija koristi:

- `ASPNETCORE_ENVIRONMENT=Production`
- `NODE_ENV=production`, samo ako Node/Express jeste javni proces
- produkcioni Angular build sa `apiUrl: "/api"`
- CORS origin: `https://softeng.pmf.kg.ac.rs:10131`
- PFX/certifikat samo kroz environment varijable na serveru

Ne commit-ovati `.pfx`, `.pem`, `.key`, privatne kljuceve ili lozinke.

## Build komande

Backend:

```bash
dotnet publish src/server/Geotrack/Geotrack.Server/Geotrack.Server.csproj -c Release -o ./publish/server
```

User-app:

```bash
cd src/user-app
npm install
npm run build -- --configuration production
```

Admin-app:

```bash
cd src/admin-app
npm install
npm run build -- --configuration production --base-href /admin/
```

## Ako Kestrel slusa javni HTTPS port

Pronaci PFX:

```bash
find / -type f -name "*.pfx" 2>/dev/null
ls -l /path/to/certificate.pfx
```

Pokretanje:

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=https://0.0.0.0:10131
export ASPNETCORE_Kestrel__Certificates__Default__Path=/path/to/certificate.pfx
export ASPNETCORE_Kestrel__Certificates__Default__Password='PASSWORD_IF_NEEDED'
dotnet Geotrack.Server.dll
```

Ako Kestrel radi iza TLS reverse proxy-ja, ukljuciti `ForwardedHeaders` samo za
poznati proxy/network i po potrebi eksplicitno podesiti
`HttpsRedirection__Enabled=false` za interni HTTP hop.

## Ako Express/Node slusa javni HTTPS port

U ovom repozitorijumu trenutno nije pronadjen aplikacioni `server.js` za
produkciju; Kestrel ima staticki fallback za user-app i admin-app. Ako se uvede
ili koristi Express entrypoint na `10131`, mora da:

- ucita HTTPS iz `HTTPS_PFX_PATH`
- opciono koristi `HTTPS_PFX_PASSWORD`
- servira user-app na `/`
- servira admin-app na `/admin`
- vrati user SPA za rute kao `/map`
- vrati admin SPA za rute kao `/admin/dashboard`
- nikada ne proguta `/api` fallback pravilom
- prosledi `/api` ka backendu preko `BACKEND_URL`
- jasno loguje HTTP ili HTTPS mode
- nikada ne loguje lozinku certifikata

Pokretanje takvog Express servera:

```bash
export NODE_ENV=production
export PORT=10131
export HTTPS_PFX_PATH=/path/to/certificate.pfx
export HTTPS_PFX_PASSWORD='PASSWORD_IF_NEEDED'
export BACKEND_URL=http://127.0.0.1:5003
node server.js
```

Ako `HTTPS_PFX_PATH` nije postavljen, HTTP mode je prihvatljiv samo za lokalni
development ili privremeni interni fallback, ne kao finalna javna produkcija.

## "Not secure" i HTTPS provera

Ako browser prikazuje "Not secure", a URL pocinje sa `https://`, proveriti:

- da li je certifikat izdat od CA kome browser veruje
- da li Subject/SAN sadrzi `softeng.pmf.kg.ac.rs`
- da li je certifikat istekao
- da li je instaliran pun chain/intermediate certifikat
- da li javni proces zaista koristi pravi `.pfx`
- da li proces ima read permisije nad `.pfx`
- da li je PFX lozinka tacna
- da li frontend ucitava HTTP resurse ili HTTP API pozive
- da li je produkcioni `apiUrl` stvarno `/api`, ne `http://localhost:5003/api`
- da li Network tab pokazuje pozive ka `localhost` ili `http://`

Ako je certifikat self-signed, browser ce i dalje prikazivati upozorenje osim
ako je taj certifikat ili njegova CA eksplicitno trusted na klijentu. To nije
bug u kodu. Za pravu produkciju potreban je trusted certifikat.

## Troubleshooting

Not Secure:

- proveriti certifikat, SAN, rok vazenja, chain, PFX path, PFX password i mixed
  content u browser DevTools.

Mixed Content:

- produkcioni frontend ne sme da poziva `http://localhost:5003/api`
- produkcioni frontend treba da koristi `/api`
- proveriti Network tab i built JavaScript bundle.

CORS error:

- proveriti `Cors:AllowedOrigins`
- origin ne sme da sadrzi putanju
- ne koristiti `AllowAnyOrigin` sa credentials
- ako se salje `Authorization`, `AllowAnyHeader` mora da ostane ukljucen.

401 posle login-a:

- proveriti da li cookie stize na `/api/auth` ili `/api/admin/auth`
- proveriti `Secure`, `SameSite`, `withCredentials`, HTTPS i forwarded headers
- proveriti da li token refresh endpoint vraca 200.

Refresh `/map` vraca 404:

- javni server mora da vrati user `index.html` za user SPA rute bez ekstenzije.

Refresh `/admin/dashboard` vraca 404:

- admin build mora da ima base href `/admin/`
- javni server mora da vrati admin `index.html` za `/admin/*` SPA rute.

Frontend i dalje poziva localhost u produkciji:

- proveriti da produkcioni build koristi `environment.prod.ts`
- proveriti da `apiUrl` u produkciji glasi `/api`
- ocistiti stari build/cache na serveru.

## QA checklist

Development:

1. Startovati backend na `http://localhost:5003`.
2. Startovati user-app na `http://localhost:4200`.
3. Startovati admin-app na `http://localhost:4300`.
4. Potvrditi da user-app API pozivi idu na `http://localhost:5003/api`.
5. Potvrditi da admin-app API pozivi idu na `http://localhost:5003/api`.
6. Potvrditi da se lokalno ne koristi produkcioni URL.
7. Potvrditi da login radi.

Production:

1. Buildovati backend, user-app i admin-app.
2. Kopirati output na server.
3. Pronaci `.pfx`.
4. Startovati tacan javni server sa `.pfx`.
5. Otvoriti `https://softeng.pmf.kg.ac.rs:10131/`.
6. Otvoriti `https://softeng.pmf.kg.ac.rs:10131/admin`.
7. Refresh `/map`.
8. Refresh `/admin/dashboard`.
9. Potvrditi da `/api` stize do backend-a.
10. Potvrditi da nema CORS gresaka.
11. Potvrditi da nema Mixed Content gresaka.
12. Potvrditi da nema poziva ka `localhost`.
13. Potvrditi browser certificate status.
14. Potvrditi da login radi.

## Buduca opcija: razdvajanje portova

Cistija arhitektura moze biti poseban javni frontend/proxy port i interni
backend port ili posebni javni frontend/backend portovi. To nije implementirano
u ovom zadatku. Trenutna konfiguracija i dalje podrzava isti javni origin:
`/`, `/admin` i `/api` na `https://softeng.pmf.kg.ac.rs:10131`.
