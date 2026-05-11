import { Component, ElementRef, OnDestroy, OnInit, ViewChild, AfterViewInit, NgZone, ChangeDetectorRef } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { ActivatedRoute } from '@angular/router';
import { Router } from '@angular/router';
import { PlacesService } from '../../../../core/services/places.service';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { PlacesListService } from '../../../../core/services/places-list.service';
import { MapPlacesService } from '../../../../core/services/map-places.service';
declare const L: any;

interface ClassItem {
  label: string;
  checked: boolean;
}

interface DayHours {
  name: string;
  open: boolean;
  openTime: string;
  closeTime: string;
}

@Component({
  selector: 'app-new-place',
  standalone: true,
  imports: [CommonModule, FormsModule, DecimalPipe, SidebarComponent, TopbarComponent],
  templateUrl: './add-place.component.html',
  styles: [`
    .rte-field { position: relative; }
    .rte-ph {
      position: absolute;
      top: 0; left: 0; right: 0;
      padding: 0.5rem 0.75rem;
      color: #9ca3af;
      font-size: 0.875rem;
      pointer-events: none;
      user-select: none;
      line-height: 1.5rem;
    }
  `]
})
export class NewPlaceComponent implements OnInit, AfterViewInit, OnDestroy {

  constructor(
    private zone: NgZone,
    private cdr: ChangeDetectorRef,
    private route: ActivatedRoute,
    private router: Router,
    private placesService: PlacesService,
    private lightboxService: LightboxService,
    private placesListSvc: PlacesListService,
    private mapPlacesService: MapPlacesService
  ) {}

  @ViewChild('editorRef')         editorRef!:            ElementRef<HTMLDivElement>;
  @ViewChild('miniMapContainer')  miniMapContainer!:     ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  // Rich text editor
  isBold        = false;
  isItalic      = false;
  isUnderline   = false;
  editorIsEmpty = true;

  execFormat(command: 'bold' | 'italic' | 'underline'): void {
    document.execCommand(command, false, undefined);
    this.editorRef.nativeElement.focus();
    this.syncToolbarState();
  }

  onEditorChange(): void { this.syncToolbarState(); this.syncEmptyState(); }
  onEditorFocus():  void { this.syncToolbarState(); this.syncEmptyState(); }
  onEditorBlur():   void {
    this.syncToolbarState();
    const el = this.editorRef?.nativeElement;
    if (el && (el.innerHTML === '<br>' || el.innerHTML === '&nbsp;' || el.innerHTML.trim() === '')) {
      el.innerHTML = '';
    }
    this.syncEmptyState();
  }

  private syncEmptyState(): void {
    const el = this.editorRef?.nativeElement;
    if (!el) return;
    const text = el.innerText ?? el.textContent ?? '';
    this.editorIsEmpty = text.trim() === '' && !el.querySelector('img');
  }

  private syncToolbarState(): void {
    this.isBold      = document.queryCommandState('bold');
    this.isItalic    = document.queryCommandState('italic');
    this.isUnderline = document.queryCommandState('underline');
  }

  // Fields
  placeName  = '';
  placeType  = 'Museum';
  opsEnabled = false;

  allHours: string[] = [
    '12:00 AM','01:00 AM','02:00 AM','03:00 AM','04:00 AM','05:00 AM',
    '06:00 AM','07:00 AM','08:00 AM','09:00 AM','10:00 AM','11:00 AM',
    '12:00 PM','01:00 PM','02:00 PM','03:00 PM','04:00 PM','05:00 PM',
    '06:00 PM','07:00 PM','08:00 PM','09:00 PM','10:00 PM','11:00 PM',
  ];

