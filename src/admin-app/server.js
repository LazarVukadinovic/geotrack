const express = require('express');
const fs = require('fs');
const http = require('http');
const https = require('https');
const path = require('path');

const appName = 'admin-app';
const defaultPort = 10132;
const host = '0.0.0.0';
const isProduction = process.env.NODE_ENV === 'production';
const port = readPort(process.env.PORT, defaultPort);
const buildDir = path.join(__dirname, 'dist', appName, 'browser');
const indexPath = path.join(buildDir, 'index.html');
const backendOrigin = normalizeOrigin(
  process.env.BACKEND_ORIGIN
    || (isProduction ? 'https://softeng.pmf.kg.ac.rs:10133' : 'http://localhost:5003')
);

if (!fs.existsSync(indexPath)) {
  fail(`Angular build output was not found at ${indexPath}. Run npm run build first.`);
}

const app = express();
app.disable('x-powered-by');

app.use((req, res, next) => {
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('Referrer-Policy', 'strict-origin-when-cross-origin');
  next();
});

app.use('/media', (req, res) => {
  res.redirect(307, `${backendOrigin}${req.originalUrl}`);
});

app.use((req, res, next) => {
  if (req.path === '/api' || req.path.startsWith('/api/')) {
    res.status(404).json({
      message: `The API is served by the backend at ${backendOrigin}/api.`,
    });
    return;
  }

  next();
});

app.use(express.static(buildDir, {
  index: false,
  redirect: false,
  setHeaders(res, filePath) {
    if (filePath === indexPath) {
      res.setHeader('Cache-Control', 'no-store');
      return;
    }

    res.setHeader('Cache-Control', 'public, max-age=31536000, immutable');
  },
}));

app.get(/.*/, (req, res, next) => {
  if (req.path === '/api' || req.path.startsWith('/api/') || req.path.startsWith('/media/')) {
    next();
    return;
  }

  if (path.extname(req.path)) {
    next();
    return;
  }

  res.sendFile(indexPath);
});

app.use((_req, res) => {
  res.status(404).send('Not found');
});

const serverOptions = readHttpsOptions();
const server = serverOptions
  ? https.createServer(serverOptions, app)
  : http.createServer(app);

server.listen(port, host, () => {
  const protocol = serverOptions ? 'https' : 'http';
  console.log(`${appName} static server listening on ${protocol}://${host}:${port}`);
});

function readHttpsOptions() {
  const pfxPath = process.env.HTTPS_PFX_PATH;

  if (!pfxPath) {
    if (isProduction) {
      fail('HTTPS_PFX_PATH is required when NODE_ENV=production.');
    }

    return null;
  }

  if (!fs.existsSync(pfxPath)) {
    fail(`HTTPS_PFX_PATH does not exist: ${pfxPath}`);
  }

  return {
    pfx: fs.readFileSync(pfxPath),
    passphrase: process.env.HTTPS_PFX_PASSWORD || undefined,
  };
}

function readPort(value, fallback) {
  const parsed = Number(value || fallback);
  if (!Number.isInteger(parsed) || parsed < 1 || parsed > 65535) {
    fail(`PORT must be a valid TCP port. Received: ${value}`);
  }

  return parsed;
}

function normalizeOrigin(value) {
  return String(value || '').replace(/\/+$/, '');
}

function fail(message) {
  console.error(message);
  process.exit(1);
}
