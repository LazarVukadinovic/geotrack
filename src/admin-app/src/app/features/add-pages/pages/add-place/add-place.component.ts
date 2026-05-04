import { Component, ElementRef, OnDestroy, OnInit, ViewChild, AfterViewInit, NgZone, ChangeDetectorRef } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { ActivatedRoute } from '@angular/router';
import { Router } from '@angular/router';
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

  constructor(private zone: NgZone, private cdr: ChangeDetectorRef, private route: ActivatedRoute, private router: Router) {}

  //ViewChild refs
  @ViewChild('editorRef')         editorRef!:            ElementRef<HTMLDivElement>;
  @ViewChild('miniMapContainer')  miniMapContainer!:     ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  //Rich text editor
  isBold        = false;
  isItalic      = false;
  isUnderline   = false;
  editorIsEmpty = true;

  execFormat(command: 'bold' | 'italic' | 'underline'): void {
    document.execCommand(command, false, undefined);
    this.editorRef.nativeElement.focus();
    this.syncToolbarState();
  }

  onEditorChange(): void {
    this.syncToolbarState();
    this.syncEmptyState();
  }
  onEditorFocus(): void {
    this.syncToolbarState();
    this.syncEmptyState();
  }
  onEditorBlur(): void {
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
    this.editorIsEmpty = text.trim() === '' && el.innerHTML !== '<img' && !el.querySelector('img');
  }

  private syncToolbarState(): void {
    this.isBold      = document.queryCommandState('bold');
    this.isItalic    = document.queryCommandState('italic');
    this.isUnderline = document.queryCommandState('underline');
  }

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

  //Cover image
  coverImageUrl : string | null = null;
  coverImageName: string        = '';

  onCoverImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file  = input.files?.[0];
    if (!file) return;
    this.coverImageUrl  = URL.createObjectURL(file);
    this.coverImageName = file.name;
    input.value = '';
  }

  removeCoverImage(): void {
    if (this.coverImageUrl) URL.revokeObjectURL(this.coverImageUrl);
    this.coverImageUrl  = null;
    this.coverImageName = '';
  }

  //Gallery
  galleryImages: { url: string; name: string }[] = [];

  onGalleryImagesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    for (const file of files) {
      this.galleryImages.push({ url: URL.createObjectURL(file), name: file.name });
    }
    input.value = '';
  }

  removeGalleryImage(index: number): void {
    URL.revokeObjectURL(this.galleryImages[index].url);
    this.galleryImages.splice(index, 1);
  }

  //Classification
  classItems: ClassItem[] = [
    { label: 'Coastal & Marine',     checked: false },
    { label: 'Mountain & Forest',    checked: false },
    { label: 'Urban Center',         checked: false },
    { label: 'Historical Site',      checked: false },
    { label: 'National Park Access', checked: false },
  ];

  //Tags
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

  //Geographic pin / Leaflet
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

  //Lightbox
  private lightboxEl: HTMLElement | null = null;
  private _lightboxKeyHandler: ((e: KeyboardEvent) => void) | null = null;
  private _currentImages: { url: string; name: string }[] = [];
  private _currentIndex  = 0;

  openLightbox(images: { url: string; name: string }[], index: number): void {
    this.zone.run(() => {
      this.closeLightbox();
      this._currentImages = [...images];
      this._currentIndex  = index;
      this._renderLightbox();

      this._lightboxKeyHandler = (e: KeyboardEvent) => {
        if (e.key === 'Escape')     { this.closeLightbox(); }
        if (e.key === 'ArrowRight') {
          this._currentIndex = (this._currentIndex + 1) % this._currentImages.length;
          this._renderLightbox();
        }
        if (e.key === 'ArrowLeft') {
          this._currentIndex = (this._currentIndex - 1 + this._currentImages.length) % this._currentImages.length;
          this._renderLightbox();
        }
      };
      document.addEventListener('keydown', this._lightboxKeyHandler);
      document.body.style.overflow = 'hidden';
    });
  }

  private _renderLightbox(): void {
    if (this.lightboxEl) document.body.removeChild(this.lightboxEl);

    const imgs   = this._currentImages;
    const idx    = this._currentIndex;
    const single = imgs.length === 1;

    const overlay = document.createElement('div');
    overlay.style.cssText =
      'position:fixed;inset:0;z-index:99999;background:rgba(0,0,0,0.92);' +
      'display:flex;align-items:center;justify-content:center;';
    overlay.addEventListener('click', () => this.closeLightbox());

    const closeBtn = document.createElement('button');
    closeBtn.innerHTML = '&times;';
    closeBtn.style.cssText =
      'position:absolute;top:16px;right:16px;width:40px;height:40px;border-radius:50%;' +
      'background:rgba(255,255,255,0.15);border:none;color:white;font-size:24px;' +
      'cursor:pointer;display:flex;align-items:center;justify-content:center;z-index:1;';
    closeBtn.addEventListener('click', (e) => { e.stopPropagation(); this.closeLightbox(); });
    overlay.appendChild(closeBtn);

    if (!single) {
      const counter = document.createElement('div');
      counter.textContent = `${idx + 1} / ${imgs.length}`;
      counter.style.cssText =
        'position:absolute;top:16px;left:50%;transform:translateX(-50%);color:white;' +
        'font-size:14px;font-weight:500;background:rgba(0,0,0,0.4);' +
        'padding:4px 12px;border-radius:20px;z-index:1;';
      overlay.appendChild(counter);
    }

    if (!single) {
      const prev = document.createElement('button');
      prev.innerHTML = '&#8249;';
      prev.style.cssText =
        'position:absolute;left:16px;top:50%;transform:translateY(-50%);width:44px;height:44px;' +
        'border-radius:50%;background:rgba(255,255,255,0.15);border:none;color:white;' +
        'font-size:28px;cursor:pointer;display:flex;align-items:center;justify-content:center;z-index:1;';
      prev.addEventListener('click', (e) => {
        e.stopPropagation();
        this._currentIndex = (idx - 1 + imgs.length) % imgs.length;
        this._renderLightbox();
      });
      overlay.appendChild(prev);
    }

    const img = document.createElement('img');
    img.src = imgs[idx].url;
    img.alt = imgs[idx].name;
    img.style.cssText =
      'max-width:88vw;max-height:80vh;object-fit:contain;' +
      'border-radius:12px;box-shadow:0 25px 60px rgba(0,0,0,0.5);';
    img.addEventListener('click', (e) => e.stopPropagation());
    overlay.appendChild(img);

    if (!single) {
      const next = document.createElement('button');
      next.innerHTML = '&#8250;';
      next.style.cssText =
        'position:absolute;right:16px;top:50%;transform:translateY(-50%);width:44px;height:44px;' +
        'border-radius:50%;background:rgba(255,255,255,0.15);border:none;color:white;' +
        'font-size:28px;cursor:pointer;display:flex;align-items:center;justify-content:center;z-index:1;';
      next.addEventListener('click', (e) => {
        e.stopPropagation();
        this._currentIndex = (idx + 1) % imgs.length;
        this._renderLightbox();
      });
      overlay.appendChild(next);
    }

    if (!single) {
      const thumbRow = document.createElement('div');
      thumbRow.style.cssText =
        'position:absolute;bottom:16px;left:50%;transform:translateX(-50%);' +
        'display:flex;gap:8px;z-index:1;';
      thumbRow.addEventListener('click', (e) => e.stopPropagation());

      imgs.forEach((im, ti) => {
        const thumb = document.createElement('div');
        thumb.style.cssText =
          `width:48px;height:48px;border-radius:8px;overflow:hidden;cursor:pointer;` +
          `border:2px solid ${ti === idx ? 'white' : 'transparent'};` +
          `opacity:${ti === idx ? '1' : '0.5'};transition:all 0.15s;`;
        const tImg = document.createElement('img');
        tImg.src = im.url;
        tImg.style.cssText = 'width:100%;height:100%;object-fit:cover;';
        tImg.addEventListener('click', (e) => {
          e.stopPropagation();
          this._currentIndex = ti;
          this._renderLightbox();
        });
        thumb.appendChild(tImg);
        thumbRow.appendChild(thumb);
      });
      overlay.appendChild(thumbRow);
    }

    document.body.appendChild(overlay);
    this.lightboxEl = overlay;
  }

  closeLightbox(): void {
    if (this.lightboxEl) {
      document.body.removeChild(this.lightboxEl);
      this.lightboxEl = null;
    }
    if (this._lightboxKeyHandler) {
      document.removeEventListener('keydown', this._lightboxKeyHandler);
      this._lightboxKeyHandler = null;
    }
    document.body.style.overflow = '';
  }
  isEditMode = false;
  editPlaceId: string | null = null;
  //Lifecycle
  ngOnInit(): void {
     const nav = history.state?.place;
    if (nav) {
      this.isEditMode = true;
      this.editPlaceId = nav.id;
      this.populateFromPlace(nav);
    } else {
      this.route.paramMap.subscribe(params => {
        const id = params.get('id');
        if (id) {
          this.isEditMode = true;
          this.editPlaceId = id;
          // Ovde bi išao servis lookup
        }
      });
    }
  }
  placeName = '';
  private populateFromPlace(place: any): void {
    this.placeName = place.name ?? '';
    this.placeType = place.category ?? '';
    this.pinLat    = place.lat?.toFixed(6) ?? '';
    this.pinLng    = place.lng?.toFixed(6) ?? '';

    setTimeout(() => {
      if (place.lat && place.lng) {
        this.updateMiniMapPin(place.lat, place.lng);
        this.pinConfirmed = true;
      }
    }, 200);
  }
  ngAfterViewInit(): void {
    setTimeout(() => this.initMiniMap(), 0);
  }

  ngOnDestroy(): void {
    this.destroyModalMap();
    this.closeLightbox();
    this.galleryImages.forEach(img => URL.revokeObjectURL(img.url));
    if (this.coverImageUrl) URL.revokeObjectURL(this.coverImageUrl);
    if (this.miniMap) { this.miniMap.remove(); this.miniMap = null; }
    if (this._geocodeDebounce) clearTimeout(this._geocodeDebounce);
  }
  
}