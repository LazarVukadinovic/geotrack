import type { EventListingItem, EventsFeatured } from '../models/events-listing.model';

const u = (id: string, w = 640) =>
  `https://images.unsplash.com/${id}?ixlib=rb-4.0.3&auto=format&fit=crop&w=${w}&q=75`;

/**
 * IDs ev-1, ev-2, ev-3 match the Explore events-slider and event-detail entries
 * so that a future unified service can merge them without ID conflicts.
 */
export const EVENTS_FEATURED: EventsFeatured = {
  title: 'Dubrovnik Summer Festival',
  description:
    'A spectacular celebration of music, theatre, and dance taking place in the historic heart of Dubrovnik — one of the Adriatic\'s longest-running summer arts events.',
  dateRange: 'Jul 10 – Aug 25, 2024',
  locationLine: 'Old Town · 1.2 km away',
};

export const MOCK_EVENTS_LISTING: EventListingItem[] = [
  {
    id: 'ev-1',
    title: 'Dubrovnik Summer Festival',
    subtitle: 'Festival',
    description:
      'World-class music, theatre, and dance in the open-air venues of the Old Town, from Lovrijenac Fortress to the Rector\'s Palace atrium.',
    location: 'Lovrijenac, Old Town',
    image: u('photo-1514512364185-4c2b2db318b5'),
    dateLabel: 'Jul 15 · 9:00 PM',
    tags: ['Festivals', 'Cultural', 'Outdoor', 'Live Music'],
    variant: 'large',
    isSaved: false,
  },
  {
    id: 'ev-2',
    title: 'Jazz Night',
    subtitle: 'Live Music',
    description:
      'An intimate evening of soulful jazz and improvisational brilliance at the legendary Sunset Stage, framed by the illuminated Old Town walls.',
    location: 'Sunset Stage · Old Town',
    image: u('photo-1485872299829-c673f5194813'),
    dateLabel: 'Fri · 7:30 PM',
    tags: ['Live Music', 'Nightlife'],
    variant: 'compact',
    isSaved: false,
  },
  {
    id: 'ev-3',
    title: 'Harbor Food Market',
    subtitle: 'Food & Market',
    description:
      'Local producers, artisan bakers, and renowned chefs along the Dubrovnik waterfront — fresh seafood, handmade cheeses, and Croatian delicacies.',
    location: 'Harbor Promenade, Dubrovnik',
    image: u('photo-1539571696357-5a69c17a67c6'),
    dateLabel: 'Sun · 11:00 AM',
    tags: ['Food & Market', 'Family', 'Outdoor'],
    variant: 'large',
    isSaved: false,
  },
  {
    id: 'ev-4',
    title: 'Theatre at Lovrijenac',
    subtitle: 'Cultural',
    description:
      'Shakespeare\'s works performed at the iconic Lovrijenac Fortress under the open sky — a theatrical tradition unique to Dubrovnik\'s summer calendar.',
    location: 'Lovrijenac Fortress, Dubrovnik',
    image: u('photo-1558618666-fcd25c85cd64'),
    dateLabel: 'Sat · 8:30 PM',
    tags: ['Cultural', 'Outdoor'],
    variant: 'compact',
    isSaved: false,
  },
  {
    id: 'ev-5',
    title: 'Acoustic Sunset',
    subtitle: 'Live Music',
    description:
      'Live acoustic sets at Lapad Bay as the sun dips below the horizon — an intimate and unhurried evening with local and regional musicians.',
    location: 'Lapad Bay, Dubrovnik',
    image: u('photo-1464822759023-fed622ff2c3b'),
    dateLabel: 'Thu · 6:00 PM',
    tags: ['Live Music', 'Outdoor'],
    variant: 'large',
    isSaved: false,
  },
  {
    id: 'ev-6',
    title: 'Open-Air Film Night',
    subtitle: 'Cultural',
    description:
      'Classic and contemporary films screened under the stars in the Old Town courtyard — a beloved Dubrovnik summer ritual for all ages.',
    location: 'Revelin Courtyard, Old Town',
    image: u('photo-1512070679279-8988d32161be'),
    dateLabel: 'Wed · 9:00 PM',
    tags: ['Cultural', 'Family', 'Outdoor'],
    variant: 'compact',
    isSaved: false,
  },
  {
    id: 'ev-7',
    title: 'Dalmatian Wine Evening',
    subtitle: 'Food & Market',
    description:
      'Sample the finest wines from Croatian vineyards paired with local charcuterie and cheeses — a guided tasting on the Old Town terrace.',
    location: 'Stradun Terrace, Old Town',
    image: u('photo-1510812431401-41d2bd2722f3'),
    dateLabel: 'Fri · 7:00 PM',
    tags: ['Food & Market', 'Nightlife'],
    variant: 'compact',
    isSaved: false,
  },
];
