import { Component, ElementRef, OnDestroy, OnInit, ViewChild, AfterViewInit, NgZone } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { Router } from '@angular/router';
declare const L: any;

interface ClassItem {
  label: string;
  checked: boolean;
}

@Component({
  selector: 'app-add-destination',
  standalone: true,
  imports: [CommonModule, FormsModule, DecimalPipe, SidebarComponent, TopbarComponent],
  templateUrl: './add-destination.component.html',
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
export class AddDestinationComponent implements OnInit, AfterViewInit, OnDestroy {

  constructor(private zone: NgZone, private router: Router) {}

  //ViewChild refs
  @ViewChild('editorRef')         editorRef!:            ElementRef<HTMLDivElement>;
  @ViewChild('miniMapContainer')  miniMapContainer!:     ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  //Basic fields
  destinationName = '';

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
  //Geographic zone / Leaflet
  zoneModalOpen = false;
  zoneConfirmed = false;
  zoneAreaKm2: number | null = null;

  drawTool: 'polygon' | 'rectangle' | 'circle' = 'polygon';

  get drawHint(): string {
    const hints: Record<string, string> = {
      polygon:   'Click to add points. Double-click to close the polygon.',
      rectangle: 'Click and drag to draw a rectangle.',
      circle:    'Click the centre point, then drag to set the radius.',
    };
    return hints[this.drawTool];
  }

  private miniMap      : any = null;
  private modalMap     : any = null;
  private drawnLayer   : any = null;
  private confirmedLayer: any = null;
  private drawControl  : any = null;

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

  openZoneModal(): void {
    this.zoneModalOpen = true;
    setTimeout(() => this.initModalMap(this.modalMapContainerRef.nativeElement), 50);
  }

  closeZoneModal(): void {
    this.zoneModalOpen = false;
    this.destroyModalMap();
  }

  onModalBackdropClick(_event: MouseEvent): void {
    this.closeZoneModal();
  }

  private initModalMap(el: HTMLDivElement): void {
    if (this.modalMap) return;

    this.modalMap = L.map(el, {
      zoomControl: true,
      attributionControl: false,
    }).setView([44.0, 20.9], 9);

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
    }).addTo(this.modalMap);

    setTimeout(() => this.modalMap?.invalidateSize(), 200);

    if (this.confirmedLayer) {
      const layerCopy = this.cloneLayer(this.confirmedLayer);
      if (layerCopy) {
        layerCopy.addTo(this.modalMap);
        this.drawnLayer = layerCopy;
        this.modalMap.fitBounds(layerCopy.getBounds(), { padding: [30, 30] });
      }
    }

    this.activateDrawTool();
  }

  private destroyModalMap(): void {
    if (this.modalMap) {
      this.modalMap.remove();
      this.modalMap    = null;
      this.drawnLayer  = null;
      this.drawControl = null;
    }
  }

  setDrawTool(tool: 'polygon' | 'rectangle' | 'circle'): void {
    this.drawTool = tool;
    if (this.modalMap) { this.clearZone(); this.activateDrawTool(); }
  }

  private activateDrawTool(): void {
    if (!this.modalMap) return;

    if (this.drawControl) { this.drawControl.disable(); this.drawControl = null; }

    const shapeOptions = {
      color: '#2563eb', fillColor: '#3b82f6', fillOpacity: 0.2, weight: 2,
    };

    let handler: any;
    switch (this.drawTool) {
      case 'polygon':
        handler = new L.Draw.Polygon(this.modalMap, {
          shapeOptions, showArea: true, allowIntersection: false,
        });
        break;
      case 'rectangle':
        handler = new L.Draw.Rectangle(this.modalMap, { shapeOptions });
        break;
      case 'circle':
        handler = new L.Draw.Circle(this.modalMap, { shapeOptions });
        break;
    }

    if (handler) {
      this.drawControl = handler;
      handler.enable();

      this.modalMap.once('draw:created', (e: any) => {
        if (this.drawnLayer) this.modalMap.removeLayer(this.drawnLayer);
        this.drawnLayer = e.layer;
        this.modalMap.addLayer(this.drawnLayer);
      });
    }
  }

  clearZone(): void {
    if (this.drawnLayer && this.modalMap) {
      this.modalMap.removeLayer(this.drawnLayer);
      this.drawnLayer = null;
    }
    if (this.drawControl) { this.drawControl.disable(); this.drawControl = null; }
    this.activateDrawTool();
  }

  confirmZone(): void {
    if (!this.drawnLayer) {
      alert('Please draw a zone on the map first.');
      return;
    }

    let areaM2 = 0;

    if (this.drawnLayer instanceof L.Circle) {
      const r = this.drawnLayer.getRadius();
      areaM2  = Math.PI * r * r;
    } else if (
      this.drawnLayer instanceof L.Polygon ||
      this.drawnLayer instanceof L.Rectangle
    ) {
      if ((L as any).GeometryUtil?.geodesicArea) {
        areaM2 = (L as any).GeometryUtil.geodesicArea(
          this.drawnLayer.getLatLngs()[0],
        );
      } else {
        areaM2 = this.shoelaceAreaM2(this.drawnLayer.getLatLngs()[0]);
      }
    }

    this.zoneAreaKm2    = areaM2 / 1_000_000;
    this.zoneConfirmed  = true;
    this.confirmedLayer = this.drawnLayer;

    this.showZoneOnMiniMap();
    this.closeZoneModal();
  }

  private showZoneOnMiniMap(): void {
    if (!this.miniMap) this.initMiniMap();
    if (!this.miniMap || !this.confirmedLayer) return;

    this.miniMap.eachLayer((layer: any) => {
      if (layer._isConfirmedZone) this.miniMap.removeLayer(layer);
    });

    const clone = this.cloneLayer(this.confirmedLayer);
    if (!clone) return;

    (clone as any)._isConfirmedZone = true;
    clone.addTo(this.miniMap);

    try {
      this.miniMap.fitBounds(clone.getBounds(), { padding: [10, 10] });
    } catch {}
  }

  private cloneLayer(layer: any): any {
    const style = { color: '#2563eb', fillColor: '#3b82f6', fillOpacity: 0.25, weight: 2 };
    if (layer instanceof L.Circle)    return L.circle(layer.getLatLng(),    { ...style, radius: layer.getRadius() });
    if (layer instanceof L.Rectangle) return L.rectangle(layer.getBounds(), style);
    if (layer instanceof L.Polygon)   return L.polygon(layer.getLatLngs(),  style);
    return null;
  }

  private shoelaceAreaM2(latlngs: any[]): number {
    const flat = Array.isArray(latlngs[0]) ? latlngs[0] : latlngs;
    const R    = 6_371_000;
    let area   = 0;
    const n    = flat.length;
    for (let i = 0; i < n; i++) {
      const j    = (i + 1) % n;
      const lat1 = (flat[i].lat * Math.PI) / 180;
      const lat2 = (flat[j].lat * Math.PI) / 180;
      const lng1 = (flat[i].lng * Math.PI) / 180;
      const lng2 = (flat[j].lng * Math.PI) / 180;
      area      += (lng2 - lng1) * (2 + Math.sin(lat1) + Math.sin(lat2));
    }
    return Math.abs((area * R * R) / 2);
  }
  //Classification
    classItems: ClassItem[] = [
      { label: 'Nature & Outdoors', checked: false },
      { label: 'Culture & Heritage', checked: false },
      { label: 'Urban & City',       checked: false },
      { label: 'Adventure & Sport',  checked: false },
      { label: 'Family Friendly',    checked: false },
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

  //Lifecycle
  isEditMode = false;

  ngOnInit(): void {
    const state = history.state as { place?: any };
    if (state?.place) {
      this.isEditMode = true;
      this._loadFromRouterState(state.place);
    }
  }

  private _loadFromRouterState(p: any): void {
    this.destinationName = p.name ?? '';
    if (p.zone) {
      setTimeout(() => {
        if (!this.miniMap) this.initMiniMap();
        const polygon = L.polygon(p.zone, {
          color: '#2563eb', fillColor: '#3b82f6', fillOpacity: 0.25, weight: 2,
        });
        (polygon as any)._isConfirmedZone = true;
        polygon.addTo(this.miniMap);
        this.zoneConfirmed = true;
        this.confirmedLayer = polygon;
        try { this.miniMap.fitBounds(polygon.getBounds(), { padding: [10, 10] }); } catch {}
      }, 200);
    }
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
  }
  publish(): void {
    if (!this.zoneConfirmed || !this.confirmedLayer) {
      alert('Please draw a zone on the map first.');
      return;
    }

    let zone: [number, number][] = [];
    let centerLat: number;
    let centerLng: number;

    try {
      if (this.confirmedLayer instanceof L.Circle) {
        const center = this.confirmedLayer.getLatLng();
        const radius = this.confirmedLayer.getRadius();
        centerLat = center.lat;
        centerLng = center.lng;
        const points = 16;
        for (let i = 0; i < points; i++) {
          const angle = (i / points) * 2 * Math.PI;
          const dLat = (radius / 111320) * Math.cos(angle);
          const dLng = (radius / (111320 * Math.cos(center.lat * Math.PI / 180))) * Math.sin(angle);
          zone.push([center.lat + dLat, center.lng + dLng]);
        }
      } else if (this.confirmedLayer instanceof L.Rectangle) {
        const bounds = this.confirmedLayer.getBounds();
        centerLat = (bounds.getNorth() + bounds.getSouth()) / 2;
        centerLng = (bounds.getEast() + bounds.getWest()) / 2;
        zone = [
          [bounds.getNorth(), bounds.getWest()],
          [bounds.getNorth(), bounds.getEast()],
          [bounds.getSouth(), bounds.getEast()],
          [bounds.getSouth(), bounds.getWest()],
        ];
      } else {
        // Polygon
        const latlngs = this.confirmedLayer.getLatLngs();
        const flat = Array.isArray(latlngs[0]) ? latlngs[0] : latlngs;
        zone = (flat as any[]).map((ll: any) => [ll.lat, ll.lng] as [number, number]);
        const bounds = this.confirmedLayer.getBounds();
        centerLat = bounds.getCenter().lat;
        centerLng = bounds.getCenter().lng;
      }
    } catch (e) {
      alert('Could not read zone data. Please try drawing again.');
      return;
    }

    const newPlace = {
      id: Date.now().toString(),
      name: this.destinationName || 'New Destination',
      type: 'Destination' as const,
      category: 'Destination',
      description: '',
      status: 'Active',
      eventsToday: 0,
      lat: centerLat!,
      lng: centerLng!,
      zone,
    };
    this.router.navigate(['/map'], { state: { newPlace } });
  }
}