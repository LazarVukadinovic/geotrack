# Node.js + Angular – OSNOVNA dokumentacija

---

# 1. Šta su Node.js i Angular

* **Node.js** → omogućava pokretanje JavaScript-a na serveru (backend)
* **Angular** → frontend framework za pravljenje web aplikacija

---

# 2. Instalacija Node.js

1. Otići na: [https://nodejs.org](https://nodejs.org)
2. Preuzeti **LTS verziju**
3. Pokrenuti instalaciju (Next → Install → Finish)

## Provera

```bash
node -v
npm -v
```

Ako ispiše verzije → Node radi 

---

# 3. Kreiranje Node.js projekta

```bash
mkdir backend
cd backend
npm init -y
```

Ovo kreira **package.json** fajl

---

# 4. Pokretanje Node.js aplikacije

Napraviti fajl `app.js`:

```js
console.log("Node radi!");
```

Pokretanje:

```bash
node app.js
```

---

# 5. Instalacija Angular CLI

```bash
npm install -g @angular/cli
```

## Provera

```bash
ng version
```

---

# 6. Kreiranje Angular aplikacije

```bash
ng new frontend
```

Opcije:

* Which stylesheet system would you like to use?: CSS
* Do you want to enable Server-Side Rendering (SSR) and Static Site Generation (SSG/Prerendering)?: YES
* Which AI tools do you want to configure with Angular best practices? https://angular.dev/ai/develop-with-ai -> None

---

# 7. Pokretanje Angular aplikacije

```bash
cd frontend
ng serve
```

Otvoriti u browseru:

```
http://localhost:4200
```

Ako vidiš Angular stranicu → radi 

---


