import type { ExploreItem } from '../../../core/models/explore-item.model';
import type {
  ExploreAttraction,
  ExploreEvent,
  ExploreLandmarkCategory,
  ExploreNearbyItem,
  ExplorePromoCard,
  ExploreStay,
  NearbyStatus,
} from '../models/explore-extras.model';

/** Stable Unsplash URLs (format + moderate width) to reduce hotlink / 404 issues. */
const u = (id: string, w = 640) =>
  `https://images.unsplash.com/${id}?ixlib=rb-4.0.3&auto=format&fit=crop&w=${w}&q=75`;

// ─── Kept for compatibility (used by LocalFavoritesList) ─────────────────────

export const MOCK_LOCAL_FAVORITES: ExploreItem[] = [
  {
    id: 'loc-1',
    title: 'Buža Bar',
    subtitle: 'Cliffside Drinks',
    description: 'Iconic cliff bar with Adriatic views.',
    category: 'restaurant',
    type: 'restaurant',
    location: 'Old Town walls',
    image: u('photo-1485872299829-c673f5194813', 560),
    rating: 4.8,
    isSaved: false,
    openingHours: 'Closes 12 AM',
    isRecommended: false,
  },
  {
    id: 'loc-2',
    title: 'Nautika Restaurant',
    subtitle: 'Dining',
    description: 'Fine dining on the waterfront terrace.',
    category: 'restaurant',
    type: 'restaurant',
    location: 'Pile Gate',
    image: u('photo-1517248135467-4c7edcad34c4', 560),
    rating: 4.9,
    isSaved: false,
    openingHours: 'Closes 11 PM',
    isRecommended: false,
  },
  {
    id: 'loc-3',
    title: 'Cogito',
    subtitle: 'Specialty Coffee',
    description: 'Specialty roasts and cozy corners.',
    category: 'activity',
    type: 'cafe',
    location: 'Stradun',
    image: u('photo-1509042239860-f550ce710b93', 560),
    rating: 4.7,
    isSaved: false,
    openingHours: 'Closes 9 PM',
    isRecommended: false,
  },
  {
    id: 'loc-4',
    title: 'Sunrise Hike',
    subtitle: 'Srđ cable + trail',
    description: 'Early trail above the city walls.',
    category: 'activity',
    type: 'activity',
    location: 'Mount Srđ',
    image: u('photo-1464822759023-fed622ff2c3b', 560),
    rating: 4.9,
    isSaved: false,
    openingHours: '6 AM – 8 PM',
    isRecommended: false,
  },
];

// ─── Nearby Status ────────────────────────────────────────────────────────────

export const MOCK_NEARBY_STATUS: NearbyStatus = {
  label: 'Near',
  value: 'Old Town · 2.5 km',
};

// ─── Promo Card ───────────────────────────────────────────────────────────────

export const MOCK_PROMO_CARD: ExplorePromoCard = {
  title: 'Personalize your stay',
  message: 'Sign up to get tailored local secrets and bespoke recommendations delivered to you.',
  ctaLabel: 'Sign Up →',
};

// ─── Events This Week ─────────────────────────────────────────────────────────

export const MOCK_EVENTS_THIS_WEEK: ExploreEvent[] = [
  {
    id: 'ev-1',
    title: 'Summer Festival',
    subtitle: 'Old Town Square',
    dateLabel: 'Jul 10 · 8:00 PM',
    image: u('photo-1514512364185-4c2b2db318b5', 560),
  },
  {
    id: 'ev-2',
    title: 'Jazz Night',
    subtitle: 'Sunset Stage · Old Town',
    dateLabel: 'Fri · 7:30 PM',
    image: u('photo-1485872299829-c673f5194813', 560),
  },
  {
    id: 'ev-3',
    title: 'Food Market',
    subtitle: 'Harbor · Local Bites',
    dateLabel: 'Sun · 11:00 AM',
    image: u('photo-1539571696357-5a69c17a67c6', 560),
  },
];

