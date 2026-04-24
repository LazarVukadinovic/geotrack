import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, tap } from 'rxjs';
import { BasicsData } from '../pages/submit-contribution/steps/step-basics/step-basics.component';
import { DetailsData } from '../pages/submit-contribution/steps/step-details/step-details.component';
import { FeaturesData } from '../pages/submit-contribution/steps/step-features/step-features.component';

export type ContributionStatus = 'PendingReview' | 'Approved' | 'Rejected';

export interface ContributionListItem {
  id: number;
  entityType: 'Stay';
  name: string;
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
  entityType: 'Stay';
  name: string;
  stayType: string;
  location: string;
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
  website: string;
  tags: string[];
  amenities: string[];
  bookingPartners: ContributionBookingPartner[];
  reviewNote?: string | null;
  reviewedAt?: string | null;
}

interface UpsertStayContributionPayload {
  name: string;
  stayType: string;
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
  website: string;
  tags: string[];
  amenities: string[];
  bookingPartners: ContributionBookingPartner[];
}

@Injectable({ providedIn: 'root' })
export class ContributionsService {
  private readonly http = inject(HttpClient);

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

  createStay(draft: ContributionDraftData): Observable<ContributionDetail> {
    return this.http.post<ContributionResponse>('/api/contributions/stays', this.toFormData(draft)).pipe(
      map(res => mapDetail(res.contribution)),
      tap(() => this.loadMine()),
    );
  }

  updateStay(id: number, draft: ContributionDraftData): Observable<ContributionDetail> {
    return this.http.put<ContributionResponse>(`/api/contributions/stays/${id}`, this.toFormData(draft)).pipe(
      map(res => mapDetail(res.contribution)),
      tap(() => this.loadMine()),
    );
  }

  deleteContribution(id: number): Observable<void> {
    return this.http.delete<void>(`/api/contributions/${id}`).pipe(
      tap(() => this._items.update(items => items.filter(item => item.id !== id))),
    );
  }

  detailToDraft(detail: ContributionDetail): ContributionDraftData {
    return {
      basics: {
        name: detail.name,
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
        amenities: detail.amenities ?? [],
      },
      details: {
        description: detail.description ?? '',
        phone: detail.phone ?? '',
        website: detail.website ?? '',
        bookingPartners: detail.bookingPartners ?? [],
      },
    };
  }

  private toFormData(draft: ContributionDraftData): FormData {
    const basics = requireBasics(draft.basics);
    const features = normalizeFeatures(draft.features);
    const details = normalizeDetails(draft.details);

    const payload: UpsertStayContributionPayload = {
      name: basics.name,
      stayType: basics.stayType,
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
      website: details.website,
      tags: features.tags,
      amenities: features.amenities,
      bookingPartners: details.bookingPartners,
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
    throw new Error('Stay basics are required before submitting.');
  }

  return basics;
}

function normalizeFeatures(features: FeaturesData | null): FeaturesData {
  return {
    tags: features?.tags ?? [],
    amenities: features?.amenities ?? [],
  };
}

function normalizeDetails(details: DetailsData | null): DetailsData {
  return {
    description: details?.description ?? '',
    phone: details?.phone ?? '',
    website: details?.website ?? '',
    bookingPartners: details?.bookingPartners ?? [],
  };
}

function mapListItem(item: ApiContributionListItem): ContributionListItem {
  const updatedAt = item.updatedAt ?? null;
  const isEdited = !!updatedAt && updatedAt !== item.submittedAt;

  return {
    id: item.id,
    entityType: 'Stay',
    name: item.name,
    stayType: item.stayType,
    location: item.location,
    coverImageUrl: item.coverImageUrl ?? '',
    status: item.status,
    statusLabel: statusLabel(item.status),
    submittedAt: item.submittedAt,
    updatedAt,
    updatedLabel: timeLabel(isEdited ? updatedAt : item.submittedAt, isEdited ? 'Updated' : 'Submitted'),
    publishedPlaceId: item.publishedPlaceId ?? null,
  };
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
    website: item.website ?? '',
    tags: item.tags ?? [],
    amenities: item.amenities ?? [],
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
