import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, tap, throwError } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { AnalyticsService } from '../../../core/services/analytics.service';
import type { AnalyticsEntityType } from '../../../core/services/analytics.types';
import { FavoriteEntityType, FavoriteStateService } from '../../../core/services/favorite-state.service';

export type CollectionEntityType = 'destination' | 'location' | 'place' | 'event';
type CollectionAnalyticsEntityType = Extract<AnalyticsEntityType, 'place' | 'hotel' | 'event' | 'destination'>;

export interface CollectionPlace {
  itemId: number;
  entityType: CollectionEntityType;
  entityId: number;
  placeId: number;
  title: string;
  image: string;
}

export interface Collection {
  id: number;
  title: string;
  description?: string;
  itemsCount: number;
  coverImages: string[];
  places?: CollectionPlace[];
}

export type CollectionDetailLoadState = 'idle' | 'loading' | 'loaded' | 'notFound' | 'error';

interface CollectionListItemDto {
  id: number;
  name: string;
  description: string;
  userId: number;
  createdAt: string;
  updatedAt: string | null;
  itemsCount: number;
  coverImages: string[];
}

interface GetCollectionsResponseDto {
  collections: CollectionListItemDto[];
}

interface CollectionItemDto {
  id: number;
  collectionId: number;
  entityType: string;
  destinationId?: number | null;
  locationId?: number | null;
  placeId?: number | null;
  eventId?: number | null;
  name: string;
  description: string;
  imageUrl?: string | null;
}

interface GetCollectionDetailsResponseDto {
  id: number;
  name: string;
  description: string;
  userId: number;
  createdAt: string;
  updatedAt: string | null;
  items: CollectionItemDto[];
}

interface CreateCollectionRequestDto {
  name: string;
  description: string;
}

interface CreateCollectionResponseDto {
  id: number;
  name: string;
  description: string;
  userId: number;
}

interface UpdateCollectionRequestDto {
  name: string;
  description: string;
}

interface UpdateCollectionResponseDto {
  id: number;
  name: string;
  description: string;
}

interface AddCollectionItemRequestDto {
  entityType: CollectionEntityType;
  entityId: number;
}

interface AddCollectionItemResponseDto {
  id: number;
  collectionId: number;
  destinationId?: number | null;
  locationId?: number | null;
  placeId?: number | null;
  eventId?: number | null;
}

@Injectable({ providedIn: 'root' })
export class CollectionsService {
  private readonly http        = inject(HttpClient);
  private readonly authService = inject(AuthService);
  private readonly analytics   = inject(AnalyticsService);
  private readonly favoriteState = inject(FavoriteStateService);

  private readonly _collections = signal<Collection[]>([]);
  private readonly _listLoadState = signal<'idle' | 'loading' | 'loaded' | 'error'>('idle');
  private readonly _detailLoadStates = signal<Record<number, CollectionDetailLoadState>>({});
  readonly collections = computed(() => this._collections());
  readonly listLoadState = computed(() => this._listLoadState());

  private _loaded = false;

  constructor() {
    effect(() => {
      if (this.authService.isLoggedIn()) {
        this.ensureLoaded();
      } else {
        this._collections.set([]);
        this._listLoadState.set('idle');
        this._detailLoadStates.set({});
        this._loaded = false;
      }
    });
  }

  getDetailLoadState(collectionId: number): CollectionDetailLoadState {
    return this._detailLoadStates()[collectionId] ?? 'idle';
  }

  ensureLoaded(force = false): void {
    if (!this.authService.isLoggedIn()) return;
    if (!force && (this._loaded || this._listLoadState() === 'loading')) return;
    this.loadFromApi();
  }

  private loadFromApi(): void {
    this._loaded = true;
    this._listLoadState.set('loading');
    this.http.get<GetCollectionsResponseDto>('/api/user/me/collections').subscribe({
      next: (res) => {
        this._collections.set(
          res.collections.map(c => ({
            id: c.id,
            title: c.name,
            description: c.description,
            itemsCount: c.itemsCount,
            coverImages: c.coverImages ?? [],
          })),
        );
        this._listLoadState.set('loaded');
      },
      error: () => {
        this._loaded = false;
        this._listLoadState.set('error');
      },
    });
  }

  /** Fetch full items for one collection and merge them into the signal. */
  loadDetails(collectionId: number): void {
    this.loadDetails$(collectionId).subscribe({ error: () => {} });
  }

