import type { PlaceDetail, PlaceReview } from '../../../core/models/place.model';

const u = (id: string, w = 800) =>
  `https://images.unsplash.com/${id}?ixlib=rb-4.0.3&auto=format&fit=crop&w=${w}&q=80`;

// ─── Shared reviewer personas ─────────────────────────────────────────────────

const REVIEW_MARKO: PlaceReview = {
  id: 'r-marko',
  authorName: 'Marko Petrović',
  avatarUrl: 'https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=80&q=75&fit=crop&auto=format',
  rating: 5.0,
  timeAgo: '2 days ago',
  body: 'Absolutely breathtaking views! The water is pristine and the service at the restaurant is top-notch. It gets busy, so arriving early is highly recommended.',
  helpfulCount: 24,
};

const REVIEW_ELENA: PlaceReview = {
  id: 'r-elena',
  authorName: 'Elena Rossi',
  avatarUrl: 'https://images.unsplash.com/photo-1494790108377-be9c29b29330?w=80&q=75&fit=crop&auto=format',
  rating: 4.5,
  timeAgo: '1 week ago',
  body: 'Great spot to relax after walking the city walls. The pebble beach is nice, but renting a lounge chair makes it much more comfortable.',
  helpfulCount: 12,
};

// ─── Place detail entries keyed by explore-page IDs ──────────────────────────