// ─── Popular Nearby ───────────────────────────────────────────────────────────

export const MOCK_POPULAR_NEARBY: ExploreNearbyItem[] = [
  {
    id: 'nb-1',
    title: 'Old Town Walls',
    categoryLabel: 'Historic Site',
    image: u('photo-1558618666-fcd25c85cd64', 560),
    rating: 4.9,
    reviewCount: '12k',
    distance: '0.5 km away',
    isSaved: false,
  },
  {
    id: 'nb-2',
    title: 'Banje Beach',
    categoryLabel: 'Beach',
    image: u('photo-1507525428034-b723cf961d3e', 560),
    rating: 4.7,
    reviewCount: '8.5k',
    distance: '1.2 km away',
    isSaved: false,
  },
  {
    id: 'nb-3',
    title: 'Stradun',
    categoryLabel: 'Landmark',
    image: u('photo-1526481280695-3c469172d3b8', 560),
    rating: 4.8,
    reviewCount: '20k',
    distance: '0.3 km away',
    isSaved: false,
  },
  {
    id: 'nb-4',
    title: 'Lokrum Island',
    categoryLabel: 'Nature',
    image: u('photo-1506905925346-21bda4d32df4', 560),
    rating: 4.6,
    reviewCount: '6k',
    distance: '1.8 km away',
    isSaved: false,
  },
];

// ─── Top Stays Near You ───────────────────────────────────────────────────────

export const MOCK_TOP_STAYS: ExploreStay[] = [
  {
    id: 'stay-1',
    title: 'Grand Hotel 1',
    image: u('photo-1566073771259-6a8506099945', 560),
    rating: 4.9,
    distance: '0.2 km away',
    pricePerNight: 121,
    isSaved: false,
  },
  {
    id: 'stay-2',
    title: 'Grand Hotel 2',
    image: u('photo-1520250497591-112f2f40a3f4', 560),
    rating: 4.8,
    distance: '0.4 km away',
    pricePerNight: 122,
    isSaved: false,
  },
  {
    id: 'stay-3',
    title: 'Villa Adriatic',
    image: u('photo-1582719508461-905c673771fd', 560),
    rating: 4.7,
    distance: '0.9 km away',
    pricePerNight: 98,
    isSaved: false,
  },
];

// ─── Must-See Attractions ─────────────────────────────────────────────────────

export const MOCK_MUST_SEE_ATTRACTIONS: ExploreAttraction[] = [
  {
    id: 'attr-1',
    title: 'Fort Lovrijenac',
    type: 'Historic Fortress',
    image: u('photo-1558618666-fcd25c85cd64', 560),
  },
  {
    id: 'attr-2',
    title: 'Dubrovnik Cable Car',
    type: 'Panoramic Views',
    image: u('photo-1580137189272-c9379f8864fd', 560),
  },
  {
    id: 'attr-3',
    title: 'Lokrum Island',
    type: 'Nature Reserve',
    image: u('photo-1467269204594-9661b134dd2b', 560),
  },
  {
    id: 'attr-4',
    title: 'Rector\'s Palace',
    type: 'Museum',
    image: u('photo-1543783207-ec64e4d3c671', 560),
  },
];

// ─── Local Places & Landmarks ─────────────────────────────────────────────────

export const MOCK_LANDMARK_CATEGORIES: ExploreLandmarkCategory[] = [
  {
    id: 'lm-1',
    title: 'Beaches',
    image: u('photo-1507525428034-b723cf961d3e', 400),
  },
  {
    id: 'lm-2',
    title: 'Museums',
    image: u('photo-1543783207-ec64e4d3c671', 400),
  },
  {
    id: 'lm-3',
    title: 'Parks',
    image: u('photo-1519331379826-f10be5486c6f', 400),
  },
  {
    id: 'lm-4',
    title: 'Markets',
    image: u('photo-1488459716781-31db52582fe9', 400),
  },
];
