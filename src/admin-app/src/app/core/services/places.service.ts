import { Injectable } from '@angular/core';
import { Observable, of, throwError } from 'rxjs';
import { delay } from 'rxjs/operators';
import { MapPlace } from '../models/map-place.model';

@Injectable({ providedIn: 'root' })
export class PlacesService {

  // --- MOCK: zameniti sa HttpClient pozivima pri integraciji ---
  private _places: MapPlace[] = [];

  getAll(): Observable<MapPlace[]> {
    // MOCK → return this.http.get<MapPlace[]>('/api/places');
    return of([...this._places]).pipe(delay(0));
  }

  getById(id: string): Observable<MapPlace> {
    // MOCK → return this.http.get<MapPlace>(`/api/places/${id}`);
    const found = this._places.find(p => p.id === id);
    if (!found) return throwError(() => new Error(`Place ${id} not found`));
    return of({ ...found });
  }

  add(place: Omit<MapPlace, 'id'>): Observable<MapPlace> {
    // MOCK → return this.http.post<MapPlace>('/api/places', place);
    const newPlace: MapPlace = { ...place, id: Date.now().toString() };
    this._places.push(newPlace);
    return of(newPlace).pipe(delay(800));
  }

  update(id: string, data: Partial<MapPlace>): Observable<MapPlace> {
    // MOCK → return this.http.put<MapPlace>(`/api/places/${id}`, data);
    const idx = this._places.findIndex(p => p.id === id);
    if (idx === -1) return throwError(() => new Error(`Place ${id} not found`));
    this._places[idx] = { ...this._places[idx], ...data };
    return of({ ...this._places[idx] }).pipe(delay(800));
  }

  delete(id: string): Observable<void> {
    // MOCK → return this.http.delete<void>(`/api/places/${id}`);
    this._places = this._places.filter(p => p.id !== id);
    return of(undefined).pipe(delay(800));
  }

  private _pendingEdit: MapPlace | null = null;

  setPendingEdit(place: MapPlace): void  { this._pendingEdit = place; }
  getPendingEdit(): MapPlace | null       { return this._pendingEdit; }
  clearPendingEdit(): void               { this._pendingEdit = null; }
}