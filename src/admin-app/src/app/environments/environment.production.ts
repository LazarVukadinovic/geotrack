export const environment = {
  production: true,
  apiBaseUrl: 'https://softeng.pmf.kg.ac.rs:10133/api',
  mediaBaseUrl: 'https://softeng.pmf.kg.ac.rs:10133/media',
  userAppBaseUrl: 'https://softeng.pmf.kg.ac.rs:10131',
  mapTiles: {
    provider: 'osm',
    urlTemplate: 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
    attribution: '&copy; OpenStreetMap contributors',
    maxZoom: 19,
    requiresApiKey: false,
    apiKey: '',
  },
};
