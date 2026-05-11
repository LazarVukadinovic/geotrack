import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, tap } from 'rxjs';
import { AnalyticsService } from '../../../core/services/analytics.service';
import { BasicsData } from '../pages/submit-contribution/steps/step-basics/step-basics.component';
import { DetailsData } from '../pages/submit-contribution/steps/step-details/step-details.component';
import { FeaturesData } from '../pages/submit-contribution/steps/step-features/step-features.component';
import {
  normalizeContributionAmenityIds,
  toContributionAmenityApiValues,
} from './contribution-amenities';

export type ContributionStatus = 'PendingReview' | 'Approved' | 'Rejected';
export type ContributionEntityType = 'Place';

export interface ContributionListItem {
  id: number;
  entityType: ContributionEntityType;
  name: string;
  placeType: string;
  stayType: string;
  location: string;
  coverImageUrl: string;
  status: ContributionStatus;
  statusLabel: string;
  submittedAt: string;
  updatedAt: string | null;
  updatedLabel: string;
  publishedPlaceId: number | null;
}

export interface ContributionDetail extends ContributionListItem {
  address: string;
  city: string;
  region: string;
  country: string;
  latitude: number | null;
  longitude: number | null;
  imagePosition: string;
  imageSize: string;
  description: string;
  phone: string;
  contactEmail: string;
  website: string;
  tags: string[];
  amenities: string[];
  bookingPartners: ContributionBookingPartner[];
  reviewNote?: string | null;
  reviewedAt?: string | null;
}

export interface ContributionBookingPartner {
  id: string;
  url: string;
}

export interface ContributionDraftData {
  basics: BasicsData | null;
  features: FeaturesData | null;
  details: DetailsData | null;
}

interface ContributionsResponse {
  contributions: ApiContributionListItem[];
}

interface ContributionResponse {
  contribution: ApiContributionDetail;
}

interface ApiContributionListItem {
  id: number;
  entityType?: string | null;
  name: string;
  placeType?: string | null;
  stayType?: string | null;
  location?: string | null;
  coverImageUrl?: string | null;
  status: ContributionStatus;
  submittedAt: string;
  updatedAt?: string | null;
  publishedPlaceId?: number | null;
}

interface ApiContributionDetail extends ApiContributionListItem {
  address: string;
  city: string;
  region: string;
  country: string;
  latitude?: number | null;
  longitude?: number | null;
  imagePosition: string;
  imageSize: string;
  description: string;
  phone: string;
  contactEmail: string;
  website: string;
  tags?: string[] | null;
  amenities?: string[] | null;
  stayAmenities?: string[] | null;
  bookingPartners?: ContributionBookingPartner[] | null;
  reviewNote?: string | null;
  reviewedAt?: string | null;
}

interface UpsertPlaceContributionPayload {
  type: 'CreatePlace';
  name: string;
  placeType: string;
  stayType?: string;
  address: string;
  city: string;
  region: string;
  country: string;
  latitude: number | null;
  longitude: number | null;
  imagePosition: string;
  imageSize: string;
  description: string;
  phone: string;
  contactEmail: string;
  website: string;
  tags: string[];
  amenities: string[];
  bookingPartners: ContributionBookingPartner[];
}

@Injectable({ providedIn: 'root' })
export class ContributionsService {
  private readonly http = inject(HttpClient);
  private readonly analytics = inject(AnalyticsService);

  private readonly _items = signal<ContributionListItem[]>([]);
  private readonly _isLoading = signal(false);
  private readonly _loadError = signal<string | null>(null);

  readonly items = computed(() => this._items());
  readonly isLoading = computed(() => this._isLoading());
  readonly loadError = computed(() => this._loadError());

  loadMine(): void {
    this._isLoading.set(true);
    this._loadError.set(null);

    this.http.get<ContributionsResponse>('/api/contributions').subscribe({
      next: (res) => {
        this._items.set((res.contributions ?? []).map(mapListItem));
        this._isLoading.set(false);
      },
      error: () => {
        this._items.set([]);
        this._loadError.set("We couldn't load your contributions. Please try again.");
        this._isLoading.set(false);
      },
    });
  }

