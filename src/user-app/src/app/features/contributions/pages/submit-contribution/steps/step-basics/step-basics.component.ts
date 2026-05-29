import { Component, input, output, signal, OnDestroy, OnInit, effect, ElementRef, viewChild, inject, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import maplibregl, { Map as MapLibreMap, MapStyleImageMissingEvent } from 'maplibre-gl';
import { firstValueFrom } from 'rxjs';
import { MapApiService } from '../../../../../../core/services/map-api.service';
import { BackgroundImageFallbackDirective } from '../../../../../../shared/directives/background-image-fallback.directive';

const MAP_STYLE_URL = '/map-style.json';
const DEFAULT_PICKER_LAT = 44.82;
const DEFAULT_PICKER_LNG = 20.46;
const MAX_OWNERSHIP_PROOF_BYTES = 10 * 1024 * 1024;
const FALLBACK_OWNABLE_PLACE_TYPES = new Set(['stay', 'restaurant', 'cafe']);

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
  galleryImageFiles?: File[];
  ownershipProofFile?: File | null;
  ownershipProofDocumentUrl?: string | null;
  ownershipProofOriginalFileName?: string | null;
  lat: number | null;
  lng: number | null;
  imgBgPosition: string;
  imgBgSize: string;
}

@Component({
  selector: 'app-step-basics',
  standalone: true,
  imports: [CommonModule, FormsModule, BackgroundImageFallbackDirective],
  templateUrl: './step-basics.component.html',
  styleUrl: './step-basics.component.css',
})
export class StepBasicsComponent implements OnInit, OnDestroy {
  private readonly mapApi = inject(MapApiService);
  private readonly http = inject(HttpClient);
  data = input<BasicsData | null>(null);
  locationLocked = input<boolean>(false);
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
  submitAttempted = signal(false);
  private pickerMap?: MapLibreMap;
  private pickerResizeObserver?: ResizeObserver;
  private pickerMapInitFrame: number | null = null;
  private pickerMapResizeFrame: number | null = null;

  form: BasicsData = {
    name: '', placeType: 'Landmark', placeTypeId: null, stayType: 'Hotel', address: '', city: '',
    region: '', country: '', imageUrl: '', cardImageUrl: '', cardImageFile: null, coverImageFile: null, galleryImageUrls: [], galleryImageFiles: [], ownershipProofFile: null,
    ownershipProofDocumentUrl: null, ownershipProofOriginalFileName: null,
    lat: null, lng: null, imgBgPosition: '50% 50%', imgBgSize: 'cover'
  };

  ngOnInit(): void {
    this.loadPlaceTypes();
  }

  selectPlaceType(type: ContributionPlaceTypeOption): void {
    this.form.placeType = type.name;
    this.form.placeTypeId = type.id;
    this.ownershipProofError.set('');

    if (!this.requiresOwnershipProof()) {
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
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      this.coverPreviewUrl.set(reader.result as string);
      this.form.imageUrl = this.coverPreviewUrl();
      this.form.coverImageFile = file;
    };
    reader.readAsDataURL(file);
  }

  onCardImageSelected(e: Event): void {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      this.cardPreviewUrl.set(reader.result as string);
      this.form.cardImageUrl = this.cardPreviewUrl();
      this.form.cardImageFile = file;
    };
    reader.readAsDataURL(file);
  }

  onGalleryImagesSelected(e: Event): void {
    const files = Array.from((e.target as HTMLInputElement).files ?? []);
    (e.target as HTMLInputElement).value = '';
    if (files.length === 0) return;

    this.form.galleryImageFiles = [...(this.form.galleryImageFiles ?? []), ...files];

    for (const file of files) {
      const reader = new FileReader();
      reader.onload = () => {
        const url = reader.result as string;
        this.galleryPreviewUrls.update(urls => [...urls, url]);
        this.form.galleryImageUrls = [...this.form.galleryImageUrls, url];
      };
      reader.readAsDataURL(file);
    }
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
      const data = await firstValueFrom(this.mapApi.reverseGeocode(confirmedLat, confirmedLng));
      if (data.address) this.form.address = data.address;
      if (data.city) this.form.city = data.city;
      if (data.region) this.form.region = data.region;
      if (data.country) this.form.country = data.country;
    } catch {
    } finally {
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
    if (!this.locationLocked() && this.requiresOwnershipProof() && !this.form.ownershipProofFile && !this.form.ownershipProofDocumentUrl) return false;
    return true;
  }

  hasConfirmedLocation(): boolean {
    return this.hasUsableCoordinates(this.form.lat, this.form.lng);
  }

  isAccommodationSelected(): boolean {
    return this.form.placeType === 'Stay';
  }

  requiresOwnershipProof(): boolean {
    const selectedType = this.selectedPlaceType();
    return selectedType?.isOwnable ?? FALLBACK_OWNABLE_PLACE_TYPES.has(normalizePlaceType(this.form.placeType));
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
          }));

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

  onContinue(): void {
    this.submitAttempted.set(true);
    if (!this.isValid()) return;

    this.form.imgBgPosition = this.bgPosition();
    this.form.imgBgSize = this.bgSize();
    this.saved.emit({ ...this.form });
  }
  imgContainer = viewChild<ElementRef<HTMLDivElement>>('imgContainer');
  private readonly pickerMapContainer = viewChild<ElementRef<HTMLDivElement>>('coordMap');
  bgPosition = signal('50% 50%');
  bgSize = signal('cover');

  private touchState: {
    startX: number; startY: number;
    startPosX: number; startPosY: number;
    startDist: number; startSize: number;
  } | null = null;

  private boundTouchStart!: (e: TouchEvent) => void;
  private boundTouchMove!: (e: TouchEvent) => void;
  private boundTouchEnd!: () => void;

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

  ngOnDestroy() {
    this.destroyPickerMap();
    const el = this.imgContainer()?.nativeElement;
    if (el && this.boundTouchStart) {
      el.removeEventListener('touchstart', this.boundTouchStart);
      el.removeEventListener('touchmove', this.boundTouchMove);
      el.removeEventListener('touchend', this.boundTouchEnd);
    }
  }
}

function defaultPlaceTypes(): ContributionPlaceTypeOption[] {
  return [
    { id: 0, name: 'Landmark', description: null, isOwnable: false },
    { id: 0, name: 'Restaurant', description: null, isOwnable: true },
    { id: 0, name: 'Cafe', description: null, isOwnable: true },
    { id: 0, name: 'Nature', description: null, isOwnable: false },
    { id: 0, name: 'Beach', description: null, isOwnable: false },
    { id: 0, name: 'Stay', description: null, isOwnable: true },
  ];
}

function normalizePlaceType(value: string | null | undefined): string {
  return (value ?? '').trim().toLowerCase();
}

function isPdfFile(file: File): boolean {
  return file.type === 'application/pdf' || file.name.trim().toLowerCase().endsWith('.pdf');
}
