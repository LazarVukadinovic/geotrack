import { Injectable } from '@angular/core';
import { Observable, of, throwError } from 'rxjs';
import { delay } from 'rxjs/operators';
import { MapPlace } from '../models/map-place.model';

@Injectable({ providedIn: 'root' })
export class LocationsService {

  // --- MOCK: zameniti sa HttpClient pozivima pri integraciji ---
  private _locations: MapPlace[] = [];

  getAll(): Observable<MapPlace[]> {
    // MOCK → return this.http.get<MapPlace[]>('/api/locations');
    return of([...this._locations]).pipe(delay(0));
  }

  getById(id: string): Observable<MapPlace> {
    // MOCK → return this.http.get<MapPlace>(`/api/locations/${id}`);
    const found = this._locations.find(p => p.id === id);
    if (!found) return throwError(() => new Error(`Location ${id} not found`));
    return of({ ...found });
  }

  add(location: Omit<MapPlace, 'id'>): Observable<MapPlace> {
    // MOCK → return this.http.post<MapPlace>('/api/locations', location);
    const newLocation: MapPlace = { ...location, id: Date.now().toString() };
    this._locations.push(newLocation);
    return of(newLocation).pipe(delay(800));
  }

  update(id: string, data: Partial<MapPlace>): Observable<MapPlace> {
    // MOCK → return this.http.put<MapPlace>(`/api/locations/${id}`, data);
    const idx = this._locations.findIndex(p => p.id === id);
    if (idx === -1) return throwError(() => new Error(`Location ${id} not found`));
    this._locations[idx] = { ...this._locations[idx], ...data };
    return of({ ...this._locations[idx] }).pipe(delay(800));
  }

  delete(id: string): Observable<void> {
    // MOCK → return this.http.delete<void>(`/api/locations/${id}`);
    this._locations = this._locations.filter(p => p.id !== id);
    return of(undefined).pipe(delay(800));
  }

  private _pendingEdit: MapPlace | null = null;

  setPendingEdit(place: MapPlace): void  { this._pendingEdit = place; }
  getPendingEdit(): MapPlace | null       { return this._pendingEdit; }
  clearPendingEdit(): void               { this._pendingEdit = null; }
}