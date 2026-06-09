export interface ContributionAmenityOption {
  id: string;
  label: string;
}

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
  'outdoor-seating': 'outdoor',
  'sea-view': 'sea',
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
  outdoor: 'outdoor-seating',
  sea: 'sea-view',
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

export function getContributionAmenityLabel(id: string): string {
  return toDisplayLabel(normalizeContributionAmenityIds([id])[0] ?? id);
}

function amenityKey(value: string): string {
  return value
    .trim()
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/&/g, ' and ')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

function toDisplayLabel(value: string): string {
  const normalized = value.trim();
  if (!normalized) return value;
  if (normalized.toLowerCase() === 'ac') return 'A/C';
  return normalized
    .replace(/-/g, ' ')
    .replace(/\b\w/g, letter => letter.toUpperCase());
}