export const MOCK_PLACE_DETAIL_MAP: Record<string, PlaceDetail> = {

  // ── Popular Nearby ──────────────────────────────────────────────────────────

  'nb-1': {
    id: 'nb-1',
    title: 'Old Town Walls',
    location: 'Dubrovnik, Croatia',
    heroImage: u('photo-1558618666-fcd25c85cd64'),
    tags: [{ label: 'Tvrđava' }, { label: 'Historija' }, { label: 'Atrakcija' }],
    rating: 4.9,
    reviewCount: '12k',
    distance: '500m away',
    openStatus: 'Open now',
    about: 'The Dubrovnik city walls are one of the most famous and best-preserved medieval city walls in the world. Walking the full circuit offers breathtaking views of the Adriatic Sea, terracotta rooftops, and the lush green of the old city below.',
    reviews: [REVIEW_MARKO, REVIEW_ELENA],
    similar: [
      { id: 'nb-2', title: 'Banje Beach', image: u('photo-1507525428034-b723cf961d3e', 400), rating: 4.7, distance: '1.2 km', isSaved: false },
      { id: 'attr-1', title: 'Fort Lovrijenac', image: u('photo-1558618666-fcd25c85cd64', 400), rating: 4.8, distance: '0.3 km', isSaved: false },
      { id: 'nb-3', title: 'Stradun', image: u('photo-1526481280695-3c469172d3b8', 400), rating: 4.8, distance: '0.3 km', isSaved: false },
    ],
    isSaved: false,
    contentType: 'place',
    category: 'attraction',
  },

  'nb-2': {
    id: 'nb-2',
    title: 'Banje Beach',
    location: 'Dubrovnik, Croatia',
    heroImage: u('photo-1507525428034-b723cf961d3e'),
    tags: [{ label: 'Plaža' }, { label: 'Priroda' }, { label: 'Plivanje' }],
    rating: 4.7,
    reviewCount: '8.5k',
    distance: '1.2 km away',
    openStatus: 'Open now',
    about: 'Known as one of the most beautiful beaches in the world, Banje offers stunning views of the Old Town and Lokrum Island. Enjoy crystal-clear waters, rent a cabana, or dine at the exclusive beachfront restaurant. Perfect for both relaxation and vibrant coastal nightlife.',
    reviews: [REVIEW_MARKO, REVIEW_ELENA],
    similar: [
      { id: 'nb-4', title: 'Lokrum Island', image: u('photo-1506905925346-21bda4d32df4', 400), rating: 4.6, distance: '1.8 km', isSaved: false },
      { id: 'nb-1', title: 'Old Town Walls', image: u('photo-1558618666-fcd25c85cd64', 400), rating: 4.9, distance: '0.8 km', isSaved: false },
      { id: 'attr-1', title: 'Fort Lovrijenac', image: u('photo-1558618666-fcd25c85cd64', 400), rating: 4.8, distance: '1.5 km', isSaved: false },
    ],
    isSaved: false,
    contentType: 'place',
    category: 'beach',
  },

  'nb-3': {
    id: 'nb-3',
    title: 'Stradun',
    location: 'Dubrovnik, Croatia',
    heroImage: u('photo-1526481280695-3c469172d3b8'),
    tags: [{ label: 'Landmark' }, { label: 'Historija' }, { label: 'Šetnja' }],
    rating: 4.8,
    reviewCount: '20k',
    distance: '300m away',
    openStatus: 'Open 24h',
    about: 'Stradun, also known as Placa, is the main street of Dubrovnik. This 300-metre long pedestrian boulevard is lined with baroque-style buildings, restaurants, and charming shops. The perfect place for an evening passeggiata.',
    reviews: [REVIEW_ELENA, REVIEW_MARKO],
    similar: [
      { id: 'nb-1', title: 'Old Town Walls', image: u('photo-1558618666-fcd25c85cd64', 400), rating: 4.9, distance: '0.1 km', isSaved: false },
      { id: 'attr-4', title: "Rector's Palace", image: u('photo-1543783207-ec64e4d3c671', 400), rating: 4.7, distance: '0.1 km', isSaved: false },
      { id: 'nb-2', title: 'Banje Beach', image: u('photo-1507525428034-b723cf961d3e', 400), rating: 4.7, distance: '1.5 km', isSaved: false },
    ],
    isSaved: false,
    contentType: 'place',
    category: 'landmark',
  },

  'nb-4': {
    id: 'nb-4',
    title: 'Lokrum Island',
    location: 'Dubrovnik, Croatia',
    heroImage: u('photo-1506905925346-21bda4d32df4'),
    tags: [{ label: 'Priroda' }, { label: 'Otok' }, { label: 'Rezervat' }],
    rating: 4.6,
    reviewCount: '6k',
    distance: '1.8 km away',
    openStatus: 'Open now',
    about: 'A short ferry ride from Dubrovnik, Lokrum is a protected nature reserve full of lush forests, roaming peacocks, saltwater lakes, and historic ruins. An ideal escape from the city bustle.',
    reviews: [REVIEW_MARKO, REVIEW_ELENA],
    similar: [
      { id: 'nb-2', title: 'Banje Beach', image: u('photo-1507525428034-b723cf961d3e', 400), rating: 4.7, distance: '2.0 km', isSaved: false },
      { id: 'loc-4', title: 'Sunrise Hike', image: u('photo-1464822759023-fed622ff2c3b', 400), rating: 4.9, distance: '2.5 km', isSaved: false },
      { id: 'attr-3', title: 'Lokrum Island', image: u('photo-1467269204594-9661b134dd2b', 400), rating: 4.7, distance: '1.8 km', isSaved: false },
    ],
    isSaved: false,
    contentType: 'place',
    category: 'nature',
  },

  // ── Local Favorites ─────────────────────────────────────────────────────────

  'loc-1': {
    id: 'loc-1',
    title: 'Buža Bar',
    location: 'Old Town, Dubrovnik',
    heroImage: u('photo-1485872299829-c673f5194813'),
    tags: [{ label: 'Bar' }, { label: 'Pogled' }, { label: 'Klif' }],
    rating: 4.8,
    reviewCount: '3.2k',
    distance: '0.4 km away',
    openStatus: 'Closes 12 AM',
    about: 'Buža Bar is carved into the ancient city walls, with rustic seating on the rocks overlooking the open Adriatic. No Wi-Fi, no distractions — just cold drinks, warm sunsets, and the sound of the sea below.',
    reviews: [REVIEW_MARKO, REVIEW_ELENA],
    similar: [
      { id: 'loc-2', title: 'Nautika Restaurant', image: u('photo-1517248135467-4c7edcad34c4', 400), rating: 4.9, distance: '0.2 km', isSaved: false },
      { id: 'nb-1', title: 'Old Town Walls', image: u('photo-1558618666-fcd25c85cd64', 400), rating: 4.9, distance: '0.1 km', isSaved: false },
      { id: 'nb-3', title: 'Stradun', image: u('photo-1526481280695-3c469172d3b8', 400), rating: 4.8, distance: '0.3 km', isSaved: false },
    ],
    isSaved: false,
    contentType: 'place',
    category: 'bar',
  },

  'loc-2': {
    id: 'loc-2',
    title: 'Nautika Restaurant',
    location: 'Pile Gate, Dubrovnik',
    heroImage: u('photo-1517248135467-4c7edcad34c4'),
    tags: [{ label: 'Restoran' }, { label: 'Fine Dining' }, { label: 'More' }],
    rating: 4.9,
    reviewCount: '1.8k',
    distance: '0.6 km away',
    openStatus: 'Closes 11 PM',
    about: "Nautika is Dubrovnik's most celebrated fine-dining restaurant, set outside the Pile Gate with an iconic view of Fort Lovrijenac and the sea. Serving fresh Adriatic seafood and award-winning Dalmatian cuisine.",
    reviews: [REVIEW_ELENA, REVIEW_MARKO],
    similar: [
      { id: 'loc-1', title: 'Buža Bar', image: u('photo-1485872299829-c673f5194813', 400), rating: 4.8, distance: '0.2 km', isSaved: false },
      { id: 'attr-1', title: 'Fort Lovrijenac', image: u('photo-1558618666-fcd25c85cd64', 400), rating: 4.8, distance: '0.1 km', isSaved: false },
      { id: 'nb-1', title: 'Old Town Walls', image: u('photo-1558618666-fcd25c85cd64', 400), rating: 4.9, distance: '0.3 km', isSaved: false },
    ],
    isSaved: false,
    contentType: 'place',
    category: 'restaurant',
  },

  'loc-3': {
    id: 'loc-3',
    title: 'Cogito',
    location: 'Stradun, Dubrovnik',
    heroImage: u('photo-1509042239860-f550ce710b93'),
    tags: [{ label: 'Kafić' }, { label: 'Specialty Coffee' }, { label: 'Kava' }],
    rating: 4.7,
    reviewCount: '890',
    distance: '0.3 km away',
    openStatus: 'Closes 9 PM',
    about: 'Cogito Coffee is a specialty roaster with one of the finest espresso bars in the Adriatic. Cozy corners, single-origin brews, and a relaxed atmosphere make it the perfect midday retreat on the Stradun.',
    reviews: [REVIEW_MARKO, REVIEW_ELENA],
    similar: [
      { id: 'loc-2', title: 'Nautika Restaurant', image: u('photo-1517248135467-4c7edcad34c4', 400), rating: 4.9, distance: '0.5 km', isSaved: false },
      { id: 'nb-3', title: 'Stradun', image: u('photo-1526481280695-3c469172d3b8', 400), rating: 4.8, distance: '0.1 km', isSaved: false },
      { id: 'loc-1', title: 'Buža Bar', image: u('photo-1485872299829-c673f5194813', 400), rating: 4.8, distance: '0.4 km', isSaved: false },
    ],
    isSaved: false,
    contentType: 'place',
    category: 'cafe',
  },

  'loc-4': {
    id: 'loc-4',
    title: 'Sunrise Hike',
    location: 'Mount Srđ, Dubrovnik',
    heroImage: u('photo-1464822759023-fed622ff2c3b'),
    tags: [{ label: 'Aktivnost' }, { label: 'Planinarenje' }, { label: 'Pogled' }],
    rating: 4.9,
    reviewCount: '2.1k',
    distance: '3.2 km away',
    openStatus: '6 AM – 8 PM',
    about: 'The trail up Mount Srđ rewards hikers with the most spectacular panoramic views of Dubrovnik and the surrounding islands. Go early for a sunrise experience or pair it with a cable car ride back down.',
    reviews: [REVIEW_MARKO, REVIEW_ELENA],
    similar: [
      { id: 'attr-2', title: 'Dubrovnik Cable Car', image: u('photo-1580137189272-c9379f8864fd', 400), rating: 4.8, distance: '3.0 km', isSaved: false },
      { id: 'nb-4', title: 'Lokrum Island', image: u('photo-1506905925346-21bda4d32df4', 400), rating: 4.6, distance: '5.0 km', isSaved: false },
      { id: 'nb-1', title: 'Old Town Walls', image: u('photo-1558618666-fcd25c85cd64', 400), rating: 4.9, distance: '2.8 km', isSaved: false },
    ],
    isSaved: false,
    contentType: 'place',
    category: 'activity',
  },

  // ── Must-See Attractions ─────────────────────────────────────────────────────

  'attr-1': {
    id: 'attr-1',
    title: 'Fort Lovrijenac',
    location: 'Dubrovnik, Croatia',
    heroImage: u('photo-1558618666-fcd25c85cd64'),
    tags: [{ label: 'Tvrđava' }, { label: 'Historija' }, { label: 'Pogled' }],
    rating: 4.8,
    reviewCount: '5.5k',
    distance: '600m away',
    openStatus: 'Open now',
    about: 'Known as the "Gibraltar of Dubrovnik," Fort Lovrijenac stands on a sheer 37-metre cliff just outside the western walls. It offers some of the most dramatic views of the walled city and the open sea.',
    reviews: [REVIEW_ELENA, REVIEW_MARKO],
    similar: [
      { id: 'nb-1', title: 'Old Town Walls', image: u('photo-1558618666-fcd25c85cd64', 400), rating: 4.9, distance: '0.3 km', isSaved: false },
      { id: 'attr-4', title: "Rector's Palace", image: u('photo-1543783207-ec64e4d3c671', 400), rating: 4.7, distance: '0.4 km', isSaved: false },
      { id: 'nb-2', title: 'Banje Beach', image: u('photo-1507525428034-b723cf961d3e', 400), rating: 4.7, distance: '1.0 km', isSaved: false },
    ],
    isSaved: false,
    contentType: 'place',
    category: 'attraction',
  },

  'attr-2': {
    id: 'attr-2',
    title: 'Dubrovnik Cable Car',
    location: 'Mount Srđ, Dubrovnik',
    heroImage: u('photo-1580137189272-c9379f8864fd'),
    tags: [{ label: 'Panorama' }, { label: 'Žičara' }, { label: 'Pogled' }],
    rating: 4.8,
    reviewCount: '7k',
    distance: '3.0 km away',
    openStatus: 'Open now',
    about: "The Dubrovnik Cable Car whisks you from the base of the old city to the top of Mount Srđ in just four minutes, offering a breathtaking bird's-eye view of the entire coastline and islands.",
    reviews: [REVIEW_MARKO, REVIEW_ELENA],
    similar: [
      { id: 'loc-4', title: 'Sunrise Hike', image: u('photo-1464822759023-fed622ff2c3b', 400), rating: 4.9, distance: '0.2 km', isSaved: false },
      { id: 'nb-4', title: 'Lokrum Island', image: u('photo-1506905925346-21bda4d32df4', 400), rating: 4.6, distance: '3.5 km', isSaved: false },
      { id: 'nb-1', title: 'Old Town Walls', image: u('photo-1558618666-fcd25c85cd64', 400), rating: 4.9, distance: '3.0 km', isSaved: false },
    ],
    isSaved: false,
    contentType: 'place',
    category: 'attraction',
  },

  'attr-3': {
    id: 'attr-3',
    title: 'Lokrum Island',
    location: 'Dubrovnik, Croatia',
    heroImage: u('photo-1467269204594-9661b134dd2b'),
    tags: [{ label: 'Rezervat' }, { label: 'Priroda' }, { label: 'Otok' }],
    rating: 4.7,
    reviewCount: '4.3k',
    distance: '1.8 km away',
    openStatus: 'Open now',
    about: 'Lokrum Island is a protected nature reserve just a short ferry ride from the Old Town. Home to exotic peacocks, botanical gardens, saltwater lakes, and a medieval monastery ruin.',
    reviews: [REVIEW_ELENA, REVIEW_MARKO],
    similar: [
      { id: 'nb-2', title: 'Banje Beach', image: u('photo-1507525428034-b723cf961d3e', 400), rating: 4.7, distance: '2.0 km', isSaved: false },
      { id: 'loc-4', title: 'Sunrise Hike', image: u('photo-1464822759023-fed622ff2c3b', 400), rating: 4.9, distance: '4.0 km', isSaved: false },
      { id: 'attr-2', title: 'Dubrovnik Cable Car', image: u('photo-1580137189272-c9379f8864fd', 400), rating: 4.8, distance: '3.5 km', isSaved: false },
    ],
    isSaved: false,
    contentType: 'place',
    category: 'nature',
  },

  'attr-4': {
    id: 'attr-4',
    title: "Rector's Palace",
    location: 'Old Town, Dubrovnik',
    heroImage: u('photo-1543783207-ec64e4d3c671'),
    tags: [{ label: 'Muzej' }, { label: 'Historija' }, { label: 'Kultura' }],
    rating: 4.7,
    reviewCount: '3.8k',
    distance: '200m away',
    openStatus: 'Open now',
    about: "The Rector's Palace is a magnificent example of late Gothic-Renaissance architecture, housing a museum with artifacts from the era of the Dubrovnik Republic. One of the finest historic buildings on the entire Adriatic coast.",
    reviews: [REVIEW_ELENA, REVIEW_MARKO],
    similar: [
      { id: 'nb-1', title: 'Old Town Walls', image: u('photo-1558618666-fcd25c85cd64', 400), rating: 4.9, distance: '0.1 km', isSaved: false },
      { id: 'nb-3', title: 'Stradun', image: u('photo-1526481280695-3c469172d3b8', 400), rating: 4.8, distance: '0.1 km', isSaved: false },
      { id: 'attr-1', title: 'Fort Lovrijenac', image: u('photo-1558618666-fcd25c85cd64', 400), rating: 4.8, distance: '0.4 km', isSaved: false },
    ],
    isSaved: false,
    contentType: 'place',
    category: 'museum',
  },

};