  /** Fetch full items for one collection and merge them into the signal. */
  loadDetails$(collectionId: number): Observable<Collection> {
    if (collectionId <= 0) {
      this.setDetailLoadState(collectionId, 'notFound');
      return throwError(() => new Error('Collection not found'));
    }

    this.setDetailLoadState(collectionId, 'loading');
    return this.http
      .get<GetCollectionDetailsResponseDto>(`/api/user/me/collections/${collectionId}`)
      .pipe(
        tap({
          next: (res) => {
            const places: CollectionPlace[] = res.items.map(i => {
              const entityType = this.resolveCollectionEntityType(i);
              const entityId = this.resolveCollectionEntityId(i, entityType);
              return {
                itemId: i.id,
                entityType,
                entityId,
                placeId: entityId,
                title: i.name,
                image: i.imageUrl ?? '',
              };
            });
            const loaded: Collection = {
              id: res.id,
              title: res.name,
              description: res.description,
              itemsCount: places.length,
              coverImages: places.slice(0, 3).map(p => p.image).filter(Boolean),
              places,
            };
            this._collections.update(cols => {
              const exists = cols.some(c => c.id === collectionId);
              if (!exists) return [loaded, ...cols];
              return cols.map(c => c.id === collectionId ? loaded : c);
            });
            this.markItemsSaved(places);
            this.setDetailLoadState(collectionId, 'loaded');
          },
          error: (err) => {
            this.setDetailLoadState(collectionId, err?.status === 404 ? 'notFound' : 'error');
          },
        }),
        map((res) =>
          this._collections().find(c => c.id === res.id) ?? {
            id: res.id,
            title: res.name,
            description: res.description,
            itemsCount: res.items.length,
            coverImages: [],
            places: [],
          },
        ),
      );
  }

  addCollection(title: string, onCreated?: (id: number) => void): void {
    this.createCollection(title).subscribe({
      next: (created) => onCreated?.(created.id),
    });
  }

  createCollection(title: string, description = ''): Observable<Collection> {
    const body: CreateCollectionRequestDto = { name: title, description };
    return this.http.post<CreateCollectionResponseDto>('/api/user/me/collections', body).pipe(
      map((res) => ({
        id: res.id,
        title: res.name,
        description: res.description,
        itemsCount: 0,
        coverImages: [],
        places: [],
      })),
      tap((created) => {
        this._collections.update(cols => [created, ...cols]);
        this.analytics.track('collection_created', { entityType: 'collection', entityId: created.id });
      }),
    );
  }

  addPlace(collectionId: number, place: { placeId: number; title: string; image: string }): void {
    this.addItem(collectionId, {
      entityType: 'place',
      entityId: place.placeId,
      title: place.title,
      image: place.image,
      analyticsEntityType: 'place',
    }).subscribe({ error: () => {} });
  }

  addItem(
    collectionId: number,
    item: {
      entityType: CollectionEntityType;
      entityId: number;
      title: string;
      image?: string;
      analyticsEntityType?: CollectionAnalyticsEntityType;
      surface?: string;
    },
  ): Observable<CollectionPlace> {
    const body: AddCollectionItemRequestDto = {
      entityType: item.entityType,
      entityId: item.entityId,
    };

    return this.http
      .post<AddCollectionItemResponseDto>(
        `/api/user/me/collections/${collectionId}/items`,
        body,
      )
      .pipe(
        map((res) => ({
          itemId: res.id,
          entityType: item.entityType,
          entityId: item.entityId,
          placeId: item.entityId,
          title: item.title,
          image: item.image ?? '',
        })),
        tap((savedItem) => {
          this._collections.update(cols =>
            cols.map(c => {
              if (c.id !== collectionId) return c;

              const places = c.places ?? [];
              const alreadyPresent = places.some(p =>
                p.entityType === savedItem.entityType && p.entityId === savedItem.entityId,
              );
              const nextPlaces = alreadyPresent ? places : [...places, savedItem];
              const nextCoverImages = savedItem.image
                ? [savedItem.image, ...c.coverImages.filter(image => image !== savedItem.image)].slice(0, 3)
                : c.coverImages;

              return {
                ...c,
                places: nextPlaces,
                itemsCount: alreadyPresent ? c.itemsCount : c.itemsCount + 1,
                coverImages: nextCoverImages,
              };
            }),
          );
          this.analytics.track('collection_item_added', {
            entityType: item.analyticsEntityType ?? this.toAnalyticsEntityType(item.entityType),
            entityId: item.entityId,
            metadata: {
              collectionId,
              ...(item.surface ? { surface: item.surface } : {}),
            },
          });
          this.setFavoriteState(savedItem, true);
        }),
      );
  }

  removePlace(collectionId: number, itemId: number): void {
    this.removeItem(collectionId, itemId).subscribe({ error: () => {} });
  }

  removeItem(collectionId: number, itemId: number): Observable<void> {
    const snapshot = this._collections();
    let removed: CollectionPlace | undefined;

    this._collections.update(cols =>
      cols.map(c => {
        if (c.id !== collectionId) return c;
        const places = c.places ?? [];
        removed = places.find(p => p.itemId === itemId);
        const nextPlaces = places.filter(p => p.itemId !== itemId);
        if (!removed) return c;

        return {
          ...c,
          places: nextPlaces,
          itemsCount: Math.max(0, c.itemsCount - 1),
          coverImages: nextPlaces.slice(0, 3).map(p => p.image).filter(Boolean),
        };
      }),
    );

    return this.http
      .delete<void>(`/api/user/me/collections/${collectionId}/items/${itemId}`)
      .pipe(
        tap({
          next: () => {
            if (!removed) return;
            this.analytics.track('collection_item_removed', {
              entityType: this.toAnalyticsEntityType(removed.entityType),
              entityId: removed.entityId,
              metadata: { collectionId },
            });
            this.refreshFavoriteMemberships([removed]);
          },
          error: () => this._collections.set(snapshot),
        }),
      );
  }

  deleteCollection(id: number): void {
    const snapshot = this._collections();
    const removedCollection = snapshot.find(c => c.id === id);
    const affectedItems = removedCollection?.places ?? [];

    this._collections.update(cols => cols.filter(c => c.id !== id));

    this.http.delete<void>(`/api/user/me/collections/${id}`).subscribe({
      next: () => this.refreshFavoriteMemberships(affectedItems),
      error: () => this._collections.set(snapshot),
    });
  }

  renameCollection(id: number, title: string): void {
    const prev = this._collections().find(c => c.id === id)?.title ?? '';
    const prevDesc = this._collections().find(c => c.id === id)?.description ?? '';
    this._collections.update(cols =>
      cols.map(c => (c.id === id ? { ...c, title } : c)),
    );

    const body: UpdateCollectionRequestDto = { name: title, description: prevDesc };
    this.http
      .put<UpdateCollectionResponseDto>(`/api/user/me/collections/${id}`, body)
      .subscribe({
        error: () => {
          this._collections.update(cols =>
            cols.map(c => (c.id === id ? { ...c, title: prev } : c)),
          );
        },
      });
  }

  private resolveCollectionEntityType(item: CollectionItemDto): CollectionEntityType {
    if (item.entityType === 'destination' || item.entityType === 'location' || item.entityType === 'place' || item.entityType === 'event') {
      return item.entityType;
    }
    if (item.placeId != null) return 'place';
    if (item.eventId != null) return 'event';
    if (item.destinationId != null) return 'destination';
    return 'location';
  }

  private resolveCollectionEntityId(item: CollectionItemDto, entityType: CollectionEntityType): number {
    if (entityType === 'place') return item.placeId ?? 0;
    if (entityType === 'event') return item.eventId ?? 0;
    if (entityType === 'destination') return item.destinationId ?? 0;
    return item.locationId ?? 0;
  }

  private toAnalyticsEntityType(entityType: CollectionEntityType): CollectionAnalyticsEntityType | undefined {
    if (entityType === 'place' || entityType === 'event' || entityType === 'destination') {
      return entityType;
    }
    return undefined;
  }

  private markItemsSaved(items: CollectionPlace[]): void {
    const byType = new Map<FavoriteEntityType, Set<number>>();
    for (const item of items) {
      const favoriteType = this.toFavoriteEntityType(item.entityType);
      if (!favoriteType) continue;
      const ids = byType.get(favoriteType) ?? new Set<number>();
      ids.add(item.entityId);
      byType.set(favoriteType, ids);
    }

    for (const [type, ids] of byType) {
      this.favoriteState.setMany(type, ids, true);
    }
  }

  private setFavoriteState(item: CollectionPlace, saved: boolean): void {
    const favoriteType = this.toFavoriteEntityType(item.entityType);
    if (!favoriteType) return;
    this.favoriteState.set(favoriteType, String(item.entityId), saved);
  }

  private refreshFavoriteMemberships(items: CollectionPlace[]): void {
    const byType = new Map<FavoriteEntityType, Set<number>>();
    for (const item of items) {
      const favoriteType = this.toFavoriteEntityType(item.entityType);
      if (!favoriteType) continue;
      const ids = byType.get(favoriteType) ?? new Set<number>();
      ids.add(item.entityId);
      byType.set(favoriteType, ids);
    }

    for (const [type, ids] of byType) {
      this.http.get<number[]>(`/api/favorites?entityType=${type}`).subscribe({
        next: (savedIds) => {
          const savedSet = new Set(savedIds.map(String));
          for (const id of ids) {
            this.favoriteState.set(type, String(id), savedSet.has(String(id)));
          }
        },
        error: () => {
          for (const id of ids) {
            this.favoriteState.set(type, String(id), this.existsInLoadedCollections(type, id));
          }
        },
      });
    }
  }

  private existsInLoadedCollections(entityType: FavoriteEntityType, entityId: number): boolean {
    return this._collections().some(collection =>
      (collection.places ?? []).some(item =>
        item.entityType === entityType && item.entityId === entityId,
      ),
    );
  }

  private toFavoriteEntityType(entityType: CollectionEntityType): FavoriteEntityType | undefined {
    if (entityType === 'place' || entityType === 'event') {
      return entityType;
    }
    return undefined;
  }

  private setDetailLoadState(collectionId: number, state: CollectionDetailLoadState): void {
    this._detailLoadStates.update(current => ({ ...current, [collectionId]: state }));
  }
}
