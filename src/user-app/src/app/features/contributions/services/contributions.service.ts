import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, tap } from 'rxjs';
import { AnalyticsService } from '../../../core/services/analytics.service';
import { BasicsData } from '../pages/submit-contribution/steps/step-basics/step-basics.component';
import type { ContributionOperatingHour, DetailsData } from '../pages/submit-contribution/steps/step-details/step-details.component';
import { FeaturesData } from '../pages/submit-contribution/steps/step-features/step-features.component';
import {
  normalizeContributionAmenityIds,
  toContributionAmenityApiValues,
} from './contribution-amenities';

export interface ResolveLocationResult {
  locationId: number | null;
  locationName: string | null;
  destinationName: string | null;
  regionName: string | null;
}

export type ContributionStatus = 'PendingReview' | 'Approved' | 'Rejected';
export type ContributionEntityType = 'Place';
export type ContributionType = 'CreatePlace' | 'UpdatePlace' | 'ClaimOwnership';

export interface ContributionListItem {
  id: number;
  type: ContributionType;
  entityType: ContributionEntityType;
  name: string;
  placeType: string;
  stayType: string;
  location: string;
  cardImageUrl: string;
  coverImageUrl: string;
  galleryImageUrls: string[];
  status: ContributionStatus;
  statusLabel: string;
  submittedAt: string;
  updatedAt: string | null;
  updatedLabel: string;
  publishedPlaceId: number | null;
  isOwnership: boolean;
  hasOwnershipProof: boolean;
}

const OWNABLE_PLACE_TYPES = new Set(['stay', 'restaurant', 'cafe', 'bar']);

export function contributionCanEdit(item: Pick<ContributionListItem, 'status' | 'hasOwnershipProof'>): boolean {
  // Under review or rejected → always editable
  if (item.status !== 'Approved') return true;
  // Approved → editable only if ownership proof was provided
  return item.hasOwnershipProof;
}

export function contributionCanRequestOwnership(
  item: Pick<ContributionListItem, 'status' | 'hasOwnershipProof' | 'isOwnership' | 'placeType' | 'publishedPlaceId'> & { hasPendingOwnershipClaim?: boolean },
): boolean {
  return item.status === 'Approved' &&
    !item.hasOwnershipProof &&
    !item.isOwnership &&
    !item.hasPendingOwnershipClaim &&
    OWNABLE_PLACE_TYPES.has((item.placeType ?? '').toLowerCase().trim()) &&
    item.publishedPlaceId != null;
}

export function contributionCanDelete(item: Pick<ContributionListItem, 'status'>): boolean {
  return item.status === 'PendingReview' || item.status === 'Rejected';
}

export interface ContributionDetail extends ContributionListItem {
  placeTypeId: number | null;
  locationId: number | null;
  address: string;
  city: string;
  region: string;
  country: string;
  latitude: number | null;
  longitude: number | null;
  imagePosition: string;
  imageSize: string;
  cardImagePosition: string;
  cardImageSize: string;
  description: string;
  openingHours: string;
  operatingHoursEnabled: boolean;
  operatingHours: ContributionOperatingHour[];
  phone: string;
  contactEmail: string;
  website: string;
  tags: string[];
  amenities: string[];
  bookingPartners: ContributionBookingPartner[];
  reviewNote?: string | null;
  reviewedAt?: string | null;
  ownershipProofDocumentUrl?: string | null;
  ownershipProofOriginalFileName?: string | null;
  hasPendingOwnershipClaim?: boolean;
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
  type?: string | null;
  entityType?: string | null;
  name: string;
  placeType?: string | null;
  stayType?: string | null;
  location?: string | null;
  cardImageUrl?: string | null;
  mainImageUrl?: string | null;
  coverImageUrl?: string | null;
  galleryImageUrls?: string[] | null;
  status: ContributionStatus;
  submittedAt: string;
  updatedAt?: string | null;
  publishedPlaceId?: number | null;
  hasOwnershipProofDocument?: boolean | null;
}

