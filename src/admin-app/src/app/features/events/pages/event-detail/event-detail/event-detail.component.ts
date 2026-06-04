import {
  Component, OnInit, AfterViewInit, OnDestroy,
  ElementRef, ViewChild, NgZone, ChangeDetectorRef, inject,
} from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent }  from '../../../../../shared/components/topbar/topbar.component';
import { EventsListService, EventListItem } from '../../../../../core/services/events-list.service';
import { LightboxService } from '../../../../../core/services/lightbox.service';
import { MapTileLayerService } from '../../../../../core/services/map-tile-layer.service';
declare const L: any;

@Component({
  selector: 'app-event-detail',
  standalone: true,
  imports: [CommonModule, DecimalPipe, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './event-detail.component.html',
})
export class EventDetailComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly mapTileLayer = inject(MapTileLayerService);

  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private zone: NgZone,
    private cdr: ChangeDetectorRef,
    private svc: EventsListService,
    private lightboxService: LightboxService,
  ) {}

  @ViewChild('miniMapContainer')  miniMapContainer!:     ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  item:      EventListItem | null = null;
  isLoading  = true;
  notFound   = false;

  region   = '';
  userName = '';
  userRole = '';

  mapModalOpen  = false;
  deleteTarget: EventListItem | null = null;
  isDeleting    = false;

  private miniMap:        any = null;
  private modalMap:       any = null;
  private miniMarker:     any = null;
  private modalMarker:    any = null;
  private miniZoneLayer:  any = null;
  private modalZoneLayer: any = null;

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    const navState = history.state as Partial<EventListItem> & { regionId?: number };

    if (id) {
      const fromService = this.svc.findById(id);
      if (fromService) {
        this.setItem(fromService);
        return;
      }

      const regionId = navState?.regionId ?? null;
      if (regionId) {
        this.loadFromBackend(regionId, id);
        return;
      }
    }

    if (navState?.id && navState?.name && navState?.status) {
      this.setItem(navState as EventListItem);
      return;
    }

    this.notFound  = true;
    this.isLoading = false;
  }

  private setItem(item: EventListItem): void {
    this.item      = { ...item };
    this.region    = this.item.regionDisplayName || this.item.region;
    this.isLoading = false;
    this.notFound  = false;
    this.cdr.markForCheck();
    setTimeout(() => this.initMiniMap(), 100);
  }

  private loadFromBackend(regionId: number, id: string): void {
    this.isLoading = true;
    this.svc.getEvent(regionId, id).subscribe({
      next: dto => this.setItem(this.svc.toListItem(dto)),
      error: () => {
        this.notFound = true;
        this.isLoading = false;
        this.cdr.markForCheck();
      },
    });
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.initMiniMap(), 100);
  }

  ngOnDestroy(): void {
    this.destroyModalMap();
    if (this.miniMap) {
      if (this.miniZoneLayer) { this.miniMap.removeLayer(this.miniZoneLayer); this.miniZoneLayer = null; }
      this.miniMap.remove();
      this.miniMap = null;
    }
  }

  private makePinIcon(): any {
    return L.divIcon({
      html: `<svg width="24" height="32" viewBox="0 0 24 32" xmlns="http://www.w3.org/2000/svg">
               <path d="M12 0C5.373 0 0 5.373 0 12c0 8 12 20 12 20S24 20 24 12C24 5.373 18.627 0 12 0z"
                     fill="#2563eb" stroke="white" stroke-width="1.5"/>
               <circle cx="12" cy="12" r="4" fill="white"/>
             </svg>`,
      iconSize: [24, 32], iconAnchor: [12, 32], className: '',
    });
  }

  private initMiniMap(): void {
    if (this.miniMap || !this.miniMapContainer?.nativeElement) return;
    const el = this.miniMapContainer.nativeElement;
    const zone: [number, number][] | undefined = (this.item as any)?.zone;
    const lat = (this.item as any)?.lat ?? 44.0;
    const lng = (this.item as any)?.lng ?? 20.9;
    const hasCoords = (this.item as any)?.lat != null;

    this.miniMap = L.map(el, {
      zoomControl: true, dragging: true, scrollWheelZoom: false,
      doubleClickZoom: false, touchZoom: false, attributionControl: false,
    }).setView([lat, lng], hasCoords ? 14 : 8);

    this.mapTileLayer.addTo(L, this.miniMap);

    if (zone?.length) {
      this.miniZoneLayer = L.polygon(zone, {
        color: '#2563eb', fillColor: '#3b82f6', fillOpacity: 0.2, weight: 2,
      }).addTo(this.miniMap);
      try { this.miniMap.fitBounds(this.miniZoneLayer.getBounds(), { padding: [10, 10] }); } catch {}
    } else if (hasCoords) {
      this.miniMarker = L.marker([lat, lng], { icon: this.makePinIcon(), draggable: false })
        .addTo(this.miniMap);
    }
    setTimeout(() => this.miniMap?.invalidateSize(), 200);
  }

  private initModalMap(el: HTMLDivElement): void {
    if (this.modalMap) return;
    const zone: [number, number][] | undefined = (this.item as any)?.zone;
    const lat = (this.item as any)?.lat ?? 44.0;
    const lng = (this.item as any)?.lng ?? 20.9;
    const hasCoords = (this.item as any)?.lat != null;

    this.modalMap = L.map(el, { zoomControl: true, attributionControl: false })
      .setView([lat, lng], hasCoords ? 15 : 9);

    this.mapTileLayer.addTo(L, this.modalMap);

    if (zone?.length) {
      this.modalZoneLayer = L.polygon(zone, {
        color: '#2563eb', fillColor: '#3b82f6', fillOpacity: 0.2, weight: 2,
      }).addTo(this.modalMap);
      try { this.modalMap.fitBounds(this.modalZoneLayer.getBounds(), { padding: [30, 30] }); } catch {}
    } else if (hasCoords) {
      this.modalMarker = L.marker([lat, lng], { icon: this.makePinIcon(), draggable: false })
        .addTo(this.modalMap);
    }
    setTimeout(() => this.modalMap?.invalidateSize(), 200);
  }

  private destroyModalMap(): void {
    if (this.modalMap) {
      this.modalMap.remove();
      this.modalMap = null;
      this.modalMarker = null;
      this.modalZoneLayer = null;
    }
  }

  get itemZone(): [number, number][] | undefined {
    return (this.item as any)?.zone;
  }

  get itemVenueType(): string {
    return (this.item as any)?.venueType ?? 'location';
  }

  openMapModal(): void {
    this.mapModalOpen = true;
    setTimeout(() => this.initModalMap(this.modalMapContainerRef.nativeElement), 50);
  }

  get parentLocationName(): string {
    return this.item?.locationName || '';
  }

  get parentPlaceName(): string {
    return this.item?.placeName || '';
  }

  get parentDestinationName(): string {
    return this.item?.destinationName || '';
  }

  goToParentLocation(event?: Event): void {
    event?.preventDefault();
    if (!this.item?.locationId || !this.item?.regionId) return;
    this.router.navigate(['/locations/detail', this.item.locationId], {
      state: { regionId: this.item.regionId },
    });
  }

  goToParentPlace(event?: Event): void {
    event?.preventDefault();
    if (!this.item?.placeId || !this.item?.regionId) return;
    this.router.navigate(['/places/detail', this.item.placeId], {
      state: { regionId: this.item.regionId },
    });
  }

  goToParentDestination(event?: Event): void {
    event?.preventDefault();
    if (!this.item?.destinationId || !this.item?.regionId) return;
    this.router.navigate(['/destinations/detail', this.item.destinationId], {
      state: { regionId: this.item.regionId },
    });
  }

  closeMapModal(): void {
    this.mapModalOpen = false;
    this.destroyModalMap();
  }

  editItem(): void {
    const full = this.svc.findById(this.item!.id) ?? this.item!;
    this.router.navigate(['/events/new'], {
      state: { place: full, returnTo: `/events/detail/${full.id}` }
    });
  }

  promptDelete(): void {
    this.deleteTarget = this.item;
    this.cdr.markForCheck();
  }

  cancelDelete(): void {
    this.deleteTarget = null;
    this.cdr.markForCheck();
  }

  confirmDelete(): void {
    if (!this.deleteTarget) return;
    this.isDeleting = true;
    this.svc.delete$(this.deleteTarget.id)
      .subscribe(() => {
        this.isDeleting   = false;
        this.deleteTarget = null;
        this.router.navigate(['/events']);
      });
  }

  readonly galleryLimit = 6;

  galleryPreview(images: { url: string; name: string }[]): { url: string; name: string }[] {
    return images.slice(0, this.galleryLimit);
  }

  galleryOverflow(images: { url: string; name: string }[]): number {
    return Math.max(0, images.length - this.galleryLimit);
  }

  openLightbox(images: { url: string; name: string }[], index: number): void {
    this.lightboxService.open(images, index);
  }

  statusClass(status: string): string {
    if (status === 'Published') return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
    if (status === 'Draft')     return 'bg-amber-50  text-amber-700  ring-1 ring-amber-200';
    if (status === 'Archived')  return 'bg-gray-100  text-gray-500   ring-1 ring-gray-200';
    return '';
  }
}
