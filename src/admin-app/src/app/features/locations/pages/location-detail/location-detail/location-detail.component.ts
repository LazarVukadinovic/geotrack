import {
  Component, OnInit, AfterViewInit, OnDestroy,
  ElementRef, ViewChild, NgZone, ChangeDetectorRef,
} from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent }  from '../../../../../shared/components/topbar/topbar.component';
import { LocationsListService, LocationListItem } from '../../../../../core/services/locations-list.service';
import { LightboxService } from '../../../../../core/services/lightbox.service';
declare const L: any;

@Component({
  selector: 'app-location-detail',
  standalone: true,
  imports: [CommonModule, DecimalPipe, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './location-detail.component.html',
})
export class LocationDetailComponent implements OnInit, AfterViewInit, OnDestroy {

  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private zone: NgZone,
    private cdr: ChangeDetectorRef,
    private svc: LocationsListService,
    private lightboxService: LightboxService,
  ) {}

  @ViewChild('miniMapContainer')  miniMapContainer!:     ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  item:      LocationListItem | null = null;
  isLoading  = true;
  notFound   = false;

  region   = 'Šumadija, Srbija';
  userName = '';
  userRole = '';

  mapModalOpen  = false;
  deleteTarget: LocationListItem | null = null;
  isDeleting    = false;

  private miniMap:     any = null;
  private modalMap:    any = null;
  private miniMarker:  any = null;
  private modalMarker: any = null;

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    const queryRegionId = Number(this.route.snapshot.queryParamMap.get('regionId'));
    const navState = history.state as Partial<LocationListItem> & { regionId?: number };

    if (id) {
      const fromService = this.svc.findById(id);
      const regionId = navState?.regionId
        ?? fromService?.regionId
        ?? (Number.isFinite(queryRegionId) && queryRegionId > 0 ? queryRegionId : null);

      if (regionId) {
        this.loadFromBackend(regionId, id);
        return;
      }

      if (fromService) {
        this.setItem(fromService);
        return;
      }
    }

    if (navState?.id && navState?.name && navState?.status) {
      const regionId = navState?.regionId
        ?? (Number.isFinite(queryRegionId) && queryRegionId > 0 ? queryRegionId : null);

      if (regionId) {
        this.loadFromBackend(regionId, String(navState.id));
        return;
      }

      this.setItem(navState as LocationListItem);
      return;
    }

    this.notFound  = true;
    this.isLoading = false;
  }

  private setItem(item: LocationListItem): void {
    this.item      = { ...item };
    this.region    = this.item.regionDisplayName || this.item.region;
    this.isLoading = false;
    this.notFound  = false;
    this.cdr.markForCheck();
    setTimeout(() => this.initMiniMap(), 100);
  }

  private loadFromBackend(regionId: number, id: string): void {
    this.isLoading = true;
    this.svc.getLocation(regionId, id).subscribe({
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
    if (this.miniMap) { this.miniMap.remove(); this.miniMap = null; }
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
    const zone = (this.item as any)?.zone as [number,number][] | undefined;
    const lat = (this.item as any)?.lat ?? 44.0;
    const lng = (this.item as any)?.lng ?? 20.9;

    this.miniMap = L.map(el, {
      zoomControl: true, dragging: true, scrollWheelZoom: false,
      doubleClickZoom: false, touchZoom: false, attributionControl: false,
    }).setView([lat, lng], zone ? 12 : 8);

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
    }).addTo(this.miniMap);

    if (zone && zone.length > 0) {
      this.miniMarker = L.polygon(zone, {
        color: '#2563eb', fillColor: '#3b82f6', fillOpacity: 0.2, weight: 2,
      }).addTo(this.miniMap);
      try { this.miniMap.fitBounds(this.miniMarker.getBounds(), { padding: [10, 10] }); } catch {}
    }
    setTimeout(() => this.miniMap?.invalidateSize(), 200);
  }

  private initModalMap(el: HTMLDivElement): void {
    if (this.modalMap) return;
    const zone = (this.item as any)?.zone as [number,number][] | undefined;
    const lat = (this.item as any)?.lat ?? 44.0;
    const lng = (this.item as any)?.lng ?? 20.9;

    this.modalMap = L.map(el, { zoomControl: true, attributionControl: false })
      .setView([lat, lng], zone ? 13 : 9);

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
    }).addTo(this.modalMap);

    if (zone && zone.length > 0) {
      this.modalMarker = L.polygon(zone, {
        color: '#2563eb', fillColor: '#3b82f6', fillOpacity: 0.2, weight: 2,
      }).addTo(this.modalMap);
      try { this.modalMap.fitBounds(this.modalMarker.getBounds(), { padding: [30, 30] }); } catch {}
    }
    setTimeout(() => this.modalMap?.invalidateSize(), 200);
  }

  private destroyModalMap(): void {
    if (this.modalMap) { this.modalMap.remove(); this.modalMap = null; this.modalMarker = null; }
  }

  openMapModal(): void {
    this.mapModalOpen = true;
    setTimeout(() => this.initModalMap(this.modalMapContainerRef.nativeElement), 50);
  }

  get parentDestinationName(): string {
    return this.item?.destinationName || (this.item as any)?.parentDestination || '';
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
    this.router.navigate(['/locations/edit', full.id], {
      queryParams: { regionId: full.regionId },
      state: { location: full, returnTo: `/locations/detail/${full.id}`, regionId: full.regionId }
    });
  }

  addEvent(): void {
    const it = this.item!;
    this.router.navigate(['/events/new'], {
      state: {
        place: {
          id:          it.id,
          name:        it.name,
          type:        'Location',
          category:    (it as any).category ?? 'Location',
          description: (it as any).description ?? '',
          status:      'Active',
          eventsToday: 0,
          lat:         (it as any).lat,
          lng:         (it as any).lng,
          zone:        (it as any).zone,
          address:     (it as any).address,
        },
        returnTo: `/locations/detail/${it.id}`,
      }
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
        this.router.navigate(['/locations']);
      });
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
