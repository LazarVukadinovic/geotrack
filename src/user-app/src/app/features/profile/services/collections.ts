import { Injectable, signal } from '@angular/core';

export interface CollectionPlace {
  id: string;
  title: string;
  image: string;
}

export interface Collection {
  id: string;
  title: string;
  places: CollectionPlace[];
}

const u = (id: string) =>
  `https://images.unsplash.com/${id}?ixlib=rb-4.0.3&auto=format&fit=crop&w=400&q=75`;

const MOCK_COLLECTIONS: Collection[] = [
  {
    id: 'col-1',
    title: 'Dream Destinations',
    places: [
      { id: 'p1', title: 'Banje Beach',     image: u('photo-1507525428034-b723cf961d3e') },
      { id: 'p2', title: 'Mount Srđ',       image: u('photo-1464822759023-fed622ff2c3b') },
    ],
  },
  {
    id: 'col-2',
    title: 'Nature & Hiking',
    places: [
      { id: 'p3', title: 'Mljet National Park', image: u('photo-1467269204594-9661b134dd2b') },
    ],
  },
  {
    id: 'col-3',
    title: 'Food & Restaurants',
    places: [
      { id: 'p4', title: 'Nautika Restaurant', image: u('photo-1517248135467-4c7edcad34c4') },
      { id: 'p5', title: 'Proto Fish',         image: u('photo-1414235077428-338989a2e8c0') },
      { id: 'p6', title: 'Konoba Dubrava',     image: u('photo-1485872299829-c673f5194813') },
    ],
  },
  {
    id: 'col-4',
    title: 'Historic Sites',
    places: [
      { id: 'p7', title: 'City Walls',     image: u('photo-1499856871958-5b9627545d1a') },
      { id: 'p8', title: 'Fort Lovrijenac', image: u('photo-1548445929-4f60a497f851') },
      { id: 'p9', title: 'Sponza Palace',  image: u('photo-1580137189272-c9379f8864fd') },
    ],
  },
];



@Injectable({ providedIn: 'root' })
export class CollectionsService {
  readonly collections = signal<Collection[]>(MOCK_COLLECTIONS);

  addCollection(title: string): void {
    const newCol: Collection = {
      id: `col-${Date.now()}`,
      title,
      places: [],
    };
    this.collections.update((cols) => [...cols, newCol]);
  }

  addPlace(collectionId: string, place: CollectionPlace): void {
    this.collections.update((cols) =>
      cols.map((c) =>
        c.id === collectionId
          ? { ...c, places: [...c.places, place] }
          : c,
      ),
    );
  }

  removePlace(collectionId: string, placeId: string): void {
    this.collections.update((cols) =>
      cols.map((c) =>
        c.id === collectionId
          ? { ...c, places: c.places.filter((p) => p.id !== placeId) }
          : c,
      ),
    );
  }

  deleteCollection(id: string): void {
    this.collections.update((cols) => cols.filter((c) => c.id !== id));
  }
}