import { Component, input, output, signal, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import maplibregl, { Map as MapLibreMap } from 'maplibre-gl';

export interface BasicsData {
  name: string;
  stayType: string;
  address: string;
  city: string;
  region: string;
  country: string;
  imageUrl: string;
  lat: number | null;
  lng: number | null;
}

@Component({
  selector: 'app-step-basics',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './step-basics.component.html',
})
export class StepBasicsComponent implements OnInit, OnDestroy {
  data = input<BasicsData | null>(null);
  saved = output<BasicsData>();

  stayTypes = ['Hotel', 'Villa', 'Apartment', 'Resort', 'Cabin', 'Glamping', 'Hostel'];
  previewUrl = '';
  showMapPicker = signal(false);
  pickerLat = signal<number | null>(null);
  pickerLng = signal<number | null>(null);
  isGeocoding = signal(false);
  private pickerMap?: MapLibreMap;

  form: BasicsData = {
    name: '', stayType: 'Hotel', address: '', city: '',
    region: '', country: '', imageUrl: '', lat: null, lng: null
  };

  ngOnInit() {
    if (this.data()) Object.assign(this.form, this.data());
    if (this.form.lat) {
      this.pickerLat.set(this.form.lat);
      this.pickerLng.set(this.form.lng);
    }
  }

  ngOnDestroy() { this.pickerMap?.remove(); }

  onImageSelected(e: Event): void {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      this.previewUrl = reader.result as string;
      this.form.imageUrl = this.previewUrl;
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
    const center: [number, number] = lat ? [lng!, lat] : [20.46, 44.82];

    this.pickerMap = new maplibregl.Map({
      container,
      style: '/map-style.json',
      center,
      zoom: lat ? 13 : 6,
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
  }

  async confirmCoordinates(): Promise<void> {
    const lat = this.pickerLat();
    const lng = this.pickerLng();
    if (lat === null || lng === null) return;

    this.form.lat = lat;
    this.form.lng = lng;
    this.isGeocoding.set(true);

    try {
      const res = await fetch(
        `https://nominatim.openstreetmap.org/reverse?lat=${lat}&lon=${lng}&format=json`
      );
      const data = await res.json();
      const a = data.address ?? {};
      this.form.address = [a.road, a.house_number].filter(Boolean).join(' ');
      this.form.city    = a.city ?? a.town ?? a.village ?? a.municipality ?? '';
      this.form.region  = a.state ?? a.county ?? '';
      this.form.country = a.country ?? '';
    } catch {
      // geocoding nije uspeo, polja ostaju kako jesu
    } finally {
      this.isGeocoding.set(false);
      this.pickerMap?.remove();
      this.pickerMap = undefined;
      this.showMapPicker.set(false);
    }
  }  // ← ova zagrada je nedostajala

  isValid(): boolean {
    return !!this.form.name.trim() && !!this.form.city.trim() && !!this.form.country.trim();
  }

  onContinue(): void {
    if (this.isValid()) this.saved.emit({ ...this.form });
  }
}