interface ApiContributionDetail extends ApiContributionListItem {
  placeTypeId?: number | null;
  locationId?: number | null;
  address: string;
  city: string;
  region: string;
  country: string;
  latitude?: number | null;
  longitude?: number | null;
  imagePosition: string;
  imageSize: string;
  cardImagePosition?: string | null;
  cardImageSize?: string | null;
  description: string;
  openingHours?: string | null;
  operatingHoursEnabled?: boolean | null;
  operatingHours?: ContributionOperatingHour[] | null;
  phone: string;
  contactEmail: string;
  website: string;
  tags?: string[] | null;
  amenities?: string[] | null;
  stayAmenities?: string[] | null;
  bookingPartners?: ContributionBookingPartner[] | null;
  images?: ApiContributionImage[] | null;
  reviewNote?: string | null;
  reviewedAt?: string | null;
  ownershipProofDocumentUrl?: string | null;
  ownershipProofOriginalFileName?: string | null;
  hasPendingOwnershipClaim?: boolean | null;
}

interface ApiContributionImage {
  url?: string | null;
  imageRole?: string | null;
  sortOrder?: number | null;
  imagePosition?: string | null;
  imageSize?: string | null;
}

interface UpsertPlaceContributionPayload {
  type: 'CreatePlace' | 'UpdatePlace' | 'ClaimOwnership';
  placeId?: number | null;
  name: string;
  placeTypeId?: number | null;
  locationId?: number | null;
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
  cardImagePosition?: string;
  cardImageSize?: string;
  ownershipProofDocumentUrl?: string | null;
  ownershipProofOriginalFileName?: string | null;
  description: string;
  openingHours?: string;
  operatingHoursEnabled?: boolean;
  operatingHours?: ContributionOperatingHour[];
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

  resolveLocation(latitude: number, longitude: number): Observable<ResolveLocationResult | null> {
    return this.http.get<ResolveLocationResult | null>('/api/contributions/resolve-location', {
      params: { latitude: String(latitude), longitude: String(longitude) },
    });
  }

  getDetail(id: number): Observable<ContributionDetail> {
    return this.http.get<ContributionResponse | ApiContributionDetail>(`/api/contributions/${id}`).pipe(
      map(res => mapDetail(unwrapContributionResponse(res))),
    );
  }

  createPlace(draft: ContributionDraftData, options?: { type?: ContributionType; placeId?: number | null }): Observable<ContributionDetail> {
    return this.http.post<ContributionResponse>('/api/contributions/places', this.toFormData(draft, options)).pipe(
      map(res => mapDetail(res.contribution)),
      tap(detail => {
        this.analytics.track('contribution_created', { entityId: detail.id });
        this.loadMine();
      }),
    );
  }

  updatePlace(id: number, draft: ContributionDraftData, options?: { type?: ContributionType; placeId?: number | null }): Observable<ContributionDetail> {
    return this.http.put<ContributionResponse>(`/api/contributions/places/${id}`, this.toFormData(draft, options)).pipe(
      map(res => mapDetail(res.contribution)),
      tap(detail => {
        this.analytics.track('contribution_updated', { entityId: detail.id });
        this.loadMine();
      }),
    );
  }

