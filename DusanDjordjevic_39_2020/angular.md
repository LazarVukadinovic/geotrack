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

## ✅ Provera

```bash
node -v
npm -v
```

Ako ispiše verzije → Node radi ✔️

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

* Routing: YES
* Style: CSS

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

# 8. Zaključak (najbitnije)

* `node -v` → proverava Node
* `npm` → upravlja paketima
* `ng new` → pravi Angular app
* `ng serve` → pokreće Angular

---

# Minimalni workflow

1. Instaliraš Node
2. Instaliraš Angular CLI
3. Kreiraš projekat
4. Pokreneš aplikaciju

---

# To je to

Ovo je minimum koji ti treba da pokažeš da:

* Node.js radi
* Angular radi
* znaš kako da pokreneš projekat
