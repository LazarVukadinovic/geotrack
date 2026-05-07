export interface ContributionAmenityOption {
  id: string;
  label: string;
}

export const CONTRIBUTION_AMENITY_OPTIONS: ContributionAmenityOption[] = [
  { id: 'pool', label: 'POOL' },
  { id: 'wifi', label: 'WIFI' },
  { id: 'breakfast', label: 'BREAKFAST' },
  { id: 'parking', label: 'PARKING' },
  { id: 'spa', label: 'SPA' },
  { id: 'gym', label: 'GYM' },
  { id: 'dining', label: 'DINING' },
  { id: 'ac', label: 'A/C' },
];

const AMENITY_ID_ALIASES: Record<string, string> = {
  'free-wifi': 'wifi',
  'free-wi-fi': 'wifi',
  'wi-fi': 'wifi',
  'swimming-pool': 'pool',
  'breakfast-included': 'breakfast',
  'included-breakfast': 'breakfast',
  'free-parking': 'parking',
  'spa-wellness': 'spa',
  'spa-and-wellness': 'spa',
  fitness: 'gym',
  'fitness-center': 'gym',
  'fitness-centre': 'gym',
  restaurant: 'dining',
  'on-site-dining': 'dining',
  'onsite-dining': 'dining',
  'on-site-restaurant': 'dining',
  'onsite-restaurant': 'dining',
  'a-c': 'ac',
  'air-conditioning': 'ac',
  'air-conditioned': 'ac',
  'air-conditioner': 'ac',
};

const STAY_AMENITY_API_VALUES: Record<string, string> = {
  wifi: 'free-wifi',
  pool: 'swimming-pool',
  breakfast: 'breakfast-included',
  parking: 'parking',
  spa: 'spa',
  gym: 'fitness-center',
  dining: 'restaurant',
  ac: 'air-conditioning',
};

const PLACE_AMENITY_API_VALUES: Record<string, string> = {
  dining: 'restaurant',
};

export function normalizeContributionAmenityIds(values: readonly string[] | null | undefined): string[] {
  const normalized: string[] = [];
  const seen = new Set<string>();

  for (const value of values ?? []) {
    const key = amenityKey(value);
    if (!key) continue;

    const id = AMENITY_ID_ALIASES[key] ?? key;
    if (seen.has(id)) continue;

    seen.add(id);
    normalized.push(id);
  }

  return normalized;
}

export function toContributionAmenityApiValues(
  values: readonly string[] | null | undefined,
  isAccommodation: boolean,
): string[] {
  const apiMap = isAccommodation ? STAY_AMENITY_API_VALUES : PLACE_AMENITY_API_VALUES;
  const normalized = normalizeContributionAmenityIds(values).map(value => apiMap[value] ?? value);
  return normalized.filter((value, index) => normalized.indexOf(value) === index);
}

function amenityKey(value: string): string {
  return value
    .trim()
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/&/g, ' and ')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}
