import type { LocalPlacesCategory } from '../models/local-places.model';

const u = (id: string, w = 400) =>
  `https://images.unsplash.com/${id}?ixlib=rb-4.0.3&auto=format&fit=crop&w=${w}&q=75`;

/**
 * IDs lm-1 through lm-4 match the Explore landmarks-slider entries.
 * categoryKey is used to navigate to /local-places/:categoryKey.
 */
export const MOCK_LOCAL_PLACES_CATEGORIES: LocalPlacesCategory[] = [
  {
    id: 'lm-1',
    title: 'Beaches',
    image: u('photo-1507525428034-b723cf961d3e'),
    categoryKey: 'beaches',
    subtitle: 'Explore beach spots and coastal places',
  },
  {
    id: 'lm-2',
    title: 'Museums',
    image: u('photo-1543783207-ec64e4d3c671'),
    categoryKey: 'museums',
    subtitle: 'Discover museums and exhibitions',
  },
  {
    id: 'lm-3',
    title: 'Parks',
    image: u('photo-1519331379826-f10be5486c6f'),
    categoryKey: 'parks',
    subtitle: 'Browse parks and green spaces',
  },
  {
    id: 'lm-4',
    title: 'Markets',
    image: u('photo-1488459716781-31db52582fe9'),
    categoryKey: 'markets',
    subtitle: 'Local markets and fresh finds',
  },
  {
    id: 'lm-5',
    title: 'Restaurants',
    image: u('photo-1414235077428-338989a2e8c0'),
    categoryKey: 'restaurants',
    subtitle: 'Where the locals go to eat',
  },
  {
    id: 'lm-6',
    title: 'Cafes',
    image: u('photo-1509042239860-f550ce710b93'),
    categoryKey: 'cafes',
    subtitle: 'Relax over coffee and conversation',
  },
  {
    id: 'lm-7',
    title: 'Historic Sites',
    image: u('photo-1558618666-fcd25c85cd64'),
    categoryKey: 'historic-sites',
    subtitle: 'Walk through centuries of history',
  },
  {
    id: 'lm-8',
    title: 'Galleries',
    image: u('photo-1580137189272-c9379f8864fd'),
    categoryKey: 'galleries',
    subtitle: 'Art and culture, beautifully curated',
  },
  {
    id: 'lm-9',
    title: 'Nature Reserves',
    image: u('photo-1506905925346-21bda4d32df4'),
    categoryKey: 'nature-reserves',
    subtitle: 'Untouched nature up close',
  },
  {
    id: 'lm-10',
    title: 'Viewpoints',
    image: u('photo-1464822759023-fed622ff2c3b'),
    categoryKey: 'viewpoints',
    subtitle: 'The world from above',
  },
];