  days: DayHours[] = [
    { name: 'Monday',    open: true,  openTime: '09:00 AM', closeTime: '05:00 PM' },
    { name: 'Tuesday',   open: true,  openTime: '09:00 AM', closeTime: '05:00 PM' },
    { name: 'Wednesday', open: true,  openTime: '09:00 AM', closeTime: '05:00 PM' },
    { name: 'Thursday',  open: true,  openTime: '09:00 AM', closeTime: '05:00 PM' },
    { name: 'Friday',    open: true,  openTime: '09:00 AM', closeTime: '05:00 PM' },
    { name: 'Saturday',  open: true,  openTime: '10:00 AM', closeTime: '08:00 PM' },
    { name: 'Sunday',    open: false, openTime: '09:00 AM', closeTime: '05:00 PM' },
  ];

  // Cover image
  coverImageUrl : string | null = null;
  coverImageName: string        = '';

  onCoverImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file  = input.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      this.zone.run(() => {
        this.coverImageUrl  = reader.result as string;
        this.coverImageName = file.name;
        this.cdr.detectChanges();
      });
    };
    reader.readAsDataURL(file);
    input.value = '';
  }

  removeCoverImage(): void {
    this.coverImageUrl  = null;
    this.coverImageName = '';
  }

  // Gallery
  galleryImages: { url: string; name: string }[] = [];

  onGalleryImagesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    for (const file of files) {
      const reader = new FileReader();
      reader.onload = () => {
        this.zone.run(() => {
          this.galleryImages = [...this.galleryImages, { url: reader.result as string, name: file.name }];
          this.cdr.detectChanges();
        });
      };
      reader.readAsDataURL(file);
    }
    input.value = '';
  }

  removeGalleryImage(index: number): void {
    this.galleryImages.splice(index, 1);
  }

  openLightbox(images: { url: string; name: string }[], index: number): void {
    this.lightboxService.open(images, index);
  }

  // Classification
  classItems: ClassItem[] = [
    { label: 'Coastal & Marine',     checked: false },
    { label: 'Mountain & Forest',    checked: false },
    { label: 'Urban Center',         checked: false },
    { label: 'Historical Site',      checked: false },
    { label: 'National Park Access', checked: false },
  ];

  // Tags
  tags    : string[] = [];
  tagInput: string   = '';

  addTag(event: Event): void {
    event.preventDefault();
    const value = this.tagInput.trim();
    if (!value || this.tags.includes(value)) { this.tagInput = ''; return; }
    this.tags     = [...this.tags, value];
    this.tagInput = '';
  }

  removeTag(tag: string): void {
    this.tags = this.tags.filter(t => t !== tag);
  }

  // Geographic pin / Leaflet
  pinModalOpen  = false;
  pinConfirmed  = false;

  pinLat: string = '';
  pinLng: string = '';
  streetAddress: string = '';
  isGeocodingAddress = false;

  modalPinLat: number | null = null;
  modalPinLng: number | null = null;

  private miniMap    : any = null;
  private modalMap   : any = null;
  private miniMarker : any = null;
  private modalMarker: any = null;
  private _geocodeDebounce: any = null;

  private makePinIcon(): any {
    return L.divIcon({
      html: `<svg width="24" height="32" viewBox="0 0 24 32" xmlns="http://www.w3.org/2000/svg">
               <path d="M12 0C5.373 0 0 5.373 0 12c0 8 12 20 12 20S24 20 24 12C24 5.373 18.627 0 12 0z"
                     fill="#2563eb" stroke="white" stroke-width="1.5"/>
               <circle cx="12" cy="12" r="4" fill="white"/>
             </svg>`,
      iconSize:   [24, 32],
      iconAnchor: [12, 32],
      className:  '',
    });
  }

  private initMiniMap(): void {
    if (this.miniMap) return;
    const el = this.miniMapContainer?.nativeElement;
    if (!el) return;

    this.miniMap = L.map(el, {
      zoomControl: false, dragging: false, scrollWheelZoom: false,
      doubleClickZoom: false, touchZoom: false, attributionControl: false,
    }).setView([44.0, 20.9], 8);

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
    }).addTo(this.miniMap);
  }

  openPinModal(): void {
    this.pinModalOpen = true;

    const lat = parseFloat(this.pinLat);
    const lng = parseFloat(this.pinLng);
    if (!isNaN(lat) && !isNaN(lng)) {
      this.modalPinLat = lat;
      this.modalPinLng = lng;
    } else {
      this.modalPinLat = null;
      this.modalPinLng = null;
    }

    setTimeout(() => this.initModalMap(this.modalMapContainerRef.nativeElement), 50);
  }

  closePinModal(): void {
    this.pinModalOpen = false;
    this.destroyModalMap();
  }

  onPinModalBackdropClick(_event: MouseEvent): void {
    this.closePinModal();
  }

  private initModalMap(el: HTMLDivElement): void {
    if (this.modalMap) return;

    const center: [number, number] = (this.modalPinLat !== null && this.modalPinLng !== null)
      ? [this.modalPinLat, this.modalPinLng]
      : [44.0, 20.9];
    const zoom = (this.modalPinLat !== null) ? 14 : 9;

    this.modalMap = L.map(el, {
      zoomControl: true,
      attributionControl: false,
    }).setView(center, zoom);

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
    }).addTo(this.modalMap);

    setTimeout(() => this.modalMap?.invalidateSize(), 200);

    if (this.modalPinLat !== null && this.modalPinLng !== null) {
      this.modalMarker = L.marker([this.modalPinLat, this.modalPinLng], {
        icon: this.makePinIcon(),
        draggable: true,
      }).addTo(this.modalMap);

      this.modalMarker.on('dragend', (e: any) => {
        const pos = e.target.getLatLng();
        this.zone.run(() => {
          this.modalPinLat = pos.lat;
          this.modalPinLng = pos.lng;
        });
      });
    }

    this.modalMap.on('click', (e: any) => {
      this.zone.run(() => {
        this.modalPinLat = e.latlng.lat;
        this.modalPinLng = e.latlng.lng;
      });

      if (this.modalMarker) {
        this.modalMarker.setLatLng(e.latlng);
      } else {
        this.modalMarker = L.marker(e.latlng, {
          icon: this.makePinIcon(),
          draggable: true,
        }).addTo(this.modalMap);

        this.modalMarker.on('dragend', (ev: any) => {
          const pos = ev.target.getLatLng();
          this.zone.run(() => {
            this.modalPinLat = pos.lat;
            this.modalPinLng = pos.lng;
          });
        });
      }
    });
  }

  private destroyModalMap(): void {
    if (this.modalMap) {
      this.modalMap.remove();
      this.modalMap    = null;
      this.modalMarker = null;
    }
  }

  confirmPin(): void {
    if (this.modalPinLat === null || this.modalPinLng === null) {
      alert('Please click on the map to place a pin first.');
      return;
    }

    this.pinLat = this.modalPinLat.toFixed(6);
    this.pinLng = this.modalPinLng.toFixed(6);
    this.pinConfirmed = true;

    this.updateMiniMapPin(this.modalPinLat, this.modalPinLng);
    this.reverseGeocode(this.modalPinLat, this.modalPinLng);
    this.closePinModal();
  }

  onCoordInputChange(): void {
    const lat = parseFloat(this.pinLat);
    const lng = parseFloat(this.pinLng);
    if (isNaN(lat) || isNaN(lng)) return;
    this.pinConfirmed = true;
    this.updateMiniMapPin(lat, lng);

    if (this._geocodeDebounce) clearTimeout(this._geocodeDebounce);
    this._geocodeDebounce = setTimeout(() => this.reverseGeocode(lat, lng), 300);
  }

  private async reverseGeocode(lat: number, lng: number): Promise<void> {
    this.isGeocodingAddress = true;
    try {
      const url = `https://nominatim.openstreetmap.org/reverse?lat=${lat}&lon=${lng}&format=json&addressdetails=1`;
      const res = await fetch(url, {
        headers: { 'Accept-Language': 'en', 'User-Agent': 'TourismCMS/1.0' }
      });
      if (!res.ok) return;
      const data = await res.json();
      if (data && data.display_name) {
        this.zone.run(() => {
          const a = data.address || {};
          const parts: string[] = [];
          if (a.road)         parts.push(a.road + (a.house_number ? ' ' + a.house_number : ''));
          if (a.city || a.town || a.village || a.municipality)
            parts.push(a.city ?? a.town ?? a.village ?? a.municipality);
          if (a.postcode)     parts.push(a.postcode);
          if (a.country)      parts.push(a.country);
          this.streetAddress = parts.length ? parts.join(', ') : data.display_name;
          this.cdr.detectChanges();
        });
      }
    } catch (_) {
    } finally {
      this.zone.run(() => {
        this.isGeocodingAddress = false;
        this.cdr.detectChanges();
      });
    }
  }

  private updateMiniMapPin(lat: number, lng: number): void {
    if (!this.miniMap) this.initMiniMap();
    if (!this.miniMap) return;

    if (this.miniMarker) {
      this.miniMarker.setLatLng([lat, lng]);
    } else {
      this.miniMarker = L.marker([lat, lng], {
        icon: this.makePinIcon(),
        draggable: false,
      }).addTo(this.miniMap);
    }

    this.miniMap.setView([lat, lng], 14);
  }

  // Lifecycle

  isEditMode   = false;
  editPlaceId: string | null = null;
  private _pendingDescription = '';

  ngOnInit(): void {
    const state = history.state as any;

    if (state?.place) {
      this.isEditMode    = true;
      this.editPlaceId   = state.place.id ?? null;
      this._pendingDescription = state.place.description ?? '';
      this.populateFromPlace(state.place);

    } else if (state?.droppedPin) {
      const pin = state.droppedPin;
      this.pinLat       = pin.lat.toFixed(6);
      this.pinLng       = pin.lng.toFixed(6);
      this.pinConfirmed = true;
      setTimeout(() => {
        this.updateMiniMapPin(pin.lat, pin.lng);
        this.reverseGeocode(pin.lat, pin.lng);
      }, 200);

    } else {
      this.route.paramMap.subscribe(params => {
        const id = params.get('id');
        if (id) {
          this.isEditMode  = true;
          this.editPlaceId = id;
          const found = this.placesListSvc.findById(id);
          if (found) {
            this._pendingDescription = (found as any).description ?? '';
            this.populateFromPlace(found);
          }
        }
      });
    }
  }

  private populateFromPlace(place: any): void {
    this.placeName     = place.name     ?? '';
    this.placeType     = place.category ?? '';
    this.streetAddress = place.address  ?? '';
    this.pinLat        = place.lat?.toFixed(6) ?? '';
    this.pinLng        = place.lng?.toFixed(6) ?? '';

    if (place.coverImageUrl) {
      this.coverImageUrl  = place.coverImageUrl;
      this.coverImageName = place.coverImageName ?? 'cover';
    }

    if (Array.isArray(place.galleryImages)) {
      this.galleryImages = [...place.galleryImages];
    }

    if (Array.isArray(place.hours)) {
      this.days = place.hours.map((h: any) => ({ ...h }));
    }

    if (Array.isArray(place.classification)) {
      this.classItems = this.classItems.map(c => ({
        ...c,
        checked: (place.classification as string[]).includes(c.label),
      }));
    }

    this.tags = Array.isArray(place.tags) ? [...place.tags] : [];

    setTimeout(() => {
      if (place.lat && place.lng) {
        this.updateMiniMapPin(place.lat, place.lng);
        this.pinConfirmed = true;
      }
    }, 200);
  }

  ngAfterViewInit(): void {
    setTimeout(() => {
      this.initMiniMap();
      if (this._pendingDescription && this.editorRef?.nativeElement) {
        this.editorRef.nativeElement.innerHTML = this._pendingDescription;
        this.syncEmptyState();
        this.cdr.detectChanges();
      }
    }, 0);
  }

  ngOnDestroy(): void {
    this.destroyModalMap();
    this.lightboxService.close();
    if (this.miniMap) { this.miniMap.remove(); this.miniMap = null; }
    if (this._geocodeDebounce) clearTimeout(this._geocodeDebounce);
  }

  isSaving = false;

  publish(): void {
    if (!this.pinConfirmed || !this.pinLat || !this.pinLng) {
      alert('Please pin the location on the map first.');
      return;
    }

    const lat = parseFloat(this.pinLat);
    const lng = parseFloat(this.pinLng);
    if (isNaN(lat) || isNaN(lng)) {
      alert('Invalid coordinates. Please re-pin the location.');
      return;
    }

    this.isSaving = true;
    const descriptionHtml = this.editorRef?.nativeElement?.innerHTML ?? '';
    const descriptionText = this.editorRef?.nativeElement?.innerText  ?? '';
    const classification  = this.classItems.filter(c => c.checked).map(c => c.label);

    if (this.isEditMode && this.editPlaceId) {
      this.placesListSvc.updateItem(this.editPlaceId, {
        name:           this.placeName || 'New Place',
        category:       this.placeType || 'Place',
        address:        this.streetAddress || undefined,
        lat,
        lng,
        status:         'Published',
        description:    descriptionHtml,
        summary:        descriptionText.slice(0, 160),
        classification: classification.length ? classification : undefined,
        tags:           this.tags.length ? this.tags : undefined,
        coverImageUrl:  this.coverImageUrl ?? undefined,
        coverImageName: this.coverImageName || undefined,
        galleryImages:  this.galleryImages.length ? [...this.galleryImages] : undefined,
        hours:          this.days.map(d => ({ ...d })),
        ...(this.coverImageUrl ? { imageBg: `url(${this.coverImageUrl}) center/cover` } : {}),
      });

      this.isSaving = false;
      const returnTo = history.state?.returnTo as string | undefined;
      this.router.navigate([returnTo ?? '/places']);
      return;
    }

    // ── ADD MODE ──
    const payload = {
      name:        this.placeName || 'New Place',
      type:        'Place' as const,
      category:    this.placeType || 'Place',
      description: descriptionHtml,
      status:      'Active',
      eventsToday: 0,
      lat,
      lng,
      address:     this.streetAddress,
    };

    this.placesService.add(payload).subscribe({
      next: (saved) => {
        this.isSaving = false;
        const returnTo = history.state?.returnTo as string | undefined;

        this.placesListSvc.addItem({
          id:            saved.id,
          name:          saved.name,
          summary:       descriptionText.slice(0, 160),
          region:        'Šumadija, Srbija',
          status:        'Published',
          lastUpdated:   new Date().toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }),
          imageBg:       this.coverImageUrl
                          ? `url(${this.coverImageUrl}) center/cover`
                          : 'linear-gradient(135deg,#2563eb 0%,#1d4ed8 100%)',
          category:      saved.category,
          lat:           saved.lat,
          lng:           saved.lng,
          address:       saved.address,
          description:   descriptionHtml,
          coverImageUrl: this.coverImageUrl ?? undefined,
          coverImageName: this.coverImageName || undefined,
          galleryImages: this.galleryImages.length ? [...this.galleryImages] : undefined,
          classification: classification.length ? classification : undefined,
          tags:          this.tags.length ? this.tags : undefined,
          hours:         this.days.map(d => ({ ...d })),
        });

        this.mapPlacesService.addPlace(saved);

        if (returnTo) {
          this.router.navigate([returnTo]);
        } else {
          this.router.navigate(['/map'], { state: { newPlace: saved } });
        }
      },
      error: () => {
        this.isSaving = false;
        alert('Failed to save. Please try again.');
      }
    });
  }
}