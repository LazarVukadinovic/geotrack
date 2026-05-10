import { Injectable } from '@angular/core';
import { Observable, of, throwError } from 'rxjs';
import { delay } from 'rxjs/operators';
import { MapPlace } from '../models/map-place.model';

@Injectable({ providedIn: 'root' })
export class DestinationsService {

  // --- MOCK: zameniti sa HttpClient pozivima pri integraciji ---
  private _destinations: MapPlace[] = [];

  getAll(): Observable<MapPlace[]> {
    // MOCK → return this.http.get<MapPlace[]>('/api/destinations');
    return of([...this._destinations]).pipe(delay(0));
  }

  getById(id: string): Observable<MapPlace> {
    // MOCK → return this.http.get<MapPlace>(`/api/destinations/${id}`);
    const found = this._destinations.find(p => p.id === id);
    if (!found) return throwError(() => new Error(`Destination ${id} not found`));
    return of({ ...found });
  }

  add(destination: Omit<MapPlace, 'id'>): Observable<MapPlace> {
    // MOCK → return this.http.post<MapPlace>('/api/destinations', destination);
    const newDest: MapPlace = { ...destination, id: Date.now().toString() };
    this._destinations.push(newDest);
    return of(newDest).pipe(delay(800));
  }

  update(id: string, data: Partial<MapPlace>): Observable<MapPlace> {
    // MOCK → return this.http.put<MapPlace>(`/api/destinations/${id}`, data);
    const idx = this._destinations.findIndex(p => p.id === id);
    if (idx === -1) return throwError(() => new Error(`Destination ${id} not found`));
    this._destinations[idx] = { ...this._destinations[idx], ...data };
    return of({ ...this._destinations[idx] }).pipe(delay(800));
  }

  delete(id: string): Observable<void> {
    // MOCK → return this.http.delete<void>(`/api/destinations/${id}`);
    this._destinations = this._destinations.filter(p => p.id !== id);
    return of(undefined).pipe(delay(800));
  }

  private _pendingEdit: MapPlace | null = null;

  setPendingEdit(place: MapPlace): void  { this._pendingEdit = place; }
  getPendingEdit(): MapPlace | null       { return this._pendingEdit; }
  clearPendingEdit(): void               { this._pendingEdit = null; }
}