import { Component, ElementRef, ViewChild, NgZone, ChangeDetectorRef, AfterViewInit, OnDestroy, HostListener } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { MapPlacesService } from '../../../../core/services/map-places.service';
import { MapPlace } from '../../../../core/models/map-place.model';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { EventsListService, EventListItem } from '../../../../core/services/events-list.service';
declare const L: any;

interface ClassItem {
  label: string;
  checked: boolean;
}

const CX = 110, CY = 110, R = 80;

@Component({
  selector: 'app-new-event',
  standalone: true,
  imports: [CommonModule, DecimalPipe, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './add-event.component.html',
  styleUrl: './add-event.component.css',
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
export class NewEventComponent implements AfterViewInit, OnDestroy {

  constructor(
    private zone: NgZone,
    private cdr: ChangeDetectorRef,
    private router: Router,
    private mapPlacesService: MapPlacesService,
    private lightboxService: LightboxService,
    private eventsListSvc: EventsListService
  ) {}

  @ViewChild('editorRef')         editorRef!:            ElementRef<HTMLDivElement>;
  @ViewChild('miniMapContainer')  miniMapContainer!:     ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  // Basic fields
  eventTitle = '';
  venueType  = 'location';
  allDay     = false;
  freeEvent  = false;
  recurring  = false;
  startDate  = this.todayIso();
  endDate    = this.todayIso();
  startTime  = '09:00';
  endTime    = '17:00';

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
    this.editorIsEmpty = text.trim() === '' && el.innerHTML !== '<img' && !el.querySelector('img');
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
    this.coverImageUrl  = URL.createObjectURL(file);
    this.coverImageName = file.name;
    input.value = '';
  }

  removeCoverImage(): void {
    if (this.coverImageUrl) URL.revokeObjectURL(this.coverImageUrl);
    this.coverImageUrl  = null;
    this.coverImageName = '';
  }

  // Gallery
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

  openLightbox(images: { url: string; name: string }[], index: number): void {
    this.lightboxService.open(images, index);
  }

  // Date/time pickers
  activePicker: string | null = null;
  calendarYear  = new Date().getFullYear();
  calendarMonth = new Date().getMonth();

  readonly monthNames = ['January','February','March','April','May','June','July','August','September','October','November','December'];
  readonly weekDays = ['Su','Mo','Tu','We','Th','Fr','Sa'];

  get calendarDays(): number[] {
    const firstDay = new Date(this.calendarYear, this.calendarMonth, 1).getDay();
    const daysInMonth = new Date(this.calendarYear, this.calendarMonth + 1, 0).getDate();
    const cells: number[] = [];
    for (let i = 0; i < firstDay; i++) cells.push(0);
    for (let d = 1; d <= daysInMonth; d++) cells.push(d);
    return cells;
  }

  prevMonth(): void {
    if (this.calendarMonth === 0) { this.calendarMonth = 11; this.calendarYear--; }
    else { this.calendarMonth--; }
  }

  nextMonth(): void {
    if (this.calendarMonth === 11) { this.calendarMonth = 0; this.calendarYear++; }
    else { this.calendarMonth++; }
  }

  selectDate(day: number, field: 'startDate' | 'endDate'): void {
    if (!day) return;
    const m = String(this.calendarMonth + 1).padStart(2, '0');
    const d = String(day).padStart(2, '0');
    if (field === 'startDate') this.startDate = `${this.calendarYear}-${m}-${d}`;
    else                       this.endDate   = `${this.calendarYear}-${m}-${d}`;
    this.activePicker = null;
  }

  isSelectedDate(day: number, isoDate: string): boolean {
    if (!day || !isoDate) return false;
    const [y, mo, d] = isoDate.split('-').map(Number);
    return y === this.calendarYear && mo === this.calendarMonth + 1 && d === day;
  }

  isToday(day: number): boolean {
    if (!day) return false;
    const now = new Date();
    return now.getFullYear() === this.calendarYear && now.getMonth() === this.calendarMonth && now.getDate() === day;
  }

  formatDisplayDate(iso: string): string {
    if (!iso) return '';
    const [y, m, d] = iso.split('-').map(Number);
    return `${this.monthNames[m - 1].slice(0, 3)} ${d}, ${y}`;
  }

  private todayIso(): string {
    const now = new Date();
    return `${now.getFullYear()}-${String(now.getMonth()+1).padStart(2,'0')}-${String(now.getDate()).padStart(2,'0')}`;
  }

  get startDateDisplay(): string { return this.isoToDisplay(this.startDate); }
  get endDateDisplay():   string { return this.isoToDisplay(this.endDate); }

  private isoToDisplay(iso: string): string {
    if (!iso) return '';
    const [y, m, d] = iso.split('-');
    return `${d}.${m}.${y}`;
  }

  private displayToIso(display: string): string | null {
    const parts = display.replace(/[\/\-\s]/g, '.').split('.');
    if (parts.length !== 3) return null;
    const [d, m, y] = parts.map(p => p.padStart(2, '0'));
    if (!d || !m || !y || y.length < 4) return null;
    const day = parseInt(d), month = parseInt(m), year = parseInt(y);
    if (isNaN(day) || isNaN(month) || isNaN(year) || month < 1 || month > 12 || day < 1 || day > 31) return null;
    return `${y}-${m.padStart(2,'0')}-${d.padStart(2,'0')}`;
  }

  onDateTextInput(event: Event, _field: 'startDate' | 'endDate'): void {
    const input = event.target as HTMLInputElement;
    input.value = input.value.replace(/[^\d\.\/]/g, '');
  }

  onDateTextBlur(event: Event, field: 'startDate' | 'endDate'): void {
    const input = event.target as HTMLInputElement;
    const iso = this.displayToIso(input.value);
    if (iso) {
      if (field === 'startDate') this.startDate = iso;
      else                       this.endDate   = iso;
      input.value = this.isoToDisplay(iso);
    } else if (input.value.trim() !== '') {
      input.value = field === 'startDate' ? this.startDateDisplay : this.endDateDisplay;
    }
  }

  // Clock
  clockMode: 'hour' | 'minute' = 'hour';
  selectedHour   = 12;
  selectedMinute = 0;
  selectedAmPm: 'AM' | 'PM' = 'AM';

  readonly clockCX = CX;
  readonly clockCY = CY;
  readonly clockR  = R;

  get clockHourMarkers() {
    return Array.from({ length: 12 }, (_, i) => {
      const h = i + 1;
      const angle = (h / 12) * Math.PI * 2 - Math.PI / 2;
      return { value: h, x: CX + R * Math.cos(angle), y: CY + R * Math.sin(angle), selected: h === this.selectedHour };
    });
  }

  get clockMinuteMarkers() {
    return Array.from({ length: 12 }, (_, i) => {
      const m = i * 5;
      const angle = (m / 60) * Math.PI * 2 - Math.PI / 2;
      return { value: m, x: CX + R * Math.cos(angle), y: CY + R * Math.sin(angle), selected: m === this.selectedMinute };
    });
  }

  get clockHandEnd(): { x: number; y: number } {
    const angle = this.clockMode === 'hour'
      ? (this.selectedHour / 12) * Math.PI * 2 - Math.PI / 2
      : (this.selectedMinute / 60) * Math.PI * 2 - Math.PI / 2;
    return { x: CX + R * Math.cos(angle), y: CY + R * Math.sin(angle) };
  }

  setClockMode(m: 'hour' | 'minute'): void { this.clockMode = m; }
  setClockAmPm(ap: 'AM' | 'PM'): void { this.selectedAmPm = ap; }

  onClockInteraction(event: MouseEvent | TouchEvent, svgEl: Element): void {
    event.preventDefault();
    event.stopPropagation();
    const rect    = svgEl.getBoundingClientRect();
    const clientX = event instanceof TouchEvent ? event.touches[0].clientX : event.clientX;
    const clientY = event instanceof TouchEvent ? event.touches[0].clientY : event.clientY;
    const svgX    = (clientX - rect.left) / rect.width  * 220;
    const svgY    = (clientY - rect.top)  / rect.height * 220;
    const angle   = Math.atan2(svgY - CY, svgX - CX);
    if (this.clockMode === 'hour') {
      let h = Math.round((angle + Math.PI / 2) / (Math.PI * 2) * 12);
      if (h <= 0) h += 12;
      if (h > 12) h -= 12;
      this.selectedHour = h;
    } else {
      let m = Math.round((angle + Math.PI / 2) / (Math.PI * 2) * 12) * 5;
      if (m < 0)   m += 60;
      if (m >= 60) m -= 60;
      this.selectedMinute = m;
    }
    this.cdr.detectChanges();
  }

  private _clockDragging = false;
  private _clockSvgEl: Element | null = null;

  onClockMouseDown(event: MouseEvent, svgEl: Element): void {
    this._clockDragging = true;
    this._clockSvgEl = svgEl;
    this.onClockInteraction(event, svgEl);
  }

  isClockHourNear(h: number): boolean {
    const diff = Math.abs(h - this.selectedHour);
    return diff === 1 || diff === 11;
  }

  isClockMinuteNear(m: number): boolean {
    const diff = Math.abs(m - this.selectedMinute);
    return diff === 5 || diff === 55;
  }

  @HostListener('document:mousemove', ['$event'])
  onDocumentMouseMove(event: MouseEvent): void {
    if (this._clockDragging && this._clockSvgEl) this.onClockInteraction(event, this._clockSvgEl);
  }

  @HostListener('document:mouseup')
  onDocumentMouseUp(): void { this._clockDragging = false; this._clockSvgEl = null; }

  confirmTime(field: 'startTime' | 'endTime'): void {
    let h = this.selectedHour;
    if (this.selectedAmPm === 'AM' && h === 12) h = 0;
    if (this.selectedAmPm === 'PM' && h !== 12) h += 12;
    const timeStr = `${String(h).padStart(2,'0')}:${String(this.selectedMinute).padStart(2,'0')}`;
    if (field === 'startTime') this.startTime = timeStr;
    else                       this.endTime   = timeStr;
    this.activePicker = null;
  }

  formatDisplayTime(time24: string): string {
    if (!time24) return '';
    const [hh, mm] = time24.split(':').map(Number);
    const ampm = hh >= 12 ? 'PM' : 'AM';
    let h = hh % 12;
    if (h === 0) h = 12;
    return `${h}:${String(mm).padStart(2,'0')} ${ampm}`;
  }

  togglePicker(name: string, event: MouseEvent): void {
    event.stopPropagation();
    if (this.activePicker === name) { this.activePicker = null; return; }
    this.activePicker = name;
    if (name === 'startDate' || name === 'endDate') {
      const iso = name === 'startDate' ? this.startDate : this.endDate;
      if (iso) { const [y, m] = iso.split('-').map(Number); this.calendarYear = y; this.calendarMonth = m - 1; }
      else { const now = new Date(); this.calendarYear = now.getFullYear(); this.calendarMonth = now.getMonth(); }
    }
    if (name === 'startTime' || name === 'endTime') {
      this._loadClockState(name === 'startTime' ? this.startTime : this.endTime);
      this.clockMode = 'hour';
    }
  }

  private _loadClockState(time24: string): void {
    if (!time24) { this.selectedHour = 12; this.selectedMinute = 0; this.selectedAmPm = 'AM'; return; }
    let [hh, mm] = time24.split(':').map(Number);
    this.selectedAmPm   = hh >= 12 ? 'PM' : 'AM';
    hh = hh % 12;
    this.selectedHour   = hh === 0 ? 12 : hh;
    this.selectedMinute = Math.floor(mm / 5) * 5;
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this._clockDragging) return;
    if (this.activePicker) { this.activePicker = null; this.cdr.detectChanges(); }
    if (this.venueDropdownOpen) { this.venueDropdownOpen = false; this.cdr.detectChanges(); }
  }

  venueSearchQuery     = '';
  venueDropdownOpen    = false;
  isSearchingVenue     = false;
  filteredVenueResults: MapPlace[] = [];
  selectedVenue: MapPlace | null   = null;
  selectedVenueAreaKm2: number | null = null;

  pinLat: string = '';
  pinLng: string = '';
  streetAddress: string = '';
  pinConfirmed = false;
  mapModalOpen = false;

  private miniMap      : any = null;
  private modalMap     : any = null;
  private miniMarker   : any = null;
  private miniZoneLayer: any = null;
  private modalZoneLayer: any = null;
  private modalMarker  : any = null;
  private _venueSearchDebounce: any = null;

  navigateToAddVenue(): void {
    this.venueDropdownOpen = false;
    this.router.navigate([this.venueType === 'place' ? '/places/new' : '/locations/new']);
  }

  onVenueTypeChange(): void {
    this.venueSearchQuery = '';
    this.filteredVenueResults = [];
    this.venueDropdownOpen = false;
    this.selectedVenue = null;
    this.selectedVenueAreaKm2 = null;
    this.clearGeoFields();
    this.clearMiniMapLayers();
    if (this.miniMap) this.miniMap.setView([44.0, 20.9], 8);
  }

  onVenueSearch(): void {
    if (this._venueSearchDebounce) clearTimeout(this._venueSearchDebounce);
    const q = this.venueSearchQuery.trim().toLowerCase();
    this.venueDropdownOpen = true;
    const source = this.venueType === 'place'
      ? this.mapPlacesService.getPlaces()
      : this.mapPlacesService.getLocations();
    if (q.length < 1) {
      this.filteredVenueResults = source;
      return;
    }
    this.isSearchingVenue = true;
    this._venueSearchDebounce = setTimeout(() => {
      this.filteredVenueResults = source.filter(
        item => item.name.toLowerCase().includes(q) || (item.address ?? '').toLowerCase().includes(q)
      );
      this.isSearchingVenue = false;
      this.cdr.detectChanges();
    }, 200);
  }

  onVenueSearchFocus(): void {
    const q = this.venueSearchQuery.trim().toLowerCase();
    const source = this.venueType === 'place'
      ? this.mapPlacesService.getPlaces()
      : this.mapPlacesService.getLocations();
    this.filteredVenueResults = q.length === 0
      ? source
      : source.filter(item => item.name.toLowerCase().includes(q) || (item.address ?? '').toLowerCase().includes(q));
    this.venueDropdownOpen = true;
  }

  selectVenue(item: MapPlace): void {
    this.selectedVenue    = item;
    this.venueSearchQuery = item.name;
    this.venueDropdownOpen = false;
    this.pinConfirmed = true;

    if (this.venueType === 'place') {
      this.pinLat        = item.lat.toFixed(6);
      this.pinLng        = item.lng.toFixed(6);
      this.streetAddress = item.address ?? '';
      this.updateMiniMapPin(item.lat, item.lng);
    } else {
      this.pinLat = '';
      this.pinLng = '';
      this.streetAddress = '';
      if (item.zone) {
        this.updateMiniMapZone(item.zone);
        this.selectedVenueAreaKm2 = this.shoelaceAreaKm2(item.zone);
      }
    }
  }

  clearVenue(): void {
    this.selectedVenue    = null;
    this.venueSearchQuery = '';
    this.selectedVenueAreaKm2 = null;
    this.clearGeoFields();
    this.clearMiniMapLayers();
    if (this.miniMap) this.miniMap.setView([44.0, 20.9], 8);
    this.pinConfirmed = false;
  }

  private clearGeoFields(): void {
    this.pinLat = '';
    this.pinLng = '';
    this.streetAddress = '';
  }

  private clearMiniMapLayers(): void {
    if (this.miniMarker    && this.miniMap) { this.miniMap.removeLayer(this.miniMarker);    this.miniMarker    = null; }
    if (this.miniZoneLayer && this.miniMap) { this.miniMap.removeLayer(this.miniZoneLayer); this.miniZoneLayer = null; }
  }

  private makePinIcon(): any {
    return L.divIcon({
      html: `<svg width="24" height="32" viewBox="0 0 24 32" xmlns="http://www.w3.org/2000/svg">
               <path d="M12 0C5.373 0 0 5.373 0 12c0 8 12 20 12 20S24 20 24 12C24 5.373 18.627 0 12 0z" fill="#2563eb" stroke="white" stroke-width="1.5"/>
               <circle cx="12" cy="12" r="4" fill="white"/>
             </svg>`,
      iconSize: [24, 32], iconAnchor: [12, 32], className: '',
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
    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', { maxZoom: 19 }).addTo(this.miniMap);
  }

  private updateMiniMapPin(lat: number, lng: number): void {
    if (!this.miniMap) this.initMiniMap();
    if (!this.miniMap) return;
    this.clearMiniMapLayers();
    this.miniMarker = L.marker([lat, lng], { icon: this.makePinIcon(), draggable: false }).addTo(this.miniMap);
    this.miniMap.setView([lat, lng], 14);
  }

  private updateMiniMapZone(zone: [number, number][]): void {
    if (!this.miniMap) this.initMiniMap();
    if (!this.miniMap) return;
    this.clearMiniMapLayers();
    this.miniZoneLayer = L.polygon(zone, {
      color: '#2563eb', fillColor: '#3b82f6', fillOpacity: 0.2, weight: 2,
    }).addTo(this.miniMap);
    try { this.miniMap.fitBounds(this.miniZoneLayer.getBounds(), { padding: [10, 10] }); } catch {}
  }

  private initModalMap(el: HTMLDivElement): void {
    if (this.modalMap) return;
    let center: [number, number] = [44.0, 20.9];
    let zoom = 9;
    if (this.selectedVenue) {
      if (this.venueType === 'place') {
        const lat = parseFloat(this.pinLat), lng = parseFloat(this.pinLng);
        if (!isNaN(lat) && !isNaN(lng)) { center = [lat, lng]; zoom = 15; }
      } else if (this.selectedVenue.zone?.length) {
        const lats = this.selectedVenue.zone.map(p => p[0]);
        const lngs = this.selectedVenue.zone.map(p => p[1]);
        center = [(Math.min(...lats) + Math.max(...lats)) / 2, (Math.min(...lngs) + Math.max(...lngs)) / 2];
        zoom = 13;
      }
    }
    this.modalMap = L.map(el, { zoomControl: true, attributionControl: false }).setView(center, zoom);
    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', { maxZoom: 19 }).addTo(this.modalMap);
    setTimeout(() => this.modalMap?.invalidateSize(), 200);
    if (this.selectedVenue) {
      if (this.venueType === 'place') {
        const lat = parseFloat(this.pinLat), lng = parseFloat(this.pinLng);
        if (!isNaN(lat) && !isNaN(lng)) {
          this.modalMarker = L.marker([lat, lng], { icon: this.makePinIcon(), draggable: false }).addTo(this.modalMap);
        }
      } else if (this.selectedVenue.zone) {
        this.modalZoneLayer = L.polygon(this.selectedVenue.zone, {
          color: '#2563eb', fillColor: '#3b82f6', fillOpacity: 0.2, weight: 2,
        }).addTo(this.modalMap);
        try { this.modalMap.fitBounds(this.modalZoneLayer.getBounds(), { padding: [30, 30] }); } catch {}
      }
    }
  }

  private destroyModalMap(): void {
    if (this.modalMap) {
      this.modalMap.remove();
      this.modalMap = null;
      this.modalMarker = null;
      this.modalZoneLayer = null;
    }
  }

  openMapModal(): void {
    if (!this.selectedVenue) return;
    this.mapModalOpen = true;
    setTimeout(() => this.initModalMap(this.modalMapContainerRef.nativeElement), 50);
  }

  closeMapModal(): void {
    this.mapModalOpen = false;
    this.destroyModalMap();
  }

  private shoelaceAreaKm2(zone: [number, number][]): number {
    const R = 6_371_000;
    let area = 0;
    const n = zone.length;
    for (let i = 0; i < n; i++) {
      const j = (i + 1) % n;
      const lat1 = (zone[i][0] * Math.PI) / 180;
      const lat2 = (zone[j][0] * Math.PI) / 180;
      const lng1 = (zone[i][1] * Math.PI) / 180;
      const lng2 = (zone[j][1] * Math.PI) / 180;
      area += (lng2 - lng1) * (2 + Math.sin(lat1) + Math.sin(lat2));
    }
    return Math.abs((area * R * R) / 2) / 1_000_000;
  }

  // Classification & Tags
  classItems: ClassItem[] = [
    { label: 'Music',           checked: false },
    { label: 'Outdoor',         checked: false },
    { label: 'Culture',         checked: false },
    { label: 'Family Friendly', checked: false },
  ];

  tags    : string[] = [];
  tagInput: string   = '';

  addTag(event: Event): void {
    event.preventDefault();
    const value = this.tagInput.trim();
    if (!value || this.tags.includes(value)) { this.tagInput = ''; return; }
    this.tags = [...this.tags, value];
    this.tagInput = '';
  }

  removeTag(tag: string): void {
    this.tags = this.tags.filter(t => t !== tag);
  }

  // Lifecycle
  ngAfterViewInit(): void {
    setTimeout(() => {
      this.initMiniMap();
      this._loadFromRouterState();
    }, 0);
  }

  private _loadFromRouterState(): void {
    const state = history.state as { place?: any };
    if (!state?.place) return;

    const p = state.place;

    if (p.type === 'Place') {
      this.venueType = 'place';
      const found = this.mapPlacesService.findById(p.id) ?? p as MapPlace;
      this.selectVenue(found);
    } else {
      this.venueType = 'location';
      const found = this.mapPlacesService.findById(p.id)
        ?? this.mapPlacesService.getLocations().find(l =>
            l.name === p.name ||
            (Math.abs(l.lat - p.lat) < 0.001 && Math.abs(l.lng - p.lng) < 0.001)
          )
        ?? p as MapPlace;
      this.selectVenue(found);
    }

    this.cdr.detectChanges();
  }

  ngOnDestroy(): void {
    this.destroyModalMap();
    this.lightboxService.close()
    this.galleryImages.forEach(img => URL.revokeObjectURL(img.url));
    if (this.coverImageUrl) URL.revokeObjectURL(this.coverImageUrl);
    if (this.miniMap) { this.miniMap.remove(); this.miniMap = null; }
    if (this._venueSearchDebounce) clearTimeout(this._venueSearchDebounce);
  }
  isSaving = false;
  publish(): void {
    this.isSaving = true;
    const returnTo = history.state?.returnTo as string | undefined;
    const isEditMode = !!(history.state?.place?.id);

    setTimeout(() => {
      this.isSaving = false;

      const lat = this.pinLat ? parseFloat(this.pinLat) : this.selectedVenue?.lat;
      const lng = this.pinLng ? parseFloat(this.pinLng) : this.selectedVenue?.lng;

      const startDisp = this.startDate
        ? new Date(this.startDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
        : '';
      const endDisp = this.endDate
        ? new Date(this.endDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
        : '';
      const dateRange = startDisp === endDisp ? startDisp : `${startDisp}–${endDisp}`;

      if (isEditMode) {
        this.eventsListSvc.updateItem(history.state.place.id, {
          name:       this.eventTitle || 'New Event',
          lat,
          lng,
          address:    this.streetAddress || this.selectedVenue?.address,
          venueName:  this.selectedVenue?.name,
          startDate:  startDisp,
          endDate:    endDisp,
          dateRange,
          status:     'Published',
        });
      } else {
        this.eventsListSvc.addItem({
          id:          Date.now().toString(),
          name:        this.eventTitle || 'New Event',
          summary:     this.editorRef?.nativeElement?.innerText?.slice(0, 160) ?? '',
          region:      'Šumadija, Srbija',
          status:      'Published',
          lastUpdated: new Date().toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }),
          imageBg:     this.coverImageUrl
                        ? `url(${this.coverImageUrl}) center/cover`
                        : 'linear-gradient(135deg,#2563eb 0%,#1d4ed8 100%)',
          category:    'Event',
          lat,
          lng,
          address:     this.streetAddress || this.selectedVenue?.address,
          venueName:   this.selectedVenue?.name,
          startDate:   startDisp,
          endDate:     endDisp,
          dateRange,
          description: this.editorRef?.nativeElement?.innerHTML ?? '',
        });
      }

      if (returnTo) {
        this.router.navigate([returnTo]);
      } else {
        this.router.navigate(['/events']);
      }
    }, 800);
  }
}