  getDetail(id: number): Observable<ContributionDetail> {
    return this.http.get<ApiContributionDetail>(`/api/contributions/${id}`).pipe(
      map(mapDetail),
    );
  }

  createPlace(draft: ContributionDraftData): Observable<ContributionDetail> {
    return this.http.post<ContributionResponse>('/api/contributions/places', this.toFormData(draft)).pipe(
      map(res => mapDetail(res.contribution)),
      tap(detail => {
        this.analytics.track('contribution_created', { entityId: detail.id });
        this.loadMine();
      }),
    );
  }

  updatePlace(id: number, draft: ContributionDraftData): Observable<ContributionDetail> {
    return this.http.put<ContributionResponse>(`/api/contributions/places/${id}`, this.toFormData(draft)).pipe(
      map(res => mapDetail(res.contribution)),
      tap(detail => {
        this.analytics.track('contribution_updated', { entityId: detail.id });
        this.loadMine();
      }),
    );
  }

  deleteContribution(id: number): Observable<void> {
    return this.http.delete<void>(`/api/contributions/${id}`).pipe(
      tap(() => {
        this._items.update(items => items.filter(item => item.id !== id));
        this.analytics.track('contribution_deleted', { entityId: id });
      }),
    );
  }

  detailToDraft(detail: ContributionDetail): ContributionDraftData {
    return {
      basics: {
        name: detail.name,
        placeType: detail.placeType || (detail.stayType ? 'Stay' : 'Place'),
        stayType: detail.stayType || 'Hotel',
        address: detail.address,
        city: detail.city,
        region: detail.region,
        country: detail.country,
        imageUrl: detail.coverImageUrl,
        coverImageFile: null,
        lat: detail.latitude,
        lng: detail.longitude,
        imgBgPosition: detail.imagePosition || '50% 50%',
        imgBgSize: detail.imageSize || 'cover',
      },
      features: {
        tags: detail.tags ?? [],
        amenities: normalizeContributionAmenityIds(detail.amenities),
      },
      details: {
        description: detail.description ?? '',
        phone: detail.phone ?? '',
        contactEmail: detail.contactEmail ?? '',
        website: detail.website ?? '',
        bookingPartners: detail.bookingPartners ?? [],
      },
    };
  }

  private toFormData(draft: ContributionDraftData): FormData {
    const basics = requireBasics(draft.basics);
    const features = normalizeFeatures(draft.features);
    const details = normalizeDetails(draft.details);
    const isAccommodation = isAccommodationPlaceType(basics.placeType);

    const payload: UpsertPlaceContributionPayload = {
      type: 'CreatePlace',
      name: basics.name,
      placeType: basics.placeType || 'Place',
      ...(isAccommodation ? { stayType: basics.stayType || 'Hotel' } : {}),
      address: basics.address,
      city: basics.city,
      region: basics.region,
      country: basics.country,
      latitude: basics.lat,
      longitude: basics.lng,
      imagePosition: basics.imgBgPosition || '50% 50%',
      imageSize: basics.imgBgSize || 'cover',
      description: details.description,
      phone: details.phone,
      contactEmail: details.contactEmail,
      website: details.website,
      tags: features.tags,
      amenities: toContributionAmenityApiValues(features.amenities, isAccommodation),
      bookingPartners: isAccommodation ? details.bookingPartners : [],
    };

    const formData = new FormData();
    formData.append('payload', JSON.stringify(payload));

    if (basics.coverImageFile) {
      formData.append('coverImage', basics.coverImageFile);
    }

    return formData;
  }
}

function requireBasics(basics: BasicsData | null): BasicsData {
  if (!basics) {
    throw new Error('Place basics are required before submitting.');
  }

  return basics;
}

