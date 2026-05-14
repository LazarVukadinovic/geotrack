const express = require('express');
const fs = require('fs');
const http = require('http');
const https = require('https');
const path = require('path');

const app = express();
const port = Number(process.env.PORT || 8080);
const apiProxyTarget = process.env.API_PROXY_TARGET;

const userRoot = resolveConfiguredPath(
  process.env.USER_APP_DIST,
  path.join(__dirname, 'dist', 'user-app', 'browser'),
);
const adminRoot = resolveConfiguredPath(
  process.env.ADMIN_APP_DIST,
  path.join(__dirname, '..', 'admin-app', 'dist', 'admin-app', 'browser'),
);
const userIndex = path.join(userRoot, 'index.html');
const adminIndex = path.join(adminRoot, 'index.html');

app.disable('x-powered-by');

app.use('/api', proxyToBackendOrReject);
app.use('/media', proxyToBackendOrReject);

app.use('/admin', express.static(adminRoot, {
  index: false,
  maxAge: '1y',
  immutable: true,
}));

app.use(express.static(userRoot, {
  index: false,
  maxAge: '1y',
  immutable: true,
}));

app.use((req, res, next) => {
  if (!isSpaNavigation(req)) {
    next();
    return;
  }

  if (req.path === '/admin' || req.path.startsWith('/admin/')) {
    sendIndex(res, adminIndex, 'admin-app');
    return;
  }

  if (req.path.startsWith('/api') || req.path.startsWith('/media')) {
    res.sendStatus(404);
    return;
  }

  sendIndex(res, userIndex, 'user-app');
});

app.use((_req, res) => {
  res.sendStatus(404);
});

const server = createServer(app);
server.listen(port, () => {
  const scheme = process.env.HTTPS_PFX_PATH ? 'https' : 'http';
  console.log(`GeoTrack static server listening on ${scheme}://0.0.0.0:${port}`);
});

function createServer(handler) {
  const pfxPath = process.env.HTTPS_PFX_PATH;

  if (!pfxPath) {
    return http.createServer(handler);
  }

  const options = {
    pfx: fs.readFileSync(pfxPath),
  };

  if (process.env.HTTPS_PFX_PASSWORD) {
    options.passphrase = process.env.HTTPS_PFX_PASSWORD;
  }

  return https.createServer(options, handler);
}

function proxyToBackendOrReject(req, res) {
  if (!apiProxyTarget) {
    res.status(502).send('API_PROXY_TARGET is not configured.');
    return;
  }

  const targetUrl = new URL(req.originalUrl, apiProxyTarget);
  const client = targetUrl.protocol === 'https:' ? https : http;
  const headers = { ...req.headers, host: targetUrl.host };

  delete headers.connection;
  delete headers['keep-alive'];
  delete headers['proxy-authenticate'];
  delete headers['proxy-authorization'];
  delete headers.te;
  delete headers.trailer;
  delete headers['transfer-encoding'];
  delete headers.upgrade;

  const proxyReq = client.request(
    targetUrl,
    {
      method: req.method,
      headers,
    },
    proxyRes => {
      res.writeHead(proxyRes.statusCode || 502, proxyRes.headers);
      proxyRes.pipe(res);
    },
  );

  proxyReq.on('error', () => {
    if (!res.headersSent) {
      res.status(502).send('Backend API is unavailable.');
    } else {
      res.end();
    }
  });

  req.pipe(proxyReq);
}

function isSpaNavigation(req) {
  return (req.method === 'GET' || req.method === 'HEAD') &&
    !path.extname(req.path);
}

function sendIndex(res, indexPath, appName) {
  if (!fs.existsSync(indexPath)) {
    res.status(404).send(`${appName} index.html not found.`);
    return;
  }

  res.sendFile(indexPath);
}

function resolveConfiguredPath(configuredPath, fallbackPath) {
  return path.resolve(configuredPath || fallbackPath);
}
