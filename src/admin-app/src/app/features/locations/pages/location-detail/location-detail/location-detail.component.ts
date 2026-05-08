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
  ) {}

  @ViewChild('miniMapContainer')  miniMapContainer!:     ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  item:      LocationListItem | null = null;
  isLoading  = true;
  notFound   = false;

  region   = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  mapModalOpen  = false;
  deleteTarget: LocationListItem | null = null;
  isDeleting    = false;

  private miniMap:     any = null;
  private modalMap:    any = null;
  private miniMarker:  any = null;
  private modalMarker: any = null;

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    const navState = history.state as LocationListItem;

    if (navState?.id) {
      this.item      = { ...navState };
      this.isLoading = false;
      return;
    }

    if (id) {
      // BACKEND → replace with: this.svc.getById$(id).subscribe(...)
      this.notFound  = true;
      this.isLoading = false;
      return;
    }

    this.notFound  = true;
    this.isLoading = false;
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
    const hasCoords = (this.item as any)?.lat != null;

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
    const hasCoords = (this.item as any)?.lat != null;

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

  closeMapModal(): void {
    this.mapModalOpen = false;
    this.destroyModalMap();
  }

  editItem(): void {
    this.router.navigate(['/locations/new'], { state: { place: this.item } });
  }

  addEvent(): void {
    this.router.navigate(['/events/new'], { state: { linkedLocation: this.item } });
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

  statusClass(status: string): string {
    if (status === 'Published') return 'bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200';
    if (status === 'Draft')     return 'bg-amber-50  text-amber-700  ring-1 ring-amber-200';
    if (status === 'Archived')  return 'bg-gray-100  text-gray-500   ring-1 ring-gray-200';
    return '';
  }
}