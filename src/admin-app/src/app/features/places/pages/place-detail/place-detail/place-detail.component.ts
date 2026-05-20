import {
  Component, OnInit, AfterViewInit, OnDestroy,
  ElementRef, ViewChild, NgZone, ChangeDetectorRef,
} from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { SidebarComponent } from '../../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent }  from '../../../../../shared/components/topbar/topbar.component';
import { PlacesListService, PlaceListItem } from '../../../../../core/services/places-list.service';
import { LightboxService } from '../../../../../core/services/lightbox.service';
declare const L: any;

@Component({
  selector: 'app-place-detail',
  standalone: true,
  imports: [CommonModule, DecimalPipe, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './place-detail.component.html',
})
export class PlaceDetailComponent implements OnInit, AfterViewInit, OnDestroy {

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

  region   = 'Šumadija, Srbija';
  userName = '';
  userRole = '';

  mapModalOpen  = false;
  deleteTarget: PlaceListItem | null = null;
  isDeleting    = false;
  ownerFormOpen = false;
  ownerIdentifier = '';
  ownerError = '';
  ownerSuccess = '';
  isAddingOwner = false;

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
    this.region    = this.item.regionDisplayName || this.item.region;
    this.isLoading = false;
    this.notFound  = false;
    this.cdr.markForCheck();
    setTimeout(() => this.initMiniMap(), 100);
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

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', { maxZoom: 19 })
      .addTo(this.miniMap);

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

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', { maxZoom: 19 })
      .addTo(this.modalMap);

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

  toggleOwnerForm(): void {
    this.ownerFormOpen = !this.ownerFormOpen;
    this.ownerError = '';
    this.ownerSuccess = '';

    if (!this.ownerFormOpen) {
      this.ownerIdentifier = '';
    }
  }

  addOwner(): void {
    if (!this.item || this.isAddingOwner) return;

    const emailOrUsername = this.ownerIdentifier.trim();
    if (!emailOrUsername) {
      this.ownerError = 'Enter owner email or username.';
      this.ownerSuccess = '';
      return;
    }

    this.isAddingOwner = true;
    this.ownerError = '';
    this.ownerSuccess = '';

    this.svc.addOwner(this.item.regionId, this.item.id, { emailOrUsername }).subscribe({
      next: dto => {
        this.setItem(this.svc.toListItem(dto));
        this.ownerIdentifier = '';
        this.ownerFormOpen = false;
        this.ownerSuccess = 'Owner added.';
        this.isAddingOwner = false;
        this.cdr.markForCheck();
      },
      error: err => {
        this.ownerError = err?.error?.message || err?.message || 'Could not add owner.';
        this.ownerSuccess = '';
        this.isAddingOwner = false;
        this.cdr.markForCheck();
      },
    });
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
        place: {
          id:          it.id,
          name:        it.name,
          type:        'Place',
          category:    (it as any).category ?? 'Place',
          description: (it as any).description ?? '',
          status:      'Active',
          eventsToday: 0,
          lat:         (it as any).lat,
          lng:         (it as any).lng,
          address:     (it as any).address,
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
    this.cdr.markForCheck();
  }

  confirmDelete(): void {
    if (!this.deleteTarget) return;
    this.isDeleting = true;
    this.svc.delete$(this.deleteTarget.id)
      .subscribe(() => {
        this.isDeleting   = false;
        this.deleteTarget = null;
        this.router.navigate(['/places']);
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
