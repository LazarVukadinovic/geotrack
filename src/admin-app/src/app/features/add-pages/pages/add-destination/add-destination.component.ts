import { Component, ElementRef, OnDestroy, OnInit, ViewChild, AfterViewInit, NgZone, ChangeDetectorRef } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { Router } from '@angular/router';
import { DestinationsService } from '../../../../core/services/destinations.service';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { ZoneDrawService } from '../../../../core/services/zone-draw.service';
import { DestinationsListService } from '../../../../core/services/destinations-list.service';
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

  constructor(
    private zone: NgZone,
    private cdr: ChangeDetectorRef,
    private router: Router,
    private destinationsService: DestinationsService,
    private lightboxService: LightboxService,
    private zoneDrawService: ZoneDrawService,
    private destinationsListSvc: DestinationsListService
  ) {}

  @ViewChild('editorRef')         editorRef!:            ElementRef<HTMLDivElement>;
  @ViewChild('miniMapContainer')  miniMapContainer!:     ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  // Basic fields
  destinationName = '';
  streetAddress   = '';

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

  // Cover image
  coverImageUrl : string | null = null;
  coverImageName: string        = '';

  onCoverImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file  = input.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      this.coverImageUrl  = reader.result as string;
      this.coverImageName = file.name;
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
        this.galleryImages.push({ url: reader.result as string, name: file.name });
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
    { label: 'Nature & Outdoors', checked: false },
    { label: 'Culture & Heritage', checked: false },
    { label: 'Urban & City',       checked: false },
    { label: 'Adventure & Sport',  checked: false },
    { label: 'Family Friendly',    checked: false },
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

  // Geographic zone / Leaflet
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
      const layerCopy = this.zoneDrawService.cloneLayer(this.confirmedLayer);
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
        areaM2 = this.zoneDrawService.shoelaceAreaM2(this.drawnLayer.getLatLngs()[0]);
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

    const clone = this.zoneDrawService.cloneLayer(this.confirmedLayer);
    if (!clone) return;

    (clone as any)._isConfirmedZone = true;
    clone.addTo(this.miniMap);

    try {
      this.miniMap.fitBounds(clone.getBounds(), { padding: [10, 10] });
    } catch {}
  }

  //Lifecycle

  isEditMode = false;
  private editId: string | null = null;

  ngOnInit(): void {
    const state = history.state as { place?: any };
    if (state?.place) {
      this.isEditMode = true;
      this.editId = state.place.id ?? null;
      this._pendingDescription = state.place.description ?? state.place.summary ?? '';
      this._loadFromRouterState(state.place);
    }
  }
  private _pendingDescription = '';

  private _loadFromRouterState(p: any): void {
    this.destinationName = p.name ?? '';
    this.streetAddress   = p.address ?? '';

    // Cover image
    if (p.coverImageUrl) {
      this.coverImageUrl  = p.coverImageUrl;
      this.coverImageName = p.coverImageName ?? 'cover';
    }

    // Gallery
    if (Array.isArray(p.galleryImages)) {
      this.galleryImages = [...p.galleryImages];
    }

    // Classification
    if (Array.isArray(p.classification)) {
      this.classItems = this.classItems.map(c => ({
        ...c,
        checked: (p.classification as string[]).includes(c.label),
      }));
    }

    // Tags
    this.tags = Array.isArray(p.tags) ? [...p.tags] : [];

    // Zone
    if (p.zone) {
      setTimeout(() => {
        if (!this.miniMap) this.initMiniMap();
        const polygon = L.polygon(p.zone, {
          color: '#2563eb', fillColor: '#3b82f6', fillOpacity: 0.25, weight: 2,
        });
        (polygon as any)._isConfirmedZone = true;
        polygon.addTo(this.miniMap);
        this.zoneConfirmed  = true;
        this.confirmedLayer = polygon;
        try { this.miniMap.fitBounds(polygon.getBounds(), { padding: [10, 10] }); } catch {}
      }, 200);
    }
  }

  ngAfterViewInit(): void {
    setTimeout(() => {
      this.initMiniMap();
      if (this._pendingDescription && this.editorRef?.nativeElement) {
        this.editorRef.nativeElement.innerHTML = this._pendingDescription;
        this.syncEmptyState();
      }
    }, 0);
  }

  ngOnDestroy(): void {
    this.destroyModalMap();
    this.lightboxService.close();
    if (this.miniMap) { this.miniMap.remove(); this.miniMap = null; }
  }

  isSaving = false;

  publish(): void {
    if (!this.zoneConfirmed || !this.confirmedLayer) {
      alert('Please draw a zone on the map first.');
      return;
    }

    this.isSaving = true;
    const { zone, centerLat, centerLng } = this.zoneDrawService.extractZoneResult(this.confirmedLayer);
    const descriptionHtml  = this.editorRef?.nativeElement?.innerHTML ?? '';
    const descriptionText  = this.editorRef?.nativeElement?.innerText ?? '';
    const classification   = this.classItems.filter(c => c.checked).map(c => c.label);

    if (this.isEditMode && this.editId) {
      this.destinationsListSvc.updateItem(this.editId, {
        name:           this.destinationName || 'New Destination',
        lat:            centerLat,
        lng:            centerLng,
        zone,
        category:       'Destination',
        address:        this.streetAddress || undefined,
        status:         'Published',
        description:    descriptionHtml,
        summary:        descriptionText.slice(0, 160),
        classification: classification.length ? classification : undefined,
        tags:           this.tags.length ? this.tags : undefined,
        coverImageUrl:  this.coverImageUrl ?? undefined,
        coverImageName: this.coverImageName || undefined,
        galleryImages:  this.galleryImages.length ? [...this.galleryImages] : undefined,
        ...(this.coverImageUrl ? { imageBg: `url(${this.coverImageUrl}) center/cover` } : {}),
      });

      this.isSaving = false;
      const returnTo = history.state?.returnTo as string | undefined;
      this.router.navigate([returnTo ?? '/destinations']);
      return;
    }

    // ── ADD MODE ──
    const payload = {
      name:        this.destinationName || 'New Destination',
      type:        'Destination' as const,
      category:    'Destination',
      description: descriptionHtml,
      status:      'Active',
      eventsToday: 0,
      lat:         centerLat,
      lng:         centerLng,
      zone,
      address:     this.streetAddress || undefined,
    };

    this.destinationsService.add(payload).subscribe({
      next: (saved) => {
        this.isSaving = false;
        const returnTo = history.state?.returnTo as string | undefined;

        this.destinationsListSvc.addItem({
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
          zone:          saved.zone,
          address:       saved.address,
          description:   descriptionHtml,
          coverImageUrl: this.coverImageUrl ?? undefined,
          coverImageName: this.coverImageName || undefined,
          galleryImages: this.galleryImages.length ? [...this.galleryImages] : undefined,
          classification: classification.length ? classification : undefined,
          tags:          this.tags.length ? this.tags : undefined,
        });

        if (returnTo) {
          this.router.navigate([returnTo]);
        } else {
          this.router.navigate(['/map'], { state: { newPlace: saved } });
        }
      },
      error: () => { this.isSaving = false; alert('Failed to save.'); }
    });
  }
}