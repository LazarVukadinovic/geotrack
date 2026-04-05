import type { EventDetail } from '../models/event-detail.model';

const u = (id: string, w = 640) =>
  `https://images.unsplash.com/${id}?ixlib=rb-4.0.3&auto=format&fit=crop&w=${w}&q=75`;

export const MOCK_EVENT_DETAIL_MAP: Record<string, EventDetail> = {
  'ev-1': {
    id: 'ev-1',
    title: 'Summer Festival',
    location: 'Dubrovnik, Croatia',
    heroImage: u('photo-1514512364185-4c2b2db318b5', 800),
    tags: [
      { label: 'Music' },
      { label: 'Festival' },
      { label: 'Live music' },
      { label: 'Outdoor' },
    ],
    distance: '750m away',
    about:
      'Join us for an unforgettable evening under the stars. The Summer Festival brings together world-class music, vibrant coastal energy, and an exclusive beachfront atmosphere. Whether you\'re here to dance barefoot in the sand or sip cocktails by the stage, this is the ultimate summer celebration.',
    info: {
      date: '15 August 2024',
      timeRange: '18:00 – 23:30',
      venue: 'Banje Beach Club Main Stage',
      ticket: 'General Admission – €45',
      duration: '5.5 Hours',
      hostedBy: 'Dubrovnik Summer Fest',
    },
    venueInfo: {
      description:
        'Located right outside the Ploče Gate of the Old Town. The venue features a terraced layout dropping down to a pebbled beach.',
      arrivalTips: 'Use the eastern entrance near the Lazareti complex.',
      accessibility:
        'No on-site parking. Public transport recommended. Venue is partially wheelchair accessible.',
    },
    similar: [
      {
        id: 'ev-2',
        title: 'Full Moon Party',
        subtitle: 'Sveti Jakov Beach',
        image: u('photo-1507525428034-b723cf961d3e', 400),
        isSaved: false,
      },
      {
        id: 'ev-3',
        title: 'Acoustic Sunset',
        subtitle: 'Lapad Bay',
        image: u('photo-1464822759023-fed622ff2c3b', 400),
        isSaved: false,
      },
    ],
    isSaved: false,
  },

  'ev-2': {
    id: 'ev-2',
    title: 'Jazz Night',
    location: 'Dubrovnik, Croatia',
    heroImage: u('photo-1485872299829-c673f5194813', 800),
    tags: [{ label: 'Jazz' }, { label: 'Live music' }, { label: 'Night out' }],
    distance: '1.2 km away',
    about:
      'An intimate jazz evening at the legendary Sunset Stage in Old Town. World-renowned musicians gather to deliver a night of soulful rhythms and improvisational brilliance, set against the backdrop of Dubrovnik\'s illuminated city walls.',
    info: {
      date: 'Friday, 19 Jul 2024',
      timeRange: '19:30 – 23:00',
      venue: 'Sunset Stage · Old Town',
      ticket: 'Early Bird – €30',
      duration: '3.5 Hours',
      hostedBy: 'Dubrovnik Jazz Society',
    },
    venueInfo: {
      description:
        'Sunset Stage is perched above the Adriatic, offering panoramic views of the old city walls and the open sea.',
      arrivalTips: 'Enter via the Pile Gate and follow signs to the Sunset Stage venue.',
      accessibility: 'Limited accessibility. Steps required to reach the stage area.',
    },
    similar: [
      {
        id: 'ev-1',
        title: 'Summer Festival',
        subtitle: 'Banje Beach',
        image: u('photo-1514512364185-4c2b2db318b5', 400),
        isSaved: false,
      },
      {
        id: 'ev-3',
        title: 'Food Market',
        subtitle: 'Harbor · Local Bites',
        image: u('photo-1539571696357-5a69c17a67c6', 400),
        isSaved: false,
      },
    ],
    isSaved: false,
  },

  'ev-3': {
    id: 'ev-3',
    title: 'Food Market',
    location: 'Dubrovnik Harbor, Croatia',
    heroImage: u('photo-1539571696357-5a69c17a67c6', 800),
    tags: [{ label: 'Food' }, { label: 'Market' }, { label: 'Local' }, { label: 'Family' }],
    distance: '900m away',
    about:
      'Explore the vibrant flavors of Dalmatian cuisine at the Harbor Food Market. Local producers, artisan bakers, and renowned chefs come together for a morning of fresh seafood, handmade cheeses, seasonal produce, and traditional Croatian delicacies.',
    info: {
      date: 'Sunday, 21 Jul 2024',
      timeRange: '11:00 – 16:00',
      venue: 'Dubrovnik Harbor Promenade',
      ticket: 'Free Entry',
      duration: '5 Hours',
      hostedBy: 'Dubrovnik City Council',
    },
    venueInfo: {
      description:
        'The harbor market runs along the main waterfront promenade, with stunning views of the Adriatic and the island of Lokrum.',
      arrivalTips: 'Best accessed on foot from Old Town or by the Srđ cable car.',
      accessibility:
        'Fully accessible along the waterfront promenade. Wheelchair-friendly paths throughout.',
    },
    similar: [
      {
        id: 'ev-1',
        title: 'Summer Festival',
        subtitle: 'Banje Beach',
        image: u('photo-1514512364185-4c2b2db318b5', 400),
        isSaved: false,
      },
      {
        id: 'ev-2',
        title: 'Jazz Night',
        subtitle: 'Sunset Stage',
        image: u('photo-1485872299829-c673f5194813', 400),
        isSaved: false,
      },
    ],
    isSaved: false,
  },
};
