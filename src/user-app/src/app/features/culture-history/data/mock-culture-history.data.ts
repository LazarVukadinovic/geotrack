import type { CultureHistoryItem } from '../models/culture-history.model';

const u = (id: string, w = 640) =>
  `https://images.unsplash.com/${id}?ixlib=rb-4.0.3&auto=format&fit=crop&w=${w}&q=75`;

/**
 * IDs attr-1 through attr-4 match the Explore must-see-slider entries so that
 * a future unified service can merge them without ID conflicts.
 */
export const MOCK_CULTURE_HISTORY: CultureHistoryItem[] = [
  {
    id: 'attr-1',
    title: 'Old Town District',
    subtitle: 'Historic Core',
    description:
      'Walk the medieval limestone streets of Dubrovnik\'s UNESCO World Heritage Old Town — a living monument to centuries of Ragusan Republic grandeur.',
    location: 'Old Town, Dubrovnik',
    image: u('photo-1558618666-fcd25c85cd64'),
    rating: 4.9,
    reviewCount: '18k',
    openingHours: 'Open all day',
    tags: ['Districts', 'Historic Sites'],
    variant: 'large',
    isSaved: false,
  },
  {
    id: 'attr-2',
    title: 'Maritime Museum',
    subtitle: 'Exhibitions',
    description:
      'Explore Dubrovnik\'s rich seafaring history through ship models, navigational instruments, and rare documents spanning five centuries of the Adriatic.',
    location: 'St John\'s Fortress, Old Town',
    image: u('photo-1580137189272-c9379f8864fd'),
    rating: 4.6,
    reviewCount: '2.3k',
    openingHours: 'Closes 6 PM',
    tags: ['Museums', 'Exhibitions'],
    variant: 'compact',
    isSaved: false,
  },
  {
    id: 'attr-3',
    title: 'Fort Lovrijenac',
    subtitle: 'Historic Fortress',
    description:
      'Rising 37 metres above the sea, this 11th-century fortress has guarded Dubrovnik for a millennium and starred as a famous TV filming location.',
    location: 'Outside Pile Gate, Dubrovnik',
    image: u('photo-1543783207-ec64e4d3c671'),
    rating: 4.8,
    reviewCount: '9.1k',
    openingHours: 'Closes 6 PM',
    tags: ['Landmarks', 'Historic Sites'],
    variant: 'large',
    isSaved: false,
  },
  {
    id: 'attr-4',
    title: 'Rector\'s Palace',
    subtitle: 'Museum',
    description:
      'Once the seat of the Ragusan Republic, this Gothic-Renaissance palace now houses a cultural history museum filled with period furnishings and portraits.',
    location: 'Pred Dvorom, Old Town',
    image: u('photo-1467269204594-9661b134dd2b'),
    rating: 4.7,
    reviewCount: '5.4k',
    openingHours: 'Closes 6 PM',
    tags: ['Museums', 'Historic Sites'],
    variant: 'compact',
    isSaved: false,
  },
  {
    id: 'ch-5',
    title: 'War Photo Limited',
    subtitle: 'Gallery',
    description:
      'A moving photojournalism gallery showcasing the human cost of conflict through powerful images by world-renowned documentary photographers.',
    location: 'Antuninska, Old Town',
    image: u('photo-1506905925346-21bda4d32df4'),
    rating: 4.8,
    reviewCount: '1.6k',
    openingHours: 'Closes 9 PM',
    tags: ['Galleries', 'Exhibitions'],
    variant: 'large',
    isSaved: false,
  },
  {
    id: 'ch-6',
    title: 'Dubrovnik Cathedral',
    subtitle: 'Baroque Landmark',
    description:
      'The Cathedral of the Assumption dominates Gundulic Square, its Treasury safeguarding relics of St Blaise and five centuries of Ragusan religious art.',
    location: 'Gundulic Square, Old Town',
    image: u('photo-1548681528-6a5c45b66063'),
    rating: 4.6,
    reviewCount: '7.2k',
    openingHours: 'Closes 5 PM',
    tags: ['Landmarks', 'Historic Sites'],
    variant: 'compact',
    isSaved: false,
  },
  {
    id: 'ch-7',
    title: 'City Walls',
    subtitle: 'Medieval Fortifications',
    description:
      'Walk the full 1.9 km circuit atop Dubrovnik\'s legendary medieval walls for panoramic views of terracotta rooftops, open sea, and surrounding islands.',
    location: 'Old Town, Dubrovnik',
    image: u('photo-1499856871958-5b9627545d1a'),
    rating: 4.9,
    reviewCount: '22k',
    openingHours: 'Closes 7:30 PM',
    tags: ['Landmarks', 'Historic Sites'],
    variant: 'large',
    isSaved: false,
  },
];
