export const environment = {
  production: false,
  apiBaseUrl: '/api',
  mediaBaseUrl: '/media',
  userAppBaseUrl: 'http://localhost:4200',
  mapTiles: {
    provider: 'osm',
    urlTemplate: 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
    attribution: '&copy; OpenStreetMap contributors',
    maxZoom: 19,
    requiresApiKey: false,
    apiKey: '',
  },
};
