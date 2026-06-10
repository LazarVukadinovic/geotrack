import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

export interface CmRegionOption {
  id: number;
  name: string;
  territoryName: string | null;
  isSuggested: boolean;
}

export interface CmDestinationOption {
  id: number;
  name: string;
}

export interface CmLocationOption {
  id: number;
  name: string;
  destinationId: number;
  destinationName: string;
  status: string;
}

export interface CmContributionListItem {
  id: number;
  type: string;
  name: string;
  placeType: string | null;
  stayType: string | null;
  location: string | null;
  cardImageUrl: string | null;
  coverImageUrl: string | null;
  status: string;
  submittedAt: string;
  updatedAt: string | null;
  submittedByDisplayName: string | null;
  submittedByInitials: string | null;
}

export interface CmOperatingHour {
  dayOfWeek: string;
  isOpen: boolean;
  openTime: string | null;
  closeTime: string | null;
}

export interface CmContributionDetail extends CmContributionListItem {
  placeTypeId: number | null;
  locationId: number | null;
  address: string | null;
  city: string | null;
  region: string | null;
  country: string | null;
  suggestedRegionId: number | null;
  suggestedRegionName: string | null;
  regionDisplayName: string | null;
  suggestedDestinationName: string | null;
  suggestedLocationName: string | null;
  latitude: number | null;
  longitude: number | null;
  description: string;
  phone: string | null;
  contactEmail: string | null;
  website: string | null;
  openingHours: string | null;
  operatingHoursEnabled: boolean;
  operatingHours: CmOperatingHour[];
  galleryImageUrls: string[];
  tags: string[];
  amenities: string[];
  bookingPartners: { id: string; url: string }[];
  imagePosition: string;
  imageSize: string;
  cardImagePosition: string | null;
  cardImageSize: string | null;
  reviewNote: string | null;
  reviewedAt: string | null;
  ownershipProofDocumentUrl: string | null;
  ownershipProofOriginalFileName: string | null;
  ownershipProofContentType: string | null;
  submittedByDisplayName: string | null;
  submittedByInitials: string | null;
  createdDestinationId: number | null;
  createdLocationId: number | null;
}

interface ApiListResponse {
  contributions: ApiContributionItem[];
}

interface ApiDetailResponse {
  contribution: ApiContributionItem;
}

interface ApiOperatingHour {
  dayOfWeek?: string | null;
  isOpen?: boolean | null;
  openTime?: string | null;
  closeTime?: string | null;
}

interface ApiContributionItem {
  id: number;
  type?: string | null;
  name: string;
  placeType?: string | null;
  stayType?: string | null;
  location?: string | null;
  cardImageUrl?: string | null;
  coverImageUrl?: string | null;
  galleryImageUrls?: string[] | null;
  status: string;
  submittedAt: string;
  updatedAt?: string | null;
  placeTypeId?: number | null;
  locationId?: number | null;
  address?: string | null;
  city?: string | null;
  region?: string | null;
  country?: string | null;
  suggestedRegionId?: number | null;
  suggestedRegionName?: string | null;
  regionDisplayName?: string | null;
  suggestedDestinationName?: string | null;
  suggestedLocationName?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  description?: string | null;
  phone?: string | null;
  contactEmail?: string | null;
  website?: string | null;
  openingHours?: string | null;
  operatingHoursEnabled?: boolean | null;
  operatingHours?: ApiOperatingHour[] | null;
  tags?: string[] | null;
  amenities?: string[] | null;
  stayAmenities?: string[] | null;
  bookingPartners?: { id: string; url: string }[] | null;
  imagePosition?: string | null;
  imageSize?: string | null;
  cardImagePosition?: string | null;
  cardImageSize?: string | null;
  reviewNote?: string | null;
  reviewedAt?: string | null;
  ownershipProofDocumentUrl?: string | null;
  ownershipProofOriginalFileName?: string | null;
  ownershipProofContentType?: string | null;
  submittedByDisplayName?: string | null;
  submittedByInitials?: string | null;
  createdDestinationId?: number | null;
  createdLocationId?: number | null;
}

@Injectable({ providedIn: 'root' })
export class ContentModeratorContributionsService {
  private readonly http = inject(HttpClient);

  getAll(): Observable<CmContributionListItem[]> {
    return this.http
      .get<ApiListResponse>('/api/contributions/content-manager')
      .pipe(map(res => (res.contributions ?? []).map(mapListItem)));
  }

  getById(id: number): Observable<CmContributionDetail> {
    return this.http
      .get<ApiDetailResponse>(`/api/contributions/content-manager/${id}`)
      .pipe(map(res => mapDetail(res.contribution)));
  }

  approve(id: number, note?: string): Observable<CmContributionDetail> {
    return this.http
      .post<ApiDetailResponse>(`/api/contributions/${id}/approve`, { note: note?.trim() || undefined })
      .pipe(map(res => mapDetail(res.contribution)));
  }

