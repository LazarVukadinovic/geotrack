export interface ContributionAmenityOption {
  id: string;
  label: string;
}

const ALL_AMENITY_OPTIONS: ContributionAmenityOption[] = [
  { id: 'outdoor', label: 'OUTDOOR SEATING' },
  { id: 'wifi', label: 'WIFI' },
  { id: 'parking', label: 'PARKING' },
  { id: 'ac', label: 'A/C' },
  { id: 'breakfast', label: 'BREAKFAST' },
  { id: 'sea', label: 'SEA VIEW' },
  { id: 'family', label: 'FAMILY FRIENDLY' },
  { id: 'pool', label: 'POOL' },
  { id: 'spa', label: 'SPA' },
  { id: 'gym', label: 'GYM' },
  { id: 'dining', label: 'DINING' },
];

const AMENITY_LABELS = new Map(ALL_AMENITY_OPTIONS.map(option => [option.id, option.label]));

const PLACE_TYPE_AMENITY_IDS: Record<string, string[]> = {
  stay:       ['wifi', 'pool', 'breakfast', 'parking', 'spa', 'gym', 'dining', 'ac', 'outdoor', 'sea', 'family'],
  restaurant: ['outdoor', 'wifi', 'parking', 'ac', 'sea'],
  cafe:       ['outdoor', 'wifi', 'breakfast', 'parking', 'ac', 'sea'],
  bar:        ['outdoor', 'wifi', 'parking', 'ac'],
  beach:      ['parking', 'outdoor', 'family', 'wifi', 'sea'],
  landmark:   ['parking', 'wifi', 'ac'],
  nature:     ['parking', 'outdoor', 'family'],
  museum:     ['parking', 'wifi', 'ac'],
  gallery:    ['parking', 'wifi', 'ac'],
};

export function getAmenitiesForPlaceType(placeType: string | null | undefined): ContributionAmenityOption[] {
  const key = (placeType ?? '').trim().toLowerCase();
  const ids = PLACE_TYPE_AMENITY_IDS[key];
  if (!ids) return ALL_AMENITY_OPTIONS;
  return ids.map(id => ALL_AMENITY_OPTIONS.find(o => o.id === id)!).filter(Boolean);
}

/** @deprecated use getAmenitiesForPlaceType */
export const CONTRIBUTION_AMENITY_OPTIONS = ALL_AMENITY_OPTIONS;

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
  'family-friendly': 'family',
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
  family: 'family-friendly',
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
  return AMENITY_LABELS.get(normalizeContributionAmenityIds([id])[0] ?? id) ?? id;
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