  ownerUpdatePlace(placeId: number, draft: ContributionDraftData): Observable<unknown> {
    const basics = requireBasics(draft.basics);
    const features = normalizeFeatures(draft.features);
    const isAccommodation = isAccommodationPlaceType(basics.placeType);
    const details = normalizeDetails(draft.details, isAccommodation);

    // Build existing-image metadata (same logic as toFormData)
    const images: { url: string; imageRole: string; sortOrder: number; imagePosition?: string; imageSize?: string }[] = [];
    if (!basics.coverImageFile && basics.cardImageUrl) {
      images.push({ url: basics.cardImageUrl, imageRole: 'card', sortOrder: -9, imagePosition: basics.cardImgBgPosition || '50% 50%', imageSize: basics.cardImgBgSize || 'cover' });
    }
    if (!basics.coverImageFile && basics.imageUrl) {
      images.push({ url: basics.imageUrl, imageRole: 'cover', sortOrder: -10, imagePosition: basics.imgBgPosition || '50% 50%', imageSize: basics.imgBgSize || 'cover' });
    }
    (basics.galleryImageUrls ?? []).forEach((url, index) => {
      const file = (basics.galleryImageFiles ?? [])[index];
      if (url && !file && !url.startsWith('data:')) {
        images.push({ url, imageRole: 'gallery', sortOrder: index });
      }
    });

    const payload = {
      name: basics.name,
      placeTypeId: basics.placeTypeId ?? null,
      description: details.description,
      operatingHoursEnabled: details.openingHoursEnabled,
      operatingHours: details.openingHoursEnabled ? details.operatingHours : [],
      website: isAccommodation ? '' : details.website,
      contactEmail: details.contactEmail,
      phoneNumber: details.phone,
      bookingExternalLink: details.bookingPartners[0]?.url ?? null,
      tags: features.tags,
      amenities: toContributionAmenityApiValues(features.amenities, isAccommodation),
      bookingPartners: details.bookingPartners,
      images,
      imagePosition: basics.imgBgPosition || '50% 50%',
      imageSize: basics.imgBgSize || 'cover',
      cardImagePosition: basics.cardImgBgPosition || '50% 50%',
      cardImageSize: basics.cardImgBgSize || 'cover',
    };

    const formData = new FormData();
    formData.append('payload', JSON.stringify(payload));

    if (basics.coverImageFile) {
      formData.append('coverImage', basics.coverImageFile);
    }
    if (basics.cardImageFile) {
      formData.append('cardImage', basics.cardImageFile);
    }
    for (const file of basics.galleryImageFiles ?? []) {
      if (file) formData.append('galleryImages', file);
    }

    return this.http.put(`/api/places/${placeId}/owner-update`, formData).pipe(
      tap(() => {
        this.analytics.track('contribution_updated', { entityId: placeId });
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
    const coverImageUrl = detail.coverImageUrl || detail.cardImageUrl || detail.galleryImageUrls[0] || '';
    const cardImageUrl = detail.cardImageUrl || detail.galleryImageUrls[0] || coverImageUrl;
    const galleryImageUrls = withoutSelectedImages(detail.galleryImageUrls, cardImageUrl, coverImageUrl);
    const isAccommodation = isAccommodationPlaceType(detail.placeType || (detail.stayType ? 'Stay' : 'Place'));
    const bookingPartners = normalizeOfficialWebsitePartner(
      detail.bookingPartners ?? [],
      isAccommodation ? detail.website : '',
    );

    return {
      basics: {
        name: detail.name,
        placeType: detail.placeType || (detail.stayType ? 'Stay' : 'Place'),
        placeTypeId: detail.placeTypeId,
        stayType: detail.stayType || 'Hotel',
        address: detail.address,
        city: detail.city,
        region: detail.region,
        country: detail.country,
        locationId: detail.locationId,
        imageUrl: coverImageUrl,
        cardImageUrl,
        cardImageFile: null,
        coverImageFile: null,
        galleryImageUrls,
        galleryImageFiles: galleryImageUrls.map(() => null as (File | null)),
        ownershipProofFile: null,
        ownershipProofDocumentUrl: detail.ownershipProofDocumentUrl ?? null,
        ownershipProofOriginalFileName: detail.ownershipProofOriginalFileName ?? null,
        lat: detail.latitude,
        lng: detail.longitude,
        imgBgPosition: detail.imagePosition || '50% 50%',
        imgBgSize: detail.imageSize || 'cover',
        cardImgBgPosition: detail.cardImagePosition || '50% 50%',
        cardImgBgSize: detail.cardImageSize || 'cover',
      },
      features: {
        tags: detail.tags ?? [],
        amenities: normalizeContributionAmenityIds(detail.amenities),
      },
      details: {
        description: detail.description ?? '',
        openingHoursEnabled: detail.operatingHoursEnabled || !!detail.openingHours?.trim(),
        openingHours: detail.openingHours ?? '',
        operatingHours: detail.operatingHours,
        phone: detail.phone ?? '',
        contactEmail: detail.contactEmail ?? '',
        website: isAccommodation ? '' : detail.website ?? '',
        bookingPartners,
      },
    };
  }

  private toFormData(draft: ContributionDraftData, options?: { type?: ContributionType; placeId?: number | null }): FormData {
    const basics = requireBasics(draft.basics);
    const features = normalizeFeatures(draft.features);
    const isAccommodation = isAccommodationPlaceType(basics.placeType);
    const details = normalizeDetails(draft.details, isAccommodation);

    const payload: UpsertPlaceContributionPayload = {
      type: options?.type ?? 'CreatePlace',
      ...(options?.placeId != null ? { placeId: options.placeId } : {}),
      name: basics.name,
      placeTypeId: basics.placeTypeId,
      placeType: basics.placeType || 'Place',
      ...(isAccommodation ? { stayType: basics.stayType || 'Hotel' } : {}),
      locationId: basics.locationId ?? null,
      address: basics.address,
      city: basics.city,
      region: basics.region,
      country: basics.country,
      latitude: basics.lat,
      longitude: basics.lng,
      imagePosition: basics.imgBgPosition || '50% 50%',
      imageSize: basics.imgBgSize || 'cover',
      cardImagePosition: basics.cardImgBgPosition || '50% 50%',
      cardImageSize: basics.cardImgBgSize || 'cover',
      ownershipProofDocumentUrl: basics.ownershipProofDocumentUrl ?? null,
      ownershipProofOriginalFileName: basics.ownershipProofOriginalFileName ?? null,
      description: details.description,
      operatingHoursEnabled: details.openingHoursEnabled,
      operatingHours: details.openingHoursEnabled ? details.operatingHours : [],
      phone: details.phone,
      contactEmail: details.contactEmail,
      website: isAccommodation ? '' : details.website,
      tags: features.tags,
      amenities: toContributionAmenityApiValues(features.amenities, isAccommodation),
      bookingPartners: isAccommodation ? details.bookingPartners : [],
    };

    const images: {
      url: string;
      imageRole: 'card' | 'cover' | 'gallery';
      sortOrder: number;
      imagePosition?: string;
      imageSize?: string;
    }[] = [];
    if (basics.cardImageFile || basics.cardImageUrl) {
      images.push({
        url: basics.cardImageFile ? '' : basics.cardImageUrl,
        imageRole: 'card',
        sortOrder: -9,
        imagePosition: basics.cardImgBgPosition || '50% 50%',
        imageSize: basics.cardImgBgSize || 'cover',
      });
    }
    if (basics.coverImageFile || basics.imageUrl) {
      images.push({
        url: basics.coverImageFile ? '' : basics.imageUrl,
        imageRole: 'cover',
        sortOrder: -10,
        imagePosition: basics.imgBgPosition || '50% 50%',
        imageSize: basics.imgBgSize || 'cover',
      });
    }
    (basics.galleryImageUrls ?? []).forEach((url, index) => {
      const file = (basics.galleryImageFiles ?? [])[index];
      if (url && !file && !url.startsWith('data:')) {
        images.push({ url, imageRole: 'gallery', sortOrder: index });
      }
    });

    if (images.length > 0) {
      (payload as UpsertPlaceContributionPayload & { images: typeof images }).images = images;
    }

    const formData = new FormData();
    formData.append('payload', JSON.stringify(payload));

    if (basics.cardImageFile) {
      formData.append('cardImage', basics.cardImageFile);
    }

    if (basics.coverImageFile) {
      formData.append('coverImage', basics.coverImageFile);
    }

    for (const file of basics.galleryImageFiles ?? []) {
      if (file) formData.append('galleryImages', file);
    }

    if (basics.ownershipProofFile) {
      formData.append('ownershipProofDocument', basics.ownershipProofFile);
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
  openingHoursEnabled: boolean;
  openingHours: string;
  operatingHours: ContributionOperatingHour[];
  phone: string;
  contactEmail: string;
  website: string;
  bookingPartners: ContributionBookingPartner[];
}

function normalizeDetails(details: DetailsData | null, isAccommodation = false): NormalizedDetailsData {
  const website = details?.website?.trim() ?? '';
  const bookingPartners = normalizeOfficialWebsitePartner(
    (details?.bookingPartners ?? []).map(partner => ({
      id: partner.id,
      url: partner.url.trim(),
    })),
    isAccommodation ? website : '',
  );

  return {
    description: details?.description ?? '',
    openingHoursEnabled: details?.openingHoursEnabled ?? false,
    openingHours: details?.openingHours?.trim() ?? '',
    operatingHours: details?.operatingHours ?? [],
    phone: details?.phone ?? '',
    contactEmail: details?.contactEmail?.trim() ?? '',
    website: isAccommodation ? '' : website,
    bookingPartners,
  };
}

function normalizeOfficialWebsitePartner(
  bookingPartners: ContributionBookingPartner[],
  website: string,
): ContributionBookingPartner[] {
  const normalized = bookingPartners.map(partner => ({
    id: partner.id,
    url: partner.url.trim(),
  }));

  if (!website.trim() || normalized.some(partner => partner.id === 'hotel')) {
    return normalized;
  }

  return [...normalized, { id: 'hotel', url: website.trim() }];
}

function mapListItem(item: ApiContributionListItem): ContributionListItem {
  const updatedAt = item.updatedAt ?? null;
  const isEdited = !!updatedAt && updatedAt !== item.submittedAt;
  const type = normalizeContributionType(item.type);

  return {
    id: item.id,
    type,
    entityType: normalizeContributionEntityType(item.entityType),
    name: item.name,
    placeType: item.placeType ?? '',
    stayType: item.stayType ?? '',
    location: item.location ?? '',
    cardImageUrl: item.cardImageUrl ?? item.mainImageUrl ?? '',
    coverImageUrl: item.coverImageUrl ?? '',
    galleryImageUrls: item.galleryImageUrls ?? [],
    status: item.status,
    statusLabel: statusLabel(item.status),
    submittedAt: item.submittedAt,
    updatedAt,
    updatedLabel: timeLabel(isEdited ? updatedAt : item.submittedAt, isEdited ? 'Updated' : 'Submitted'),
    publishedPlaceId: item.publishedPlaceId ?? null,
    isOwnership: type === 'ClaimOwnership',
    hasOwnershipProof: item.hasOwnershipProofDocument ?? false,
  };
}

function unwrapContributionResponse(response: ContributionResponse | ApiContributionDetail): ApiContributionDetail {
  return 'contribution' in response ? response.contribution : response;
}

function normalizeContributionEntityType(entityType: string | null | undefined): ContributionEntityType {
  // Older contribution responses used "Stay" for this user-submitted entity.
  // The current PlaceContribution contract treats the contribution entity as a Place.
  return 'Place';
}

function normalizeContributionType(type: string | null | undefined): ContributionType {
  switch ((type ?? '').trim().toLowerCase()) {
    case 'updateplace':
      return 'UpdatePlace';
    case 'claimownership':
      return 'ClaimOwnership';
    case 'createplace':
    default:
      return 'CreatePlace';
  }
}

function isAccommodationPlaceType(placeType: string | null | undefined): boolean {
  return (placeType ?? '').trim().toLowerCase() === 'stay';
}

function mapDetail(item: ApiContributionDetail): ContributionDetail {
  const detailImages = normalizeDetailImages(item.images);
  const cardImage = detailImages.find(image => image.imageRole === 'card') ?? null;
  const coverImage = detailImages.find(image => image.imageRole === 'cover') ?? null;
  const cardImageUrl = item.cardImageUrl?.trim() ||
    item.mainImageUrl?.trim() ||
    cardImage?.url ||
    detailImages[0]?.url ||
    coverImage?.url ||
    '';
  const coverImageUrl = item.coverImageUrl?.trim() ||
    coverImage?.url ||
    detailImages[0]?.url ||
    cardImageUrl;
  const rawGalleryImageUrls = (item.galleryImageUrls && item.galleryImageUrls.length > 0)
    ? item.galleryImageUrls
    : detailImages
      .filter(image => image.imageRole === 'gallery')
      .map(image => image.url);
  const galleryImageUrls = withoutSelectedImages(rawGalleryImageUrls, cardImageUrl, coverImageUrl);

  return {
    ...mapListItem(item),
    cardImageUrl,
    coverImageUrl,
    galleryImageUrls,
    placeTypeId: item.placeTypeId ?? null,
    locationId: item.locationId ?? null,
    address: item.address ?? '',
    city: item.city ?? '',
    region: item.region ?? '',
    country: item.country ?? '',
    latitude: item.latitude ?? null,
    longitude: item.longitude ?? null,
    imagePosition: coverImage?.imagePosition || item.imagePosition || '50% 50%',
    imageSize: coverImage?.imageSize || item.imageSize || 'cover',
    cardImagePosition: cardImage?.imagePosition || item.cardImagePosition || '50% 50%',
    cardImageSize: cardImage?.imageSize || item.cardImageSize || 'cover',
    description: item.description ?? '',
    openingHours: item.openingHours ?? '',
    operatingHoursEnabled: item.operatingHoursEnabled ?? false,
    operatingHours: item.operatingHours ?? [],
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
    ownershipProofDocumentUrl: item.ownershipProofDocumentUrl ?? null,
    ownershipProofOriginalFileName: item.ownershipProofOriginalFileName ?? null,
    hasPendingOwnershipClaim: item.hasPendingOwnershipClaim ?? false,
  };
}

function normalizeDetailImages(images: ApiContributionImage[] | null | undefined): {
  url: string;
  imageRole: 'card' | 'cover' | 'gallery';
  sortOrder: number;
  imagePosition: string;
  imageSize: string;
}[] {
  return (images ?? [])
    .map((image, index) => {
      const url = image.url?.trim() ?? '';
      if (!url) return null;
      return {
        url,
        imageRole: normalizeImageRole(image.imageRole),
        sortOrder: typeof image.sortOrder === 'number' && Number.isFinite(image.sortOrder) ? image.sortOrder : index,
        imagePosition: image.imagePosition?.trim() || '50% 50%',
        imageSize: image.imageSize?.trim() || 'cover',
      };
    })
    .filter((image): image is {
      url: string;
      imageRole: 'card' | 'cover' | 'gallery';
      sortOrder: number;
      imagePosition: string;
      imageSize: string;
    } => image !== null)
    .sort((a, b) => imageRoleSort(a.imageRole) - imageRoleSort(b.imageRole) || a.sortOrder - b.sortOrder);
}

function normalizeImageRole(value: string | null | undefined): 'card' | 'cover' | 'gallery' {
  const normalized = value?.trim().toLowerCase();
  return normalized === 'card' || normalized === 'cover' || normalized === 'gallery' ? normalized : 'gallery';
}

function imageRoleSort(role: 'card' | 'cover' | 'gallery'): number {
  switch (role) {
    case 'card': return 0;
    case 'cover': return 1;
    case 'gallery': return 2;
  }
}

function withoutSelectedImages(urls: string[], cardUrl: string, coverUrl: string): string[] {
  const selected = new Set([cardUrl, coverUrl].filter(Boolean).map(url => url.trim()));
  return urls
    .map(url => url.trim())
    .filter((url, index, all) => !!url && !selected.has(url) && all.indexOf(url) === index);
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
