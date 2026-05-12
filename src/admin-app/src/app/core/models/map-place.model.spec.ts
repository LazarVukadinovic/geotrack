import { MapPlace } from './map-place.model';

describe('MapPlace model', () => {
  it('supports map place data', () => {
    const place: MapPlace = {
      id: '1',
      name: 'Harbor',
      type: 'Place',
      category: 'Landmark',
      description: 'Mapped location',
      status: 'Published',
      eventsToday: 0,
      lat: 42,
      lng: 18,
    };

    expect(place.name).toBe('Harbor');
  });
});
