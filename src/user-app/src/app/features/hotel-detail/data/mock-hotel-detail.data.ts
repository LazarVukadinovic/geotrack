import type { HotelDetail, HotelReview } from '../models/hotel-detail.model';

const u = (id: string, w = 800) =>
  `https://images.unsplash.com/${id}?ixlib=rb-4.0.3&auto=format&fit=crop&w=${w}&q=80`;

// ─── Shared reviewer personas ─────────────────────────────────────────────────

const REVIEW_MARKO: HotelReview = {
  id: 'r-marko',
  authorName: 'Marko Petrović',
  avatarUrl: 'https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=80&q=75&fit=crop&auto=format',
  rating: 5.0,
  timeAgo: '2 days ago',
  body: 'Absolutely breathtaking views! The water is pristine and the service at the restaurant is top-notch. It gets busy, so arriving early is highly recommended.',
  helpfulCount: 24,
};

const REVIEW_ELENA: HotelReview = {
  id: 'r-elena',
  authorName: 'Elena Rossi',
  avatarUrl: 'https://images.unsplash.com/photo-1494790108377-be9c29b29330?w=80&q=75&fit=crop&auto=format',
  rating: 4.5,
  timeAgo: '1 week ago',
  body: 'Great spot to relax after walking the city walls. The pebble beach is nice, but renting a lounge chair makes it much more comfortable.',
  helpfulCount: 12,
};

// ─── Hotel detail entries keyed by explore-page stay IDs ─────────────────────

export const MOCK_HOTEL_DETAIL_MAP: Record<string, HotelDetail> = {

  'stay-1': {
    id: 'stay-1',
    title: 'Grand Hotel Park',
    location: 'Dubrovnik Riviera, Croatia',
    heroImage: u('photo-1566073771259-6a8506099945'),
    tags: [{ label: 'Hotel' }, { label: 'Luxury' }, { label: 'Spa' }, { label: 'Sea View' }],
    rating: 4.9,
    reviewCount: '1.2k',
    distance: '0.2 km from center',
    pricePerNight: 121,
    about:
      'Grand Hotel Park is a landmark of Dubrovnik hospitality, perched above the Adriatic with commanding views of the old walled city. Impeccably appointed rooms, a world-class spa, and multiple dining options make it the perfect base for exploring the pearl of the Adriatic.',
    bookingPartners: [
      { id: 'booking', name: 'Booking.com', variant: 'dark' },
      { id: 'airbnb', name: 'Airbnb', variant: 'brand' },
      { id: 'hotel-site', name: 'Hotel site', variant: 'muted' },
    ],
    amenities: [
      { id: 'pool', label: 'Pool', icon: 'pool' },
      { id: 'wifi', label: 'Free Wifi', icon: 'wifi' },
      { id: 'breakfast', label: 'Breakfast', icon: 'breakfast' },
      { id: 'parking', label: 'Parking', icon: 'parking' },
    ],
    reviews: [REVIEW_MARKO, REVIEW_ELENA],
    similar: [
      { id: 'stay-2', title: 'Grand Hotel 2', image: u('photo-1520250497591-112f2f40a3f4', 400), rating: 4.8, distance: '0.4 km', isSaved: false },
      { id: 'stay-3', title: 'Villa Adriatic', image: u('photo-1582719508461-905c673771fd', 400), rating: 4.7, distance: '0.9 km', isSaved: false },
    ],
    isSaved: false,
  },

  'stay-2': {
    id: 'stay-2',
    title: 'Hotel Excelsior',
    location: 'Old Town, Dubrovnik',
    heroImage: u('photo-1520250497591-112f2f40a3f4'),
    tags: [{ label: 'Hotel' }, { label: 'Boutique' }, { label: 'Family Friendly' }],
    rating: 4.8,
    reviewCount: '876',
    distance: '0.4 km from center',
    pricePerNight: 122,
    about:
      'Hotel Excelsior is a cherished institution on the Dalmatian coast, combining old-world elegance with modern comforts. Set steps from the Old Town entrance, it offers seamless access to the city walls, with a private beach and a rooftop terrace that captures the soul of Dubrovnik.',
    bookingPartners: [
      { id: 'booking', name: 'Booking.com', variant: 'dark' },
      { id: 'airbnb', name: 'Airbnb', variant: 'brand' },
      { id: 'hotel-site', name: 'Hotel site', variant: 'muted' },
    ],
    amenities: [
      { id: 'pool', label: 'Pool', icon: 'pool' },
      { id: 'wifi', label: 'Free Wifi', icon: 'wifi' },
      { id: 'restaurant', label: 'Restaurant', icon: 'restaurant' },
      { id: 'spa', label: 'Spa', icon: 'spa' },
    ],
    reviews: [REVIEW_ELENA, REVIEW_MARKO],
    similar: [
      { id: 'stay-1', title: 'Grand Hotel Park', image: u('photo-1566073771259-6a8506099945', 400), rating: 4.9, distance: '0.2 km', isSaved: false },
      { id: 'stay-3', title: 'Villa Adriatic', image: u('photo-1582719508461-905c673771fd', 400), rating: 4.7, distance: '0.7 km', isSaved: false },
    ],
    isSaved: false,
  },

  'stay-3': {
    id: 'stay-3',
    title: 'Villa Adriatica Luxury Resort',
    location: 'Dubrovnik Riviera, Croatia',
    heroImage: u('photo-1582719508461-905c673771fd'),
    tags: [{ label: 'Hotel' }, { label: 'Luxury' }, { label: 'Spa' }, { label: 'Family Friendly' }],
    rating: 4.9,
    reviewCount: '342',
    distance: '2.5 km from center',
    pricePerNight: 98,
    about:
      'Perched on a cliff overlooking the Adriatic Sea, Villa Adriatica offers an unparalleled luxury experience. Enjoy breathtaking sunset views, exclusive private beach access, and world-class dining just minutes from the historic Old Town.',
    bookingPartners: [
      { id: 'booking', name: 'Booking.com', variant: 'dark' },
      { id: 'airbnb', name: 'Airbnb', variant: 'brand' },
      { id: 'hotel-site', name: 'Hotel site', variant: 'muted' },
    ],
    amenities: [
      { id: 'pool', label: 'Pool', icon: 'pool' },
      { id: 'wifi', label: 'Free Wifi', icon: 'wifi' },
      { id: 'breakfast', label: 'Breakfast', icon: 'breakfast' },
      { id: 'parking', label: 'Parking', icon: 'parking' },
    ],
    reviews: [REVIEW_MARKO, REVIEW_ELENA],
    similar: [
      { id: 'stay-1', title: 'Hotel Bellevue', image: u('photo-1566073771259-6a8506099945', 400), rating: 4.8, distance: '1.2 km', isSaved: false },
      { id: 'stay-2', title: 'Excelsior Hotel', image: u('photo-1520250497591-112f2f40a3f4', 400), rating: 4.9, distance: '3.5 km', isSaved: false },
    ],
    isSaved: false,
  },

};