  approveWithRegion(
    id: number,
    regionId: number,
    existingDestinationId: number | null,
    newDestinationName: string | null,
    newLocationName: string | null,
    note?: string,
  ): Observable<CmContributionDetail> {
    return this.http
      .post<ApiDetailResponse>(`/api/contributions/content-manager/${id}/approve`, {
        regionId,
        existingDestinationId: existingDestinationId ?? undefined,
        newDestinationName: newDestinationName?.trim() || undefined,
        newLocationName: newLocationName?.trim() || undefined,
        note: note?.trim() || undefined,
      })
      .pipe(map(res => mapDetail(res.contribution)));
  }

  getDestinationsForRegion(regionId: number): Observable<CmDestinationOption[]> {
    return this.http
      .get<{ id: number; name: string; destinations?: { id: number; name: string }[] }>(
        `/api/regions/${regionId}`
      )
      .pipe(
        map(res => (res.destinations ?? []).map(d => ({ id: d.id, name: d.name }))),
      );
  }

  getLocationsForDestination(destinationId: number): Observable<CmLocationOption[]> {
    return this.http
      .get<{ locations: CmLocationOption[] }>(
        `/api/contributions/content-manager/destinations/${destinationId}/locations`
      )
      .pipe(map(res => res.locations ?? []));
  }

  reject(id: number, note?: string): Observable<CmContributionDetail> {
    return this.http
      .post<ApiDetailResponse>(`/api/contributions/${id}/reject`, { note: note?.trim() || undefined })
      .pipe(map(res => mapDetail(res.contribution)));
  }

  getRegionOptions(): Observable<CmRegionOption[]> {
    return this.http
      .get<{ regions: CmRegionOption[] }>('/api/contributions/region-options')
      .pipe(map(res => (res.regions ?? []).map(region => ({
        ...region,
        isSuggested: region.isSuggested ?? false,
      }))));
  }

  uploadDestinationCardImage(destinationId: number, imageFile: File): Observable<void> {
    const formData = new FormData();
    formData.append('image', imageFile);
    return this.http.post<void>(
      `/api/contributions/content-manager/destinations/${destinationId}/card-image`,
      formData,
    );
  }

  uploadLocationCardImage(locationId: number, imageFile: File): Observable<void> {
    const formData = new FormData();
    formData.append('image', imageFile);
    return this.http.post<void>(
      `/api/contributions/content-manager/locations/${locationId}/card-image`,
      formData,
    );
  }
}

function mapListItem(item: ApiContributionItem): CmContributionListItem {
  return {
    id: item.id,
    type: item.type ?? 'CreatePlace',
    name: item.name,
    placeType: item.placeType ?? null,
    stayType: item.stayType ?? null,
    location: item.location ?? null,
    cardImageUrl: item.cardImageUrl ?? null,
    coverImageUrl: item.coverImageUrl ?? null,
    status: item.status,
    submittedAt: item.submittedAt,
    updatedAt: item.updatedAt ?? null,
    submittedByDisplayName: item.submittedByDisplayName ?? null,
    submittedByInitials: item.submittedByInitials ?? null,
  };
}

function mapDetail(item: ApiContributionItem): CmContributionDetail {
  return {
    ...mapListItem(item),
    placeTypeId: item.placeTypeId ?? null,
    locationId: item.locationId ?? null,
    address: item.address ?? null,
    city: item.city ?? null,
    region: item.region ?? null,
    country: item.country ?? null,
    suggestedRegionId: item.suggestedRegionId ?? null,
    suggestedRegionName: item.suggestedRegionName ?? null,
    regionDisplayName: item.regionDisplayName ?? null,
    suggestedDestinationName: item.suggestedDestinationName ?? null,
    suggestedLocationName: item.suggestedLocationName ?? null,
    latitude: item.latitude ?? null,
    longitude: item.longitude ?? null,
    description: item.description ?? '',
    phone: item.phone ?? null,
    contactEmail: item.contactEmail ?? null,
    website: item.website ?? null,
    openingHours: item.openingHours ?? null,
    operatingHoursEnabled: item.operatingHoursEnabled ?? false,
    operatingHours: (item.operatingHours ?? []).map(h => ({
      dayOfWeek: h.dayOfWeek ?? '',
      isOpen: h.isOpen ?? false,
      openTime: h.openTime ?? null,
      closeTime: h.closeTime ?? null,
    })),
    galleryImageUrls: item.galleryImageUrls ?? [],
    tags: item.tags ?? [],
    amenities: [...(item.amenities ?? []), ...(item.stayAmenities ?? [])],
    bookingPartners: (item.bookingPartners ?? []).map(bp => ({ id: bp.id, url: bp.url })),
    imagePosition: item.imagePosition || '50% 50%',
    imageSize: item.imageSize || 'cover',
    cardImagePosition: item.cardImagePosition ?? null,
    cardImageSize: item.cardImageSize ?? null,
    reviewNote: item.reviewNote ?? null,
    reviewedAt: item.reviewedAt ?? null,
    ownershipProofDocumentUrl: item.ownershipProofDocumentUrl ?? null,
    ownershipProofOriginalFileName: item.ownershipProofOriginalFileName ?? null,
    ownershipProofContentType: item.ownershipProofContentType ?? null,
    submittedByDisplayName: item.submittedByDisplayName ?? null,
    submittedByInitials: item.submittedByInitials ?? null,
    createdDestinationId: item.createdDestinationId ?? null,
    createdLocationId: item.createdLocationId ?? null,
  };
}
