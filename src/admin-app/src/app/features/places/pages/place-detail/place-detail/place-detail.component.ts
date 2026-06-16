import {
  Component, OnInit, AfterViewInit, OnDestroy,
  ElementRef, ViewChild, NgZone, ChangeDetectorRef, inject,
} from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent }  from '../../../../../shared/components/topbar/topbar.component';
import { PlacesListService, PlaceListItem } from '../../../../../core/services/places-list.service';
import { LightboxService } from '../../../../../core/services/lightbox.service';
import { MapTileLayerService } from '../../../../../core/services/map-tile-layer.service';
import { DescriptionRichHtmlPipe } from '../../../../../shared/pipes/description-rich-html.pipe';
import {
  OPEN_24_HOURS_LABEL,
  isOpen24HoursSchedule,
  isOpen24HoursText,
} from '../../../../../shared/utils/opening-hours';
declare const L: any;

@Component({
  selector: 'app-place-detail',
  standalone: true,
  imports: [CommonModule, DecimalPipe, FormsModule, SidebarComponent, TopbarComponent, DescriptionRichHtmlPipe],
  templateUrl: './place-detail.component.html',
})
export class PlaceDetailComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly mapTileLayer = inject(MapTileLayerService);

  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private zone: NgZone,
    private cdr: ChangeDetectorRef,
    private svc: PlacesListService,
    private lightboxService: LightboxService,
  ) {}

  @ViewChild('miniMapContainer')  miniMapContainer!:     ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  item:      PlaceListItem | null = null;
  isLoading  = true;
  notFound   = false;
  descriptionExpanded = false;

  region   = 'Šumadija, Srbija';
  userName = '';
  userRole = '';

  mapModalOpen  = false;
  deleteTarget: PlaceListItem | null = null;
  isDeleting    = false;
  deleteError: string | null = null;

  private miniMap:     any = null;
  private modalMap:    any = null;
  private miniMarker:  any = null;
  private modalMarker: any = null;

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    const queryRegionId = Number(this.route.snapshot.queryParamMap.get('regionId'));
    const navState = history.state as Partial<PlaceListItem> & { regionId?: number };

    if (id) {
      const fromService = this.svc.findById(id);
      const regionId = navState?.regionId ?? fromService?.regionId ?? (Number.isFinite(queryRegionId) && queryRegionId > 0 ? queryRegionId : null);
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
      const regionId = navState?.regionId ?? (Number.isFinite(queryRegionId) && queryRegionId > 0 ? queryRegionId : null);
      if (regionId) {
        this.loadFromBackend(regionId, String(navState.id));
        return;
      }

      this.setItem(navState as PlaceListItem);
      return;
    }

    this.notFound  = true;
    this.isLoading = false;
  }

  private setItem(item: PlaceListItem): void {
    this.item      = { ...item };
    this.descriptionExpanded = false;
    this.region    = this.item.regionDisplayName || this.item.region;
    this.isLoading = false;
    this.notFound  = false;
    this.cdr.markForCheck();
    setTimeout(() => this.initMiniMap(), 100);
  }

  isDescriptionLong(value: string | null | undefined): boolean {
    return (value ?? '').replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim().length > 420;
  }

  private loadFromBackend(regionId: number, id: string): void {
    this.isLoading = true;
    this.svc.getPlace(regionId, id).subscribe({
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
    const lat = (this.item as any)?.lat ?? 44.0;
    const lng = (this.item as any)?.lng ?? 20.9;
    const hasCoords = (this.item as any)?.lat != null && (this.item as any)?.lng != null;

    this.miniMap = L.map(el, {
      zoomControl: true, dragging: true, scrollWheelZoom: false,
      doubleClickZoom: false, touchZoom: false, attributionControl: false,
    }).setView([lat, lng], hasCoords ? 14 : 8);

    this.mapTileLayer.addTo(L, this.miniMap);

    if (hasCoords) {
      this.miniMarker = L.marker([lat, lng], { icon: this.makePinIcon(), draggable: false })
        .addTo(this.miniMap);
    }
    setTimeout(() => this.miniMap?.invalidateSize(), 200);
  }

  private initModalMap(el: HTMLDivElement): void {
    if (this.modalMap) return;
    const lat = (this.item as any)?.lat ?? 44.0;
    const lng = (this.item as any)?.lng ?? 20.9;
    const hasCoords = (this.item as any)?.lat != null && (this.item as any)?.lng != null;

    this.modalMap = L.map(el, { zoomControl: true, attributionControl: false })
      .setView([lat, lng], hasCoords ? 15 : 9);

    this.mapTileLayer.addTo(L, this.modalMap);

    if (hasCoords) {
      this.modalMarker = L.marker([lat, lng], { icon: this.makePinIcon(), draggable: false })
        .addTo(this.modalMap);
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

  get parentLocationName(): string {
    return this.item?.locationName || (this.item as any)?.parentLocation || '';
  }

  get showsOpen24Hours(): boolean {
    const hours = this.item?.hours ?? [];
    return isOpen24HoursSchedule(hours) || isOpen24HoursText(this.item?.openingHours);
  }

  readonly open24HoursLabel = OPEN_24_HOURS_LABEL;

  goToParentLocation(event?: Event): void {
    event?.preventDefault();
    if (!this.item?.locationId || !this.item?.regionId) return;
    this.router.navigate(['/locations/detail', this.item.locationId], {
      state: { regionId: this.item.regionId },
    });
  }


  get isOwnable(): boolean {
    return Boolean((this.item as any)?.isOwnable);
  }

  get owners(): { userId: number; username: string; email: string; displayName: string }[] {
    return ((this.item as any)?.owners ?? []) as { userId: number; username: string; email: string; displayName: string }[];
  }

  get hasOwner(): boolean {
    return this.owners.length > 0;
  }

  get ownerNames(): string {
    return this.owners
      .map(owner => owner.displayName || owner.username || owner.email)
      .filter(Boolean)
      .join(', ');
  }

  closeMapModal(): void {
    this.mapModalOpen = false;
    this.destroyModalMap();
  }

  editItem(): void {
    const full = this.svc.findById(this.item!.id) ?? this.item!;
    this.router.navigate(['/places/edit', full.id], {
      queryParams: { regionId: full.regionId },
      state: { place: full, returnTo: `/places/detail/${full.id}`, regionId: full.regionId }
    });
  }

  addEvent(): void {
    const it = this.item!;
    this.router.navigate(['/events/new'], {
      state: {
        regionId: it.regionId,
        eventPrefill: {
          venueType: 'place',
          destinationId: it.destinationId ?? null,
          locationId: it.locationId ?? null,
          placeId: Number(it.id),
        },
        returnTo: `/places/detail/${it.id}`,
      }
    });
  }

  promptDelete(): void {
    this.deleteTarget = this.item;
    this.cdr.markForCheck();
  }

  cancelDelete(): void {
    this.deleteTarget = null;
    this.deleteError  = null;
    this.cdr.markForCheck();
  }

  confirmDelete(): void {
    if (!this.deleteTarget) return;
    this.isDeleting  = true;
    this.deleteError = null;
    this.svc.delete$(this.deleteTarget.id, this.deleteTarget.regionId)
      .subscribe({
        next: () => {
          this.isDeleting   = false;
          this.deleteTarget = null;
          this.router.navigate(['/places']);
        },
        error: (err: unknown) => {
          this.isDeleting = false;
          this.deleteError = err instanceof HttpErrorResponse
            ? (err.error?.message ?? 'Unable to delete this place.')
            : 'Unable to delete this place.';
          this.cdr.markForCheck();
        },
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
