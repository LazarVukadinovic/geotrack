import { Component, input, output, signal, OnDestroy, OnInit, effect, ElementRef, viewChild, inject, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import maplibregl, { Map as MapLibreMap, MapStyleImageMissingEvent } from 'maplibre-gl';
import { firstValueFrom } from 'rxjs';
import { MapApiService } from '../../../../../../core/services/map-api.service';
import { BackgroundImageFallbackDirective } from '../../../../../../shared/directives/background-image-fallback.directive';
import { PhotoGalleryComponent } from '../../../../../../shared/components/photo-gallery/photo-gallery.component';
import type { EntityImage } from '../../../../../../core/models/entity-image.model';

const MAP_STYLE_URL = '/map-style.json';
const DEFAULT_PICKER_LAT = 44.82;
const DEFAULT_PICKER_LNG = 20.46;
const MAX_OWNERSHIP_PROOF_BYTES = 10 * 1024 * 1024;
const FALLBACK_OWNABLE_PLACE_TYPES = new Set(['stay', 'restaurant', 'cafe', 'bar']);
const HIDDEN_CONTRIBUTION_PLACE_TYPES = new Set(['beach', 'nature', 'shopping']);
const COVER_IMAGE_OUTPUT = { width: 1600, height: 900 };
const CARD_IMAGE_OUTPUT = { width: 960, height: 720 };

export interface ContributionPlaceTypeOption {
  id: number;
  name: string;
  description?: string | null;
  isOwnable: boolean;
}

export interface BasicsData {
  name: string;
  placeType: string;
  placeTypeId: number | null;
  stayType: string;
  address: string;
  city: string;
  region: string;
  country: string;
  locationId?: number | null;
  imageUrl: string;
  cardImageUrl: string;
  cardImageFile?: File | null;
  coverImageFile?: File | null;
  galleryImageUrls: string[];
  galleryImageFiles?: (File | null)[];
  ownershipProofFile?: File | null;
  ownershipProofDocumentUrl?: string | null;
  ownershipProofOriginalFileName?: string | null;
  lat: number | null;
  lng: number | null;
  imgBgPosition: string;
  imgBgSize: string;
  cardImgBgPosition?: string;
  cardImgBgSize?: string;
}

@Component({
  selector: 'app-step-basics',
  standalone: true,
  imports: [CommonModule, FormsModule, BackgroundImageFallbackDirective, PhotoGalleryComponent],
  templateUrl: './step-basics.component.html',
  styleUrl: './step-basics.component.css',
})
export class StepBasicsComponent implements OnInit, OnDestroy {
  private readonly mapApi = inject(MapApiService);
  private readonly http = inject(HttpClient);
  data = input<BasicsData | null>(null);
  locationLocked = input<boolean>(false);
  ownershipProofLocked = input<boolean>(false);
  saved = output<BasicsData>();

  placeTypes = signal<ContributionPlaceTypeOption[]>(defaultPlaceTypes());
  stayTypes = ['Hotel', 'Villa', 'Apartment', 'Resort', 'Cabin', 'Glamping', 'Hostel'];
  coverPreviewUrl = signal('');
  cardPreviewUrl = signal('');
  galleryPreviewUrls = signal<string[]>([]);
  ownershipProofFileName = signal('');
  ownershipProofError = signal('');
  showMapPicker = signal(false);
  pickerLat = signal<number | null>(null);
  pickerLng = signal<number | null>(null);
  isGeocoding = signal(false);
  isAddressGeocoding = signal(false);
  addressGeocodeStatus = signal('');
  submitAttempted = signal(false);
  private pickerMap?: MapLibreMap;
  private pickerResizeObserver?: ResizeObserver;
  private pickerMapInitFrame: number | null = null;
  private pickerMapResizeFrame: number | null = null;
  private addressGeocodeTimeout: number | null = null;
  private addressGeocodeRequestId = 0;
  private lastGeocodedAddressKey = '';
  private suppressAddressGeocode = false;

  form: BasicsData = {
    name: '', placeType: 'Landmark', placeTypeId: null, stayType: 'Hotel', address: '', city: '',
    region: '', country: '', imageUrl: '', cardImageUrl: '', cardImageFile: null, coverImageFile: null, galleryImageUrls: [], galleryImageFiles: [], ownershipProofFile: null,
    ownershipProofDocumentUrl: null, ownershipProofOriginalFileName: null,
    lat: null, lng: null, imgBgPosition: '50% 50%', imgBgSize: 'cover',
    cardImgBgPosition: '50% 50%', cardImgBgSize: 'cover',
  };

  ngOnInit(): void {
    this.loadPlaceTypes();
  }

  selectPlaceType(type: ContributionPlaceTypeOption): void {
    this.form.placeType = type.name;
    this.form.placeTypeId = type.id;
    this.ownershipProofError.set('');

    if (!this.requiresOwnershipProof() && !this.ownershipProofLocked()) {
      this.form.ownershipProofFile = null;
      this.form.ownershipProofDocumentUrl = null;
      this.form.ownershipProofOriginalFileName = null;
      this.ownershipProofFileName.set('');
    }
  }

  isPlaceTypeSelected(type: ContributionPlaceTypeOption): boolean {
    return (this.form.placeTypeId !== null && this.form.placeTypeId > 0 && this.form.placeTypeId === type.id) ||
      normalizePlaceType(this.form.placeType) === normalizePlaceType(type.name);
  }

  onOwnershipProofSelected(e: Event): void {
    if (this.ownershipProofLocked()) return;

    const file = (e.target as HTMLInputElement).files?.[0];
    (e.target as HTMLInputElement).value = '';
    if (!file) return;
    this.ownershipProofError.set('');
    if (!isPdfFile(file)) {
      this.ownershipProofError.set('Only PDF documents are accepted.');
      return;
    }
    if (file.size > MAX_OWNERSHIP_PROOF_BYTES) {
      this.ownershipProofError.set('Document must be 10 MB or smaller.');
      return;
    }
    this.form.ownershipProofFile = file;
    this.form.ownershipProofDocumentUrl = null;
    this.form.ownershipProofOriginalFileName = null;
    this.ownershipProofFileName.set(file.name);
  }

  onCoverImageSelected(e: Event): void {
    const file = (e.target as HTMLInputElement).files?.[0];
    (e.target as HTMLInputElement).value = '';
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      this.coverPreviewUrl.set(reader.result as string);
      this.form.imageUrl = this.coverPreviewUrl();
      this.form.coverImageFile = file;
      this.resetCoverCrop();
    };
    reader.readAsDataURL(file);
  }

  onCardImageSelected(e: Event): void {
    const file = (e.target as HTMLInputElement).files?.[0];
    (e.target as HTMLInputElement).value = '';
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      this.cardPreviewUrl.set(reader.result as string);
      this.form.cardImageUrl = this.cardPreviewUrl();
      this.form.cardImageFile = file;
      this.resetCardCrop();
    };
    reader.readAsDataURL(file);
  }

  onGalleryImagesSelected(e: Event): void {
    const files = Array.from((e.target as HTMLInputElement).files ?? []);
    (e.target as HTMLInputElement).value = '';
    if (files.length === 0) return;

    for (const file of files) {
      const reader = new FileReader();
      reader.onload = () => {
        const url = reader.result as string;
        this.galleryPreviewUrls.update(urls => [...urls, url]);
        this.form.galleryImageUrls = [...this.form.galleryImageUrls, url];
        this.form.galleryImageFiles = [...(this.form.galleryImageFiles ?? []), file];
      };
      reader.readAsDataURL(file);
    }
  }

  removeGalleryImage(index: number): void {
    this.galleryPreviewUrls.update(urls => urls.filter((_, i) => i !== index));
    this.form.galleryImageUrls = this.form.galleryImageUrls.filter((_, i) => i !== index);
    const files = [...(this.form.galleryImageFiles ?? [])];
    files.splice(index, 1);
    this.form.galleryImageFiles = files;
  }

  changeGalleryImage(index: number, e: Event): void {
    const file = (e.target as HTMLInputElement).files?.[0];
    (e.target as HTMLInputElement).value = '';
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      const url = reader.result as string;
      this.galleryPreviewUrls.update(urls => {
        const copy = [...urls];
        copy[index] = url;
        return copy;
      });
      this.form.galleryImageUrls = this.form.galleryImageUrls.map((u, i) => i === index ? url : u);
      const files = [...(this.form.galleryImageFiles ?? [])];
      while (files.length <= index) files.push(null);
      files[index] = file;
      this.form.galleryImageFiles = files;
    };
    reader.readAsDataURL(file);
  }

  galleryLightboxIndex = signal<number | null>(null);
  visibleGalleryItems = computed(() => {
    const urls = this.galleryPreviewUrls();
    const visibleUrls = urls.slice(0, 6);
    return visibleUrls.map((url, index) => ({ url, index }));
  });
  hiddenGalleryCount = computed(() => Math.max(0, this.galleryPreviewUrls().length - this.visibleGalleryItems().length));

  openGalleryLightbox(index: number): void {
    this.galleryLightboxIndex.set(index);
  }

  closeGalleryLightbox(): void {
    this.galleryLightboxIndex.set(null);
  }

  galleryLightboxPrev(): void {
    const idx = this.galleryLightboxIndex();
    if (idx === null || idx <= 0) return;
    this.galleryLightboxIndex.set(idx - 1);
  }

  galleryLightboxNext(): void {
    const idx = this.galleryLightboxIndex();
    const total = this.galleryPreviewUrls().length;
    if (idx === null || idx >= total - 1) return;
    this.galleryLightboxIndex.set(idx + 1);
  }

  galleryLightboxImages(): EntityImage[] {
    return this.galleryPreviewUrls().map((url, index) => ({
      id: String(index),
      url,
      imageRole: 'gallery',
      sortOrder: index,
      isMain: false,
      imagePosition: '50% 50%',
      imageSize: 'cover',
      altText: `${this.form.name || 'Gallery image'} ${index + 1}`,
      caption: '',
    }));
  }

  openMapPicker(): void {
    const hasCoordinates = this.hasUsableCoordinates(this.form.lat, this.form.lng);
    // Only pre-set coords if we already have confirmed ones — otherwise wait
    // for the map 'load' event to sync them once the container has real dimensions.
    this.pickerLat.set(hasCoordinates ? this.form.lat : null);
    this.pickerLng.set(hasCoordinates ? this.form.lng : null);
    this.destroyPickerMap();
    this.showMapPicker.set(true);
    this.schedulePickerMapInit();
  }

  closeMapPicker(): void {
    this.destroyPickerMap();
    this.showMapPicker.set(false);
  }

  private schedulePickerMapInit(attempt = 0): void {
    this.cancelPickerMapInit();

    this.pickerMapInitFrame = requestAnimationFrame(() => {
      this.pickerMapInitFrame = null;
      if (!this.showMapPicker() || this.pickerMap) return;

      const container = this.pickerMapContainer()?.nativeElement;
      const rect = container?.getBoundingClientRect();

      if (!container || !rect || rect.width < 1 || rect.height < 1) {
        if (attempt < 10) this.schedulePickerMapInit(attempt + 1);
        return;
      }

      this.initPickerMap(container);
    });
  }

  private initPickerMap(container: HTMLElement): void {
    if (this.pickerMap) return;

    const lat = this.pickerLat();
    const lng = this.pickerLng();
    const hasCoordinates = this.hasUsableCoordinates(lat, lng);
    const center: [number, number] = hasCoordinates
      ? [lng as number, lat as number]
      : [DEFAULT_PICKER_LNG, DEFAULT_PICKER_LAT];

    this.pickerMap = new maplibregl.Map({
      container,
      style: MAP_STYLE_URL,
      center,
      zoom: hasCoordinates ? 13 : 6,
      attributionControl: false,
      fadeDuration: 0,
      trackResize: true,
    });

    this.pickerResizeObserver = new ResizeObserver(() => this.schedulePickerMapResize());
    this.pickerResizeObserver.observe(container);

    this.pickerMap.on('load', () => {
      this.schedulePickerMapResize();
      this.syncPickerCoordinatesFromCenter();
    });

    this.pickerMap.on('styleimagemissing', (event: MapStyleImageMissingEvent) => {
      const map = this.pickerMap;
      if (!map || map.hasImage(event.id)) return;
      map.addImage(event.id, { width: 1, height: 1, data: new Uint8Array([0, 0, 0, 0]) });
    });

    this.pickerMap.on('move', () => {
      this.syncPickerCoordinatesFromCenter();
    });

    this.pickerMap.on('click', (event) => {
      this.pickerLat.set(event.lngLat.lat);
      this.pickerLng.set(event.lngLat.lng);
      this.pickerMap!.easeTo({ center: event.lngLat, duration: 200 });
    });

    this.schedulePickerMapResize();
  }

  private schedulePickerMapResize(): void {
    if (this.pickerMapResizeFrame !== null) {
      cancelAnimationFrame(this.pickerMapResizeFrame);
    }

    this.pickerMapResizeFrame = requestAnimationFrame(() => {
      this.pickerMapResizeFrame = null;
      if (!this.pickerMap || !this.showMapPicker()) return;

      this.pickerMap.resize();
      this.syncPickerCoordinatesFromCenter();
    });
  }

  private syncPickerCoordinatesFromCenter(): void {
    if (!this.pickerMap) return;

    const c = this.pickerMap.getCenter();
    if (!this.hasUsableCoordinates(c.lat, c.lng)) return;

    this.pickerLat.set(c.lat);
    this.pickerLng.set(c.lng);
  }

  private destroyPickerMap(): void {
    this.cancelPickerMapInit();
    if (this.pickerMapResizeFrame !== null) {
      cancelAnimationFrame(this.pickerMapResizeFrame);
      this.pickerMapResizeFrame = null;
    }

    this.pickerResizeObserver?.disconnect();
    this.pickerResizeObserver = undefined;
    this.pickerMap?.remove();
    this.pickerMap = undefined;
  }

  private cancelPickerMapInit(): void {
    if (this.pickerMapInitFrame === null) return;

    cancelAnimationFrame(this.pickerMapInitFrame);
    this.pickerMapInitFrame = null;
  }

  private hasUsableCoordinates(latitude: number | null | undefined, longitude: number | null | undefined): boolean {
    return typeof latitude === 'number' &&
      typeof longitude === 'number' &&
      Number.isFinite(latitude) &&
      Number.isFinite(longitude) &&
      latitude >= -90 &&
      latitude <= 90 &&
      longitude >= -180 &&
      longitude <= 180 &&
      (latitude !== 0 || longitude !== 0);
  }

  onAddressFieldsChanged(): void {
    if (this.locationLocked() || this.suppressAddressGeocode) return;

    const addressKey = this.addressLookupKey();
    if (addressKey.length < 4 || addressKey === this.lastGeocodedAddressKey) return;

    if (this.addressGeocodeTimeout !== null) {
      window.clearTimeout(this.addressGeocodeTimeout);
    }

    this.addressGeocodeStatus.set('');
    this.addressGeocodeTimeout = window.setTimeout(() => {
      this.addressGeocodeTimeout = null;
      void this.geocodeAddressFromFields(addressKey);
    }, 650);
  }

  private async geocodeAddressFromFields(addressKey = this.addressLookupKey()): Promise<void> {
    if (this.locationLocked() || addressKey.length < 4) return;

    const requestId = ++this.addressGeocodeRequestId;
    this.isAddressGeocoding.set(true);
    this.addressGeocodeStatus.set('Looking up coordinates...');

    try {
      const results = await firstValueFrom(this.mapApi.geocode(addressKey));
      if (requestId !== this.addressGeocodeRequestId) return;

      const result = results.find(item => this.hasUsableCoordinates(item.latitude, item.longitude));
      if (!result) {
        this.addressGeocodeStatus.set('Could not find coordinates for this address.');
        return;
      }

      this.form.lat = result.latitude;
      this.form.lng = result.longitude;
      this.pickerLat.set(result.latitude);
      this.pickerLng.set(result.longitude);
      this.lastGeocodedAddressKey = addressKey;
      this.addressGeocodeStatus.set('Coordinates found from address.');
    } catch {
      if (requestId === this.addressGeocodeRequestId) {
        this.addressGeocodeStatus.set('Could not look up coordinates right now.');
      }
    } finally {
      if (requestId === this.addressGeocodeRequestId) {
        this.isAddressGeocoding.set(false);
      }
    }
  }

  private addressLookupKey(): string {
    return [this.form.address, this.form.city, this.form.region, this.form.country]
      .map(part => (part ?? '').trim())
      .filter(Boolean)
      .join(', ');
  }

  async confirmCoordinates(): Promise<void> {
    const lat = this.pickerLat();
    const lng = this.pickerLng();
    if (!this.hasUsableCoordinates(lat, lng)) return;

    const confirmedLat = lat as number;
    const confirmedLng = lng as number;

    this.form.lat = confirmedLat;
    this.form.lng = confirmedLng;
    this.isGeocoding.set(true);

    try {
      this.suppressAddressGeocode = true;
      const data = await firstValueFrom(this.mapApi.reverseGeocode(confirmedLat, confirmedLng));
      if (data.address) this.form.address = data.address;
      if (data.city) this.form.city = data.city;
      if (data.region) this.form.region = data.region;
      if (data.country) this.form.country = data.country;
      this.lastGeocodedAddressKey = this.addressLookupKey();
      this.addressGeocodeStatus.set('Coordinates set.');
    } catch {
    } finally {
      this.suppressAddressGeocode = false;
      this.isGeocoding.set(false);
      this.closeMapPicker();
    }
  }

  isValid(): boolean {
    const baseValid = !!this.form.name.trim() &&
      !!this.form.city.trim() &&
      !!this.form.country.trim() &&
      (this.locationLocked() || this.hasConfirmedLocation());
    if (!baseValid) return false;
    if (!this.form.cardImageFile && !this.form.cardImageUrl) return false;
    if (!this.form.coverImageFile && !this.form.imageUrl) return false;
    return true;
  }

  hasConfirmedLocation(): boolean {
    return this.hasUsableCoordinates(this.form.lat, this.form.lng);
  }

  isAccommodationSelected(): boolean {
    return this.form.placeType === 'Stay';
  }

  /** True for types where ownership doc section is shown (Bar, Cafe, Restaurant, Stay). */
  requiresOwnershipProof(): boolean {
    const selectedType = this.selectedPlaceType();
    return selectedType?.isOwnable ?? FALLBACK_OWNABLE_PLACE_TYPES.has(normalizePlaceType(this.form.placeType));
  }

  /** True when ownable type selected but no proof attached — user should see warning. */
  showOwnershipWarning(): boolean {
    return this.requiresOwnershipProof() &&
      !this.form.ownershipProofFile &&
      !this.form.ownershipProofDocumentUrl;
  }

  private selectedPlaceType(): ContributionPlaceTypeOption | undefined {
    const selectedId = this.form.placeTypeId;
    const selectedName = normalizePlaceType(this.form.placeType);

    return this.placeTypes().find(type =>
      (selectedId !== null && selectedId > 0 && type.id === selectedId) ||
      normalizePlaceType(type.name) === selectedName);
  }

  private loadPlaceTypes(): void {
    this.http.get<ContributionPlaceTypeOption[]>('/api/contributions/place-types').subscribe({
      next: (types) => {
        const normalized = (types ?? [])
          .filter(type => !!type?.name?.trim())
          .map(type => ({
            id: type.id,
            name: type.name.trim(),
            description: type.description ?? null,
            isOwnable: !!type.isOwnable,
          }))
          .filter(type => !HIDDEN_CONTRIBUTION_PLACE_TYPES.has(normalizePlaceType(type.name)));

        if (normalized.length === 0) return;

        this.placeTypes.set(normalized);
        this.syncSelectedPlaceTypeWithOptions();
      },
      error: () => {
        this.syncSelectedPlaceTypeWithOptions();
      },
    });
  }

  private syncSelectedPlaceTypeWithOptions(): void {
    const selected = this.selectedPlaceType();
    if (selected) {
      this.form.placeType = selected.name;
      this.form.placeTypeId = selected.id;
      return;
    }

    const fallback = this.placeTypes()[0];
    if (!this.form.placeType && fallback) {
      this.form.placeType = fallback.name;
      this.form.placeTypeId = fallback.id;
    }
  }

  ownershipProofHelpText(): string {
    return this.isAccommodationSelected()
      ? 'Upload a PDF document proving your ownership or management of this property.'
      : 'Upload a PDF document proving your ownership or management of this business.';
  }

  showLocationError(): boolean {
    return this.submitAttempted() && !this.hasConfirmedLocation();
  }

  canConfirmPickerLocation(): boolean {
    return this.hasUsableCoordinates(this.pickerLat(), this.pickerLng()) && !this.isGeocoding();
  }

  async onContinue(): Promise<void> {
    this.submitAttempted.set(true);
    if (!this.isValid()) return;

    this.form.imgBgPosition = this.bgPosition();
    this.form.imgBgSize = this.bgSize();
    this.form.cardImgBgPosition = this.cardBgPosition();
    this.form.cardImgBgSize = this.cardBgSize();

    const croppedCoverImageFile = this.form.coverImageFile
      ? await cropImageFile(
        this.form.coverImageFile,
        this.coverPreviewUrl(),
        this.bgPosition(),
        this.bgSize(),
        COVER_IMAGE_OUTPUT.width,
        COVER_IMAGE_OUTPUT.height,
        'cover')
      : this.form.coverImageFile;
    const croppedCardImageFile = this.form.cardImageFile
      ? await cropImageFile(
        this.form.cardImageFile,
        this.cardPreviewUrl(),
        this.cardBgPosition(),
        this.cardBgSize(),
        CARD_IMAGE_OUTPUT.width,
        CARD_IMAGE_OUTPUT.height,
        'card')
      : this.form.cardImageFile;

    this.saved.emit({
      ...this.form,
      coverImageFile: croppedCoverImageFile,
      cardImageFile: croppedCardImageFile,
    });
  }
  imgContainer = viewChild<ElementRef<HTMLDivElement>>('imgContainer');
  cardImgContainer = viewChild<ElementRef<HTMLDivElement>>('cardImgContainer');
  private readonly pickerMapContainer = viewChild<ElementRef<HTMLDivElement>>('coordMap');
  bgPosition = signal('50% 50%');
  bgSize = signal('cover');
  cardBgPosition = signal('50% 50%');
  cardBgSize = signal('cover');

  private touchState: {
    startX: number; startY: number;
    startPosX: number; startPosY: number;
    startDist: number; startSize: number;
  } | null = null;

  private cardTouchState: {
    startX: number; startY: number;
    startPosX: number; startPosY: number;
    startDist: number; startSize: number;
  } | null = null;

  private boundTouchStart!: (e: TouchEvent) => void;
  private boundTouchMove!: (e: TouchEvent) => void;
  private boundTouchEnd!: () => void;
  private boundCardTouchStart!: (e: TouchEvent) => void;
  private boundCardTouchMove!: (e: TouchEvent) => void;
  private boundCardTouchEnd!: () => void;

  constructor() {
    effect(() => {
      const d = this.data();
      if (d) {
        // Preserve coordinates the user already confirmed via the map picker —
        // the parent may re-pass data without lat/lng (e.g. during change detection)
        // and we must not let that overwrite a confirmed location.
        const prevLat = this.form.lat;
        const prevLng = this.form.lng;
        const hadConfirmedLocation = this.hasUsableCoordinates(prevLat, prevLng);

        Object.assign(this.form, d);
        this.syncSelectedPlaceTypeWithOptions();
        if (d.imageUrl) this.coverPreviewUrl.set(d.imageUrl);
        if (d.cardImageUrl) this.cardPreviewUrl.set(d.cardImageUrl);
        this.galleryPreviewUrls.set(d.galleryImageUrls ?? []);
        this.form.cardImageFile = d.cardImageFile ?? null;
        this.form.coverImageFile = d.coverImageFile ?? null;
        this.form.galleryImageFiles = d.galleryImageFiles ?? [];
        this.form.ownershipProofFile = d.ownershipProofFile ?? null;
        this.form.ownershipProofDocumentUrl = d.ownershipProofDocumentUrl ?? null;
        this.form.ownershipProofOriginalFileName = d.ownershipProofOriginalFileName ?? null;
        if (d.ownershipProofFile) {
          this.ownershipProofFileName.set(d.ownershipProofFile.name);
        } else if (d.ownershipProofOriginalFileName) {
          this.ownershipProofFileName.set(d.ownershipProofOriginalFileName);
        } else {
          this.ownershipProofFileName.set('');
        }
        if (this.hasUsableCoordinates(d.lat, d.lng)) {
          this.pickerLat.set(d.lat);
          this.pickerLng.set(d.lng);
        } else if (hadConfirmedLocation) {
          // Restore previously confirmed location that the incoming data doesn't carry
          this.form.lat = prevLat;
          this.form.lng = prevLng;
          this.pickerLat.set(prevLat);
          this.pickerLng.set(prevLng);
        } else {
          this.form.lat = null;
          this.form.lng = null;
          this.pickerLat.set(null);
          this.pickerLng.set(null);
        }
        if (d.imgBgPosition) this.bgPosition.set(d.imgBgPosition);
        if (d.imgBgSize) this.bgSize.set(d.imgBgSize);
        if (d.cardImgBgPosition) this.cardBgPosition.set(d.cardImgBgPosition);
        if (d.cardImgBgSize) this.cardBgSize.set(d.cardImgBgSize);
      }
    });

    effect(() => {
      const el = this.imgContainer()?.nativeElement;
      if (!el) return;
      if (this.boundTouchStart) {
        el.removeEventListener('touchstart', this.boundTouchStart);
        el.removeEventListener('touchmove', this.boundTouchMove);
        el.removeEventListener('touchend', this.boundTouchEnd);
      }
      this.registerTouchListeners();
    });

    effect(() => {
      const el = this.cardImgContainer()?.nativeElement;
      if (!el) return;
      if (this.boundCardTouchStart) {
        el.removeEventListener('touchstart', this.boundCardTouchStart);
        el.removeEventListener('touchmove', this.boundCardTouchMove);
        el.removeEventListener('touchend', this.boundCardTouchEnd);
      }
      this.registerCardTouchListeners();
    });
  }

  private getDist(touches: TouchList): number {
    const dx = touches[0].clientX - touches[1].clientX;
    const dy = touches[0].clientY - touches[1].clientY;
    return Math.sqrt(dx * dx + dy * dy);
  }

  private parsePct(val: string): number {
    return parseFloat(val.replace('%', ''));
  }

  private parseSize(val: string): number {
    if (val === 'cover') return 100;
    return parseFloat(val.replace('%', ''));
  }

  resetCoverCrop(): void {
    this.bgPosition.set('50% 50%');
    this.bgSize.set('cover');
  }

  resetCardCrop(): void {
    this.cardBgPosition.set('50% 50%');
    this.cardBgSize.set('cover');
  }

  private registerTouchListeners(): void {
    const el = this.imgContainer()?.nativeElement;
    if (!el) return;

    this.boundTouchStart = (e: TouchEvent) => {
      if (!this.coverPreviewUrl()) return;
      const target = e.target as HTMLElement;
      if (target.closest('.change-label')) return;
      e.preventDefault();
      const posX = this.parsePct(this.bgPosition().split(' ')[0]);
      const posY = this.parsePct(this.bgPosition().split(' ')[1]);
      const size = this.parseSize(this.bgSize());
      if (e.touches.length === 1) {
        this.touchState = {
          startX: e.touches[0].clientX,
          startY: e.touches[0].clientY,
          startPosX: posX, startPosY: posY,
          startDist: 0, startSize: size
        };
      } else if (e.touches.length === 2) {
        this.touchState = {
          startX: (e.touches[0].clientX + e.touches[1].clientX) / 2,
          startY: (e.touches[0].clientY + e.touches[1].clientY) / 2,
          startPosX: posX, startPosY: posY,
          startDist: this.getDist(e.touches),
          startSize: size
        };
      }
    };

    this.boundTouchMove = (e: TouchEvent) => {
      if (!this.touchState || !this.coverPreviewUrl()) return;
      e.preventDefault();

      if (e.touches.length === 1) {
        const dx = (e.touches[0].clientX - this.touchState.startX) / 2;
        const dy = (e.touches[0].clientY - this.touchState.startY) / 2;
        const newX = Math.min(100, Math.max(0, this.touchState.startPosX - dx));
        const newY = Math.min(100, Math.max(0, this.touchState.startPosY - dy));
        this.bgPosition.set(`${newX}% ${newY}%`);
      } else if (e.touches.length === 2) {
        const newDist = this.getDist(e.touches);
        const scale = newDist / this.touchState.startDist;
        const newSize = Math.min(200, Math.max(50, this.touchState.startSize * scale));
        this.bgSize.set(`${newSize}%`);
      }
    };

    this.boundTouchEnd = () => { this.touchState = null; };

    el.addEventListener('touchstart', this.boundTouchStart, { passive: false });
    el.addEventListener('touchmove', this.boundTouchMove, { passive: false });
    el.addEventListener('touchend', this.boundTouchEnd);
  }

  private registerCardTouchListeners(): void {
    const el = this.cardImgContainer()?.nativeElement;
    if (!el) return;

    this.boundCardTouchStart = (e: TouchEvent) => {
      if (!this.cardPreviewUrl()) return;
      const target = e.target as HTMLElement;
      if (target.closest('.change-label')) return;
      e.preventDefault();
      const posX = this.parsePct(this.cardBgPosition().split(' ')[0]);
      const posY = this.parsePct(this.cardBgPosition().split(' ')[1]);
      const size = this.parseSize(this.cardBgSize());
      if (e.touches.length === 1) {
        this.cardTouchState = { startX: e.touches[0].clientX, startY: e.touches[0].clientY, startPosX: posX, startPosY: posY, startDist: 0, startSize: size };
      } else if (e.touches.length === 2) {
        this.cardTouchState = { startX: (e.touches[0].clientX + e.touches[1].clientX) / 2, startY: (e.touches[0].clientY + e.touches[1].clientY) / 2, startPosX: posX, startPosY: posY, startDist: this.getDist(e.touches), startSize: size };
      }
    };

    this.boundCardTouchMove = (e: TouchEvent) => {
      if (!this.cardTouchState || !this.cardPreviewUrl()) return;
      e.preventDefault();
      if (e.touches.length === 1) {
        const dx = (e.touches[0].clientX - this.cardTouchState.startX) / 2;
        const dy = (e.touches[0].clientY - this.cardTouchState.startY) / 2;
        const newX = Math.min(100, Math.max(0, this.cardTouchState.startPosX - dx));
        const newY = Math.min(100, Math.max(0, this.cardTouchState.startPosY - dy));
        this.cardBgPosition.set(`${newX}% ${newY}%`);
      } else if (e.touches.length === 2) {
        const newDist = this.getDist(e.touches);
        const scale = newDist / this.cardTouchState.startDist;
        const newSize = Math.min(200, Math.max(50, this.cardTouchState.startSize * scale));
        this.cardBgSize.set(`${newSize}%`);
      }
    };

    this.boundCardTouchEnd = () => { this.cardTouchState = null; };

    el.addEventListener('touchstart', this.boundCardTouchStart, { passive: false });
    el.addEventListener('touchmove', this.boundCardTouchMove, { passive: false });
    el.addEventListener('touchend', this.boundCardTouchEnd);
  }

  ngOnDestroy() {
    this.destroyPickerMap();
    if (this.addressGeocodeTimeout !== null) {
      window.clearTimeout(this.addressGeocodeTimeout);
      this.addressGeocodeTimeout = null;
    }
    const el = this.imgContainer()?.nativeElement;
    if (el && this.boundTouchStart) {
      el.removeEventListener('touchstart', this.boundTouchStart);
      el.removeEventListener('touchmove', this.boundTouchMove);
      el.removeEventListener('touchend', this.boundTouchEnd);
    }
    const cardEl = this.cardImgContainer()?.nativeElement;
    if (cardEl && this.boundCardTouchStart) {
      cardEl.removeEventListener('touchstart', this.boundCardTouchStart);
      cardEl.removeEventListener('touchmove', this.boundCardTouchMove);
      cardEl.removeEventListener('touchend', this.boundCardTouchEnd);
    }
  }
}

