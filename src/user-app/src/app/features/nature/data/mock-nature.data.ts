import type { NatureItem } from '../models/nature.model';

const u = (id: string, w = 640) =>
  `https://images.unsplash.com/${id}?ixlib=rb-4.0.3&auto=format&fit=crop&w=${w}&q=75`;

/**
 * IDs nb-4 and nb-5 match the Explore popular-nearby-slider entries so that
 * a future unified service can merge them without ID conflicts.
 */
export const MOCK_NATURE: NatureItem[] = [
  {
    id: 'nb-4',
    title: 'Lokrum Island',
    subtitle: 'Nature Reserve',
    description:
      'A lush island nature reserve just minutes from Dubrovnik\'s Old Port, home to peacocks, botanical gardens, a saltwater lake, and a medieval Benedictine monastery.',
    location: 'Lokrum, Dubrovnik',
    image: u('photo-1506905925346-21bda4d32df4'),
    rating: 4.6,
    reviewCount: '6k',
    distance: '1.8 km away',
    tags: ['Islands', 'Nature Reserves'],
    variant: 'large',
    isSaved: false,
  },
  {
    id: 'nb-5',
    title: 'Mljet National Park',
    subtitle: 'Pristine Lakes',
    description:
      'Two interconnected saltwater lakes inside a dense pine forest on Croatia\'s most forested island — perfect for kayaking, cycling, and untouched Adriatic swimming.',
    location: 'Mljet Island, Croatia',
    image: u('photo-1467269204594-9661b134dd2b'),
    rating: 4.8,
    reviewCount: '4.1k',
    distance: '2.4 km away',
    tags: ['Parks', 'Nature Reserves'],
    variant: 'compact',
    isSaved: false,
  },
  {
    id: 'nb-2',
    title: 'Banje Beach',
    subtitle: 'Beach',
    description:
      'The most celebrated beach in Dubrovnik with crystal-clear Adriatic water, a backdrop of the Old Town walls, and views of Lokrum Island from the shoreline.',
    location: 'Ploče, Dubrovnik',
    image: u('photo-1507525428034-b723cf961d3e'),
    rating: 4.7,
    reviewCount: '8.5k',
    distance: '1.2 km away',
    tags: ['Beaches', 'Scenic Views'],
    variant: 'large',
    isSaved: false,
  },
  {
    id: 'nat-4',
    title: 'Mount Srđ',
    subtitle: 'Panorama',
    description:
      'Ride the cable car or hike the Napoleon Road to the summit for the most sweeping panoramic views of Dubrovnik\'s terracotta rooftops, islands, and open Adriatic.',
    location: 'Srđ, Dubrovnik',
    image: u('photo-1464822759023-fed622ff2c3b'),
    rating: 4.9,
    reviewCount: '11k',
    distance: '3.1 km away',
    tags: ['Mountains', 'Hiking', 'Scenic Views'],
    variant: 'compact',
    isSaved: false,
  },
  {
    id: 'nat-5',
    title: 'Trsteno Arboretum',
    subtitle: 'Historic Gardens',
    description:
      'One of Europe\'s oldest arboretums, dating to the 15th century — a tranquil coastal garden of ancient plane trees, grottoes, and sea-view terraces north of Dubrovnik.',
    location: 'Trsteno, Croatia',
    image: u('photo-1519331379826-f10be5486c6f'),
    rating: 4.5,
    reviewCount: '2.8k',
    distance: '15 km away',
    tags: ['Parks', 'Scenic Views'],
    variant: 'large',
    isSaved: false,
  },
  {
    id: 'nat-6',
    title: 'Konavle Valley',
    subtitle: 'Rural Landscape',
    description:
      'A fertile valley stretching south from Dubrovnik, threaded by the Ljuta river and ringed by hills — ideal for cycling, birdwatching, and traditional agritourism escapes.',
    location: 'Konavle, Dubrovnik',
    image: u('photo-1558618047-3c8c76ca7d13'),
    rating: 4.4,
    reviewCount: '1.2k',
    distance: '22 km away',
    tags: ['Hiking', 'Scenic Views'],
    variant: 'compact',
    isSaved: false,
  },
  {
    id: 'nat-7',
    title: 'Elafiti Islands',
    subtitle: 'Island Chain',
    description:
      'A serene archipelago of three car-free islands — Šipan, Lopud, and Koločep — offering secluded sandy coves, pine forests, and a peaceful escape from the mainland.',
    location: 'Elafiti Archipelago, Croatia',
    image: u('photo-1559827260-dc66d52bef19'),
    rating: 4.7,
    reviewCount: '3.3k',
    distance: '12 km away',
    tags: ['Islands', 'Beaches', 'Scenic Views'],
    variant: 'large',
    isSaved: false,
  },
];
