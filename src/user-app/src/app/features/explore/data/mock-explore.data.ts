import type { ExploreItem } from '../../../core/models/explore-item.model';
import type { ExploreEvent, ExplorePromoCard, NearbyStatus } from '../models/explore-extras.model';

/** Stable Unsplash URLs (format + moderate width) to reduce hotlink / 404 issues. */
const u = (id: string, w = 640) =>
  `https://images.unsplash.com/${id}?ixlib=rb-4.0.3&auto=format&fit=crop&w=${w}&q=75`;

export const MOCK_RECOMMENDED_ITEMS: ExploreItem[] = [
  {
    id: 'rec-1',
    title: 'City Tour',
    subtitle: 'Stunning views, historic streets.',
    description: 'Guided walk through Old Town walls and limestone lanes.',
    category: 'attraction',
    type: 'attraction',
    location: 'Old Town',
    image: u('photo-1526481280695-3c469172d3b8'),
    rating: 4.8,
    isSaved: false,
    openingHours: '8 AM – 6 PM',
    isRecommended: true,
  },
  {
    id: 'rec-2',
    title: 'Harbor Sunset',
    subtitle: 'Waterfront dining & golden hour.',
    description: 'Best tables for seafood with Adriatic views.',
    category: 'restaurant',
    type: 'restaurant',
    location: 'Port',
    image: u('photo-1507525428034-b723cf961d3e'),
    rating: 4.7,
    isSaved: false,
    openingHours: '12 PM – 11 PM',
    isRecommended: true,
  },
  {
    id: 'rec-3',
    title: 'Boutique Stay',
    subtitle: 'Rooftop pool, quiet nights.',
    description: 'Boutique hotel steps from Stradun.',
    category: 'hotel',
    type: 'hotel',
    location: 'Ploče',
    image: u('photo-1566073771259-6a8506099945'),
    rating: 4.9,
    isSaved: false,
    isRecommended: true,
  },
  {
    id: 'rec-4',
    title: 'Sea Kayaking',
    subtitle: 'Crystal coves & hidden caves.',
    description: 'Half-day paddle along Lokrum and cliffs.',
    category: 'activity',
    type: 'activity',
    location: 'Pile Bay',
    image: u('photo-1540946485063-a40040818724'),
    rating: 4.8,
    isSaved: false,
    openingHours: '9 AM – 4 PM',
    isRecommended: true,
  },
];

export const MOCK_LOCAL_FAVORITES: ExploreItem[] = [
  {
    id: 'loc-1',
    title: 'Lago di Braies',
    subtitle: 'South Tyrol',
    description: 'Alpine lake framed by pine and peaks.',
    category: 'attraction',
    type: 'attraction',
    location: 'South Tyrol',
    image: u('photo-1506905925346-21bda4d32df4', 560),
    rating: 4.9,
    isSaved: false,
    openingHours: '8 AM – 6 PM',
    isRecommended: false,
  },
  {
    id: 'loc-2',
    title: 'Paris',
    subtitle: 'Top Choice',
    description: 'Evening lights along the Seine.',
    category: 'attraction',
    type: 'attraction',
    location: 'Île de la Cité',
    image: u('photo-1502602898657-3e91760cbb34', 560),
    rating: 4.8,
    isSaved: false,
    openingHours: 'Always Open',
    isRecommended: false,
  },
  {
    id: 'loc-3',
    title: 'Coastal Tavern',
    subtitle: 'Dubrovnik',
    description: 'Grilled catch and local wine.',
    category: 'restaurant',
    type: 'restaurant',
    location: 'Lapad',
    image: u('photo-1517248135467-4c7edcad34c4', 560),
    rating: 4.7,
    isSaved: false,
    openingHours: '5 PM – 12 AM',
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

export const MOCK_NEARBY_STATUS: NearbyStatus = {
  label: 'Near',
  value: 'Old Town · 2.5 km',
};

export const MOCK_PROMO_CARD: ExplorePromoCard = {
  title: 'Personalize your stay',
  message: 'Sign up to get tailored local tips and smart recommendations.',
  ctaLabel: 'Sign up',
};

export const MOCK_EVENTS_THIS_WEEK: ExploreEvent[] = [
  {
    id: 'ev-1',
    title: 'Jazz Night',
    subtitle: 'Sunset stage · Old Town',
    dateLabel: 'Fri · 7:30 PM',
    image: u('photo-1485872299829-c673f5194813', 560),
  },
  {
    id: 'ev-2',
    title: 'Summer Festival',
    subtitle: 'City walls · Live',
    dateLabel: 'Sat · 9:00 PM',
    image: u('photo-1520975958225-9a3d0f3b7d2a', 560),
  },
  {
    id: 'ev-3',
    title: 'Food Market',
    subtitle: 'Local bites · Harbor',
    dateLabel: 'Sun · 11:00 AM',
    image: u('photo-1514512364185-4c2b2db318b5', 560),
  },
];
