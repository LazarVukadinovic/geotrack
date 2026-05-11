import { Injectable } from '@angular/core';
import { MapPlace } from '../models/map-place.model';
import { Observable, of } from 'rxjs';
@Injectable({ providedIn: 'root' })
export class MapPlacesService {

  private places: MapPlace[] = [
    {
      id: '1', name: 'Šumarice Memorial Park', type: 'Location', category: 'Park & Nature',
      description: 'Memorial park dedicated to victims of the Kragujevac massacre in 1941.',
      status: 'Active', eventsToday: 1, lat: 44.0213, lng: 20.8943,
      zone: [[44.0250, 20.8890], [44.0270, 20.8980], [44.0240, 20.9020], [44.0200, 20.9010], [44.0185, 20.8950], [44.0200, 20.8890]],
    },
    {
      id: '2', name: 'Knjazevsko-srpski teatar', type: 'Place', category: 'Culture & Arts',
      description: 'One of the oldest theatres in Serbia, founded in 1835.',
      status: 'Active', eventsToday: 2, lat: 44.0106, lng: 20.9103,
      address: 'Daniciceva 3, Kragujevac',
    },
    {
      id: '3', name: 'Sumadija sajam', type: 'Place', category: 'Community Hub',
      description: 'A mixed-use facility serving as an incubator for local tech startups and events.',
      status: 'Active', eventsToday: 3, lat: 44.0248, lng: 20.9305,
      address: 'Save Kovacevica bb, Kragujevac',
    },
    {
      id: '4', name: 'Bubanj Lake', type: 'Location', category: 'Nature & Leisure',
      description: 'Popular recreational lake area perfect for walks and outdoor activities.',
      status: 'Active', eventsToday: 0, lat: 44.0169, lng: 20.9258,
      zone: [[44.0185, 20.9220], [44.0190, 20.9310], [44.0155, 20.9315], [44.0148, 20.9225]],
    },
    {
      id: '5', name: 'Stadion Cika Daca', type: 'Place', category: 'Sports',
      description: 'Home stadium of FK Radnički Kragujevac.',
      status: 'Active', eventsToday: 1, lat: 44.0212, lng: 20.8991,
      address: 'Kralja Milana 4, Kragujevac',
    },
    {
      id: '6', name: 'Kragujevac Old Town', type: 'Destination', category: 'Historic Quarter',
      description: 'The historic heart of Kragujevac with cultural landmarks and old architecture.',
      status: 'Active', eventsToday: 0, lat: 44.0128, lng: 20.9142,
      zone: [[44.0135, 20.9090], [44.0140, 20.9200], [44.0100, 20.9220], [44.0085, 20.9130], [44.0110, 20.9080]],
      address: 'Staro jezgro, Kragujevac',
    },
    {
      id: '7', name: 'Hala Jezero', type: 'Place', category: 'Sports & Events',
      description: 'Multi-purpose sports and events hall.',
      status: 'Active', eventsToday: 2, lat: 44.0139, lng: 20.9241,
      address: 'Grada Sirena 15, Kragujevac',
    },
    {
      id: 'l2', name: 'City Center Kragujevac', type: 'Location', category: 'Urban Center',
      description: 'Central square and surrounding area of Kragujevac.',
      status: 'Active', eventsToday: 0, lat: 44.011028, lng: 20.916156,
      zone: [[44.0135, 20.9090], [44.0140, 20.9200], [44.0100, 20.9220], [44.0085, 20.9130], [44.0110, 20.9080]],
      address: 'Trg Radomira Putnika, Kragujevac',
    },
    {
      id: 'l3', name: 'Veliki Park', type: 'Location', category: 'Park & Nature',
      description: 'Large public park in central Kragujevac.',
      status: 'Active', eventsToday: 0, lat: 44.0178, lng: 20.9042,
      zone: [[44.0195, 20.9010], [44.0200, 20.9075], [44.0165, 20.9080], [44.0155, 20.9015]],
      address: 'Kragujevackog oktobra bb, Kragujevac',
    },
  ];

  getAll(): MapPlace[] { return [...this.places]; }

  getLocations(): MapPlace[] {
    return this.places.filter(p => p.type === 'Location');
  }

  getPlaces(): MapPlace[] {
    return this.places.filter(p => p.type === 'Place');
  }

  getDestinations(): MapPlace[] {
    return this.places.filter(p => p.type === 'Destination');
  }

  search(query: string): MapPlace[] {
    const q = query.trim().toLowerCase();
    return this.places.filter(p =>
      p.name.toLowerCase().includes(q) ||
      p.category.toLowerCase().includes(q) ||
      p.type.toLowerCase().includes(q)
    );
  }

  addPlace(place: MapPlace): void {
    this.places.push(place);
  }

  findById(id: string): MapPlace | undefined {
    return this.places.find(p => p.id === id);
  }
  getAll$(): Observable<MapPlace[]> {
    // MOCK → return this.http.get<MapPlace[]>('/api/map/places');
    return of(this.getAll());
  }

  getLocations$(): Observable<MapPlace[]> {
    // MOCK → return this.http.get<MapPlace[]>('/api/map/locations');
    return of(this.getLocations());
  }

  getPlaces$(): Observable<MapPlace[]> {
    // MOCK → return this.http.get<MapPlace[]>('/api/map/places/type/place');
    return of(this.getPlaces());
  }

  search$(query: string): Observable<MapPlace[]> {
    // MOCK → return this.http.get<MapPlace[]>(`/api/map/search?q=${query}`);
    return of(this.search(query));
  }
}