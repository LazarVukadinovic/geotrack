import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../../../core/services/auth.service';

export interface CollectionPlace {
  itemId: number;
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
  entityType: string;
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

  private readonly _collections = signal<Collection[]>([]);
  readonly collections = computed(() => this._collections());

  private _loaded = false;

  constructor() {
    effect(() => {
      if (this.authService.isLoggedIn()) {
        if (!this._loaded) this.loadFromApi();
      } else {
        this._collections.set([]);
        this._loaded = false;
      }
    });
  }

  private loadFromApi(): void {
    this._loaded = true;
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
      },
      error: () => {
        this._loaded = false;
      },
    });
  }

  /** Fetch full items for one collection and merge them into the signal. */
  loadDetails(collectionId: number): void {
    this.http
      .get<GetCollectionDetailsResponseDto>(`/api/user/me/collections/${collectionId}`)
      .subscribe({
        next: (res) => {
          const places: CollectionPlace[] = res.items.map(i => ({
            itemId: i.id,
            placeId: i.placeId ?? i.eventId ?? i.destinationId ?? i.locationId ?? 0,
            title: i.name,
            image: i.imageUrl ?? '',
          }));
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
        },
      });
  }

  addCollection(title: string, onCreated?: (id: number) => void): void {
    const body: CreateCollectionRequestDto = { name: title, description: '' };
    this.http.post<CreateCollectionResponseDto>('/api/user/me/collections', body).subscribe({
      next: (res) => {
        const created: Collection = {
          id: res.id,
          title: res.name,
          description: res.description,
          itemsCount: 0,
          coverImages: [],
          places: [],
        };
        this._collections.update(cols => [created, ...cols]);
        onCreated?.(res.id);
      },
    });
  }

  addPlace(collectionId: number, place: { placeId: number; title: string; image: string }): void {
    const temp: CollectionPlace = {
      itemId: -Date.now(),
      placeId: place.placeId,
      title: place.title,
      image: place.image,
    };

    this._collections.update(cols =>
      cols.map(c =>
        c.id === collectionId
          ? {
              ...c,
              places: [...(c.places ?? []), temp],
              itemsCount: c.itemsCount + 1,
              coverImages: [...c.coverImages, place.image].slice(0, 3),
            }
          : c,
      ),
    );

    const body: AddCollectionItemRequestDto = { entityType: 'place', entityId: place.placeId };
    this.http
      .post<AddCollectionItemResponseDto>(
        `/api/user/me/collections/${collectionId}/items`,
        body,
      )
      .subscribe({
        next: (res) => {
          this._collections.update(cols =>
            cols.map(c =>
              c.id === collectionId
                ? {
                    ...c,
                    places: (c.places ?? []).map(p =>
                      p.itemId === temp.itemId ? { ...p, itemId: res.id } : p,
                    ),
                  }
                : c,
            ),
          );
        },
        error: () => {
          this._collections.update(cols =>
            cols.map(c =>
              c.id === collectionId
                ? {
                    ...c,
                    places: (c.places ?? []).filter(p => p.itemId !== temp.itemId),
                    itemsCount: Math.max(0, c.itemsCount - 1),
                    coverImages: (c.places ?? [])
                      .filter(p => p.itemId !== temp.itemId)
                      .slice(0, 3)
                      .map(p => p.image),
                  }
                : c,
            ),
          );
        },
      });
  }

  removePlace(collectionId: number, itemId: number): void {
    let removed: CollectionPlace | undefined;
    let prevCount = 0;
    let prevCovers: string[] = [];

    this._collections.update(cols =>
      cols.map(c => {
        if (c.id !== collectionId) return c;
        prevCount = c.itemsCount;
        prevCovers = c.coverImages;
        const places = c.places ?? [];
        removed = places.find(p => p.itemId === itemId);
        const nextPlaces = places.filter(p => p.itemId !== itemId);
        return {
          ...c,
          places: nextPlaces,
          itemsCount: Math.max(0, c.itemsCount - 1),
          coverImages: nextPlaces.slice(0, 3).map(p => p.image).filter(Boolean),
        };
      }),
    );

    this.http
      .delete(`/api/user/me/collections/${collectionId}/items/${itemId}`)
      .subscribe({
        error: () => {
          if (!removed) return;
          const restored = removed;
          this._collections.update(cols =>
            cols.map(c =>
              c.id === collectionId
                ? {
                    ...c,
                    places: [...(c.places ?? []), restored],
                    itemsCount: prevCount,
                    coverImages: prevCovers,
                  }
                : c,
            ),
          );
        },
      });
  }

  deleteCollection(id: number): void {
    const snapshot = this._collections();
    this._collections.update(cols => cols.filter(c => c.id !== id));

    this.http.delete(`/api/user/me/collections/${id}`).subscribe({
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
}
