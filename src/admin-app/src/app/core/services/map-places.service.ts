import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { MapPlace } from '../models/map-place.model';
import { PlacesListService, PlaceListItem } from './places-list.service';
import { LocationsListService, LocationListItem } from './locations-list.service';
import { DestinationsListService, DestinationListItem } from './destinations-list.service';

@Injectable({ providedIn: 'root' })
export class MapPlacesService {

  constructor(
    private placesSvc: PlacesListService,
    private locationsSvc: LocationsListService,
    private destinationsSvc: DestinationsListService,
  ) {}



  getAll(): MapPlace[] {
    return [
      ...this.getPlaces(),
      ...this.getLocations(),
      ...this.getDestinations(),
    ];
  }

  getPlaces(): MapPlace[] {
    return this.placesSvc.getAllItems()
      .filter(i => i.lat != null && i.lng != null)
      .map(i => this.fromPlace(i));
  }

  getLocations(): MapPlace[] {
    return this.locationsSvc.getAllItems()
      .filter(i => i.lat != null && i.lng != null)
      .map(i => this.fromLocation(i));
  }

  getDestinations(): MapPlace[] {
    return this.destinationsSvc.getAllItems()
      .filter(i => i.lat != null && i.lng != null)
      .map(i => this.fromDestination(i));
  }

  search(query: string): MapPlace[] {
    const q = query.trim().toLowerCase();
    return this.getAll().filter(p =>
      p.name.toLowerCase().includes(q) ||
      p.category.toLowerCase().includes(q) ||
      p.type.toLowerCase().includes(q)
    );
  }

  findById(id: string): MapPlace | undefined {
    return this.getAll().find(p => p.id === id);
  }

  addPlace(place: MapPlace): void {
    if (this.findById(place.id)) return;

    const today = new Date().toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
    const base = {
      id:          place.id,
      name:        place.name,
      summary:     place.description,
      region:      'Šumadija, Srbija',
      status:      (place.status === 'Active' ? 'Published' : 'Draft') as 'Published' | 'Draft',
      lastUpdated: today,
      imageBg:     'linear-gradient(135deg,#2563eb 0%,#1d4ed8 100%)',
      category:    place.category,
      lat:         place.lat,
      lng:         place.lng,
      zone:        place.zone,
      address:     place.address,
      description: place.description,
    };

    if (place.type === 'Place')       this.placesSvc.addItem(base);
    else if (place.type === 'Location') this.locationsSvc.addItem(base);
    else                               this.destinationsSvc.addItem(base);
  }

  // ── Observable wrappers (drop-in replacements for future HTTP calls)

  getAll$():                        Observable<MapPlace[]> { return of(this.getAll()); }
  getLocations$():                  Observable<MapPlace[]> { return of(this.getLocations()); }
  getPlaces$():                     Observable<MapPlace[]> { return of(this.getPlaces()); }
  search$(query: string):           Observable<MapPlace[]> { return of(this.search(query)); }

  // Private converters

  private fromPlace(i: PlaceListItem): MapPlace {
    return {
      id:          i.id,
      name:        i.name,
      type:        'Place',
      category:    i.category,
      description: i.description ?? i.summary,
      status:      i.status === 'Published' ? 'Active' : 'Inactive',
      eventsToday: 0,
      lat:         i.lat!,
      lng:         i.lng!,
      address:     i.address,
    };
  }

  private fromLocation(i: LocationListItem): MapPlace {
    return {
      id:          i.id,
      name:        i.name,
      type:        'Location',
      category:    i.category,
      description: i.description ?? i.summary,
      status:      i.status === 'Published' ? 'Active' : 'Inactive',
      eventsToday: 0,
      lat:         i.lat!,
      lng:         i.lng!,
      zone:        i.zone,
      address:     i.address,
    };
  }

  private fromDestination(i: DestinationListItem): MapPlace {
    return {
      id:          i.id,
      name:        i.name,
      type:        'Destination',
      category:    i.category,
      description: i.description ?? i.summary,
      status:      i.status === 'Published' ? 'Active' : 'Inactive',
      eventsToday: 0,
      lat:         i.lat!,
      lng:         i.lng!,
      zone:        i.zone,
      address:     i.address,
    };
  }
}
