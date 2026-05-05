export interface ResolvedLocation {
  latitude: number;
  longitude: number;
  city: string;
  region: string;
  country: string;
  displayLabel: string;
  source: 'gps' | 'approximate' | 'manual' | 'default' | 'unknown';
  cachedAt?: number;
}