function normalizeFeatures(features: FeaturesData | null): FeaturesData {
  return {
    tags: features?.tags ?? [],
    amenities: normalizeContributionAmenityIds(features?.amenities),
  };
}

interface NormalizedDetailsData {
  description: string;
  phone: string;
  contactEmail: string;
  website: string;
  bookingPartners: ContributionBookingPartner[];
}

function normalizeDetails(details: DetailsData | null): NormalizedDetailsData {
  return {
    description: details?.description ?? '',
    phone: details?.phone ?? '',
    contactEmail: details?.contactEmail?.trim() ?? '',
    website: details?.website?.trim() ?? '',
    bookingPartners: (details?.bookingPartners ?? []).map(partner => ({
      id: partner.id,
      url: partner.url.trim(),
    })),
  };
}

function mapListItem(item: ApiContributionListItem): ContributionListItem {
  const updatedAt = item.updatedAt ?? null;
  const isEdited = !!updatedAt && updatedAt !== item.submittedAt;

  return {
    id: item.id,
    entityType: normalizeContributionEntityType(item.entityType),
    name: item.name,
    placeType: item.placeType ?? '',
    stayType: item.stayType ?? '',
    location: item.location ?? '',
    coverImageUrl: item.coverImageUrl ?? '',
    status: item.status,
    statusLabel: statusLabel(item.status),
    submittedAt: item.submittedAt,
    updatedAt,
    updatedLabel: timeLabel(isEdited ? updatedAt : item.submittedAt, isEdited ? 'Updated' : 'Submitted'),
    publishedPlaceId: item.publishedPlaceId ?? null,
  };
}

function normalizeContributionEntityType(entityType: string | null | undefined): ContributionEntityType {
  // Older contribution responses used "Stay" for this user-submitted entity.
  // The current PlaceContribution contract treats the contribution entity as a Place.
  return 'Place';
}

function isAccommodationPlaceType(placeType: string | null | undefined): boolean {
  return (placeType ?? '').trim().toLowerCase() === 'stay';
}

function mapDetail(item: ApiContributionDetail): ContributionDetail {
  return {
    ...mapListItem(item),
    address: item.address ?? '',
    city: item.city ?? '',
    region: item.region ?? '',
    country: item.country ?? '',
    latitude: item.latitude ?? null,
    longitude: item.longitude ?? null,
    imagePosition: item.imagePosition || '50% 50%',
    imageSize: item.imageSize || 'cover',
    description: item.description ?? '',
    phone: item.phone ?? '',
    contactEmail: item.contactEmail ?? '',
    website: item.website ?? '',
    tags: item.tags ?? [],
    amenities: normalizeContributionAmenityIds([
      ...(item.amenities ?? []),
      ...(item.stayAmenities ?? []),
    ]),
    bookingPartners: item.bookingPartners ?? [],
    reviewNote: item.reviewNote ?? null,
    reviewedAt: item.reviewedAt ?? null,
  };
}

function statusLabel(status: ContributionStatus): string {
  switch (status) {
    case 'Approved':
      return 'APPROVED';
    case 'Rejected':
      return 'REJECTED';
    case 'PendingReview':
    default:
      return 'UNDER REVIEW';
  }
}

function timeLabel(value: string, prefix: 'Submitted' | 'Updated'): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return prefix;
  }

  const seconds = Math.max(0, Math.floor((Date.now() - date.getTime()) / 1000));

  if (seconds < 60) return `${prefix} just now`;
  if (seconds < 3600) return `${prefix} ${Math.floor(seconds / 60)}m ago`;
  if (seconds < 86400) return `${prefix} ${Math.floor(seconds / 3600)}h ago`;
  if (seconds < 604800) return `${prefix} ${Math.floor(seconds / 86400)}d ago`;
  if (seconds < 2592000) return `${prefix} ${Math.floor(seconds / 604800)}w ago`;

  return `${prefix} ${date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })}`;
}