function defaultPlaceTypes(): ContributionPlaceTypeOption[] {
  return [
    { id: 0, name: 'Landmark', description: null, isOwnable: false },
    { id: 0, name: 'Restaurant', description: null, isOwnable: true },
    { id: 0, name: 'Cafe', description: null, isOwnable: true },
    { id: 0, name: 'Bar', description: null, isOwnable: true },
    { id: 0, name: 'Stay', description: null, isOwnable: true },
  ];
}

function normalizePlaceType(value: string | null | undefined): string {
  return (value ?? '').trim().toLowerCase();
}

function isPdfFile(file: File): boolean {
  return file.type === 'application/pdf' || file.name.trim().toLowerCase().endsWith('.pdf');
}

function clampPercent(value: number): number {
  return Math.min(100, Math.max(0, Number.isFinite(value) ? value : 50));
}

async function cropImageFile(
  originalFile: File,
  previewUrl: string,
  position: string,
  size: string,
  outputWidth: number,
  outputHeight: number,
  role: 'cover' | 'card',
): Promise<File> {
  const source = previewUrl || await fileToDataUrl(originalFile);
  const image = await loadImage(source);
  const canvas = document.createElement('canvas');
  canvas.width = outputWidth;
  canvas.height = outputHeight;

  const context = canvas.getContext('2d');
  if (!context) return originalFile;

  const [xRaw, yRaw] = (position || '50% 50%').split(' ');
  const focusX = clampPercent(parseFloat(xRaw || '50')) / 100;
  const focusY = clampPercent(parseFloat(yRaw || '50')) / 100;
  const zoom = Math.max(100, Number.isFinite(parseFloat(size)) ? parseFloat(size) : 100) / 100;
  const baseScale = Math.max(outputWidth / image.naturalWidth, outputHeight / image.naturalHeight);
  const scale = baseScale * zoom;
  const drawWidth = image.naturalWidth * scale;
  const drawHeight = image.naturalHeight * scale;
  const x = clampOffset(outputWidth * focusX - image.naturalWidth * focusX * scale, outputWidth - drawWidth, 0);
  const y = clampOffset(outputHeight * focusY - image.naturalHeight * focusY * scale, outputHeight - drawHeight, 0);

  context.drawImage(image, x, y, drawWidth, drawHeight);

  const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.9));
  if (!blob) return originalFile;

  const originalName = originalFile.name.replace(/\.[^.]+$/, '');
  return new File([blob], `${originalName}-${role}.jpg`, { type: 'image/jpeg' });
}

function clampOffset(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

function fileToDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ''));
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}

function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error('Could not load selected image.'));
    image.src = src;
  });
}
