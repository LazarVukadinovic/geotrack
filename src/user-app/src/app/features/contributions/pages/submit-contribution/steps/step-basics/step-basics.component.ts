import { Component, input, output, signal, OnDestroy, effect, ElementRef, viewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import maplibregl, { Map as MapLibreMap } from 'maplibre-gl';
import { firstValueFrom } from 'rxjs';
import { MapApiService } from '../../../../../../core/services/map-api.service';

export interface BasicsData {
  name: string;
  stayType: string;
  address: string;
  city: string;
  region: string;
  country: string;
  imageUrl: string;
  coverImageFile?: File | null;
  lat: number | null;
  lng: number | null;
  imgBgPosition: string;
  imgBgSize: string;
}

@Component({
  selector: 'app-step-basics',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './step-basics.component.html',
})
export class StepBasicsComponent implements OnDestroy {
  private readonly mapApi = inject(MapApiService);
  data = input<BasicsData | null>(null);
  saved = output<BasicsData>();

  stayTypes = ['Hotel', 'Villa', 'Apartment', 'Resort', 'Cabin', 'Glamping', 'Hostel'];
  previewUrl = signal('');
  showMapPicker = signal(false);
  pickerLat = signal<number | null>(null);
  pickerLng = signal<number | null>(null);
  isGeocoding = signal(false);
  private pickerMap?: MapLibreMap;

  form: BasicsData = {
    name: '', stayType: 'Hotel', address: '', city: '',
    region: '', country: '', imageUrl: '', coverImageFile: null, lat: null, lng: null,
    imgBgPosition: '50% 50%',
    imgBgSize: 'cover'
  };

  onImageSelected(e: Event): void {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      this.previewUrl.set(reader.result as string);
      this.form.imageUrl = this.previewUrl();
      this.form.coverImageFile = file;
    };
    reader.readAsDataURL(file);
  }

  openMapPicker(): void {
    this.pickerLat.set(this.form.lat);
    this.pickerLng.set(this.form.lng);
    this.pickerMap?.remove();
    this.pickerMap = undefined;
    this.showMapPicker.set(true);
    setTimeout(() => this.initPickerMap(), 150);
  }

  private initPickerMap(): void {
    const container = document.getElementById('coord-map');
    if (!container || this.pickerMap) return;

    const lat = this.pickerLat();
    const lng = this.pickerLng();
    const hasCoordinates = lat !== null && lng !== null;
    const center: [number, number] = hasCoordinates ? [lng, lat] : [20.46, 44.82];

    this.pickerMap = new maplibregl.Map({
      container,
      style: '/map-style.json',
      center,
      zoom: hasCoordinates ? 13 : 6,
      attributionControl: false,
      fadeDuration: 0,
    });

    this.pickerMap.on('load', () => {
      const c = this.pickerMap!.getCenter();
      this.pickerLat.set(c.lat);
      this.pickerLng.set(c.lng);
    });

    this.pickerMap.on('move', () => {
      const c = this.pickerMap!.getCenter();
      this.pickerLat.set(c.lat);
      this.pickerLng.set(c.lng);
    });

    this.pickerMap.on('click', (event) => {
      this.pickerLat.set(event.lngLat.lat);
      this.pickerLng.set(event.lngLat.lng);
      this.pickerMap!.easeTo({ center: event.lngLat, duration: 200 });
    });
  }

  async confirmCoordinates(): Promise<void> {
    const lat = this.pickerLat();
    const lng = this.pickerLng();
    if (lat === null || lng === null) return;

    this.form.lat = lat;
    this.form.lng = lng;
    this.isGeocoding.set(true);

    try {
      const data = await firstValueFrom(this.mapApi.reverseGeocode(lat, lng));
      this.form.address = data.address ?? '';
      this.form.city = data.city ?? '';
      this.form.region = data.region ?? '';
      this.form.country = data.country ?? '';
    } catch {
    } finally {
      this.isGeocoding.set(false);
      this.pickerMap?.remove();
      this.pickerMap = undefined;
      this.showMapPicker.set(false);
    }
  }

  isValid(): boolean {
    return !!this.form.name.trim() && !!this.form.city.trim() && !!this.form.country.trim();
  }

  onContinue(): void {
    if (!this.isValid()) return;
    this.form.imgBgPosition = this.bgPosition();
    this.form.imgBgSize = this.bgSize();
    this.saved.emit({ ...this.form });
  }
  imgContainer = viewChild<ElementRef<HTMLDivElement>>('imgContainer');
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
        Object.assign(this.form, d);
        if (d.imageUrl) this.previewUrl.set(d.imageUrl);
        this.form.coverImageFile = d.coverImageFile ?? null;
        if (d.lat) {
          this.pickerLat.set(d.lat);
          this.pickerLng.set(d.lng);
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
      if (!this.previewUrl()) return;
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
      if (!this.touchState || !this.previewUrl()) return;
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
    this.pickerMap?.remove();
    const el = this.imgContainer()?.nativeElement;
    if (el && this.boundTouchStart) {
      el.removeEventListener('touchstart', this.boundTouchStart);
      el.removeEventListener('touchmove', this.boundTouchMove);
      el.removeEventListener('touchend', this.boundTouchEnd);
    }
  }
}
