export const environment = {
  production: true,
  // Replace these examples in the private deployment build configuration.
  apiBaseUrl: 'https://api.example.invalid/api',
  mediaBaseUrl: 'https://api.example.invalid/media',
  userAppBaseUrl: 'https://app.example.invalid',
  mapTiles: {
    provider: 'osm',
    urlTemplate: 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
    attribution: '&copy; OpenStreetMap contributors',
    maxZoom: 19,
    requiresApiKey: false,
    apiKey: '',
  },
};
