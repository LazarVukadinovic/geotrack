import { Component, ElementRef, ViewChild, NgZone, ChangeDetectorRef, AfterViewInit, OnDestroy, HostListener } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { MapPlacesService } from '../../../../core/services/map-places.service';
import { MapPlace } from '../../../../core/models/map-place.model';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { EventsListService } from '../../../../core/services/events-list.service';
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
  eventTitle    = '';
  eventCategory = '';
  summary       = '';
  venueType     = 'location';
  allDay     = false;
  freeEvent  = false;
  recurring  = false;
  startDate  = this.todayIso();
  endDate    = this.todayIso();
  startTime  = '09:00';
  endTime    = '17:00';

  // Event details
  customVenueName  = '';
  organizerName    = '';
  contactEmail     = '';
  attendanceLimit  = '';
  ageRestriction   = '';

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

  // Venue search
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
    const returnState = { returnTo: '/events/new', venueType: this.venueType };
    if (this.venueType === 'place') this.router.navigate(['/places/new'], { state: returnState });
    else if (this.venueType === 'destination') this.router.navigate(['/destinations/new'], { state: returnState });
    else this.router.navigate(['/locations/new'], { state: returnState });
  }

  private getVenueSource(): MapPlace[] {
    if (this.venueType === 'place') return this.mapPlacesService.getPlaces();
    if (this.venueType === 'destination') return this.mapPlacesService.getDestinations();
    return this.mapPlacesService.getLocations();
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
    const source = this.getVenueSource();
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
    const source = this.getVenueSource();
    this.filteredVenueResults = q.length === 0
      ? source
      : source.filter(item => item.name.toLowerCase().includes(q) || (item.address ?? '').toLowerCase().includes(q));
    this.venueDropdownOpen = true;
  }

  selectVenue(item: MapPlace): void {
    this.selectedVenue     = item;
    this.venueSearchQuery  = item.name;
    this.venueDropdownOpen = false;
    this.pinConfirmed      = true;

    if (this.venueType === 'place') {
      this.pinLat        = item.lat.toFixed(6);
      this.pinLng        = item.lng.toFixed(6);
      this.streetAddress = item.address ?? '';
      this.updateMiniMapPin(item.lat, item.lng);
    } else {
      // location or destination — zone-based
      this.pinLat = '';
      this.pinLng = '';
      this.streetAddress = '';
      if (item.zone) {
        this.updateMiniMapZone(item.zone);
        this.selectedVenueAreaKm2 = this.shoelaceAreaKm2(item.zone);
      } else if (item.lat && item.lng) {
        this.updateMiniMapPin(item.lat, item.lng);
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
    const Rm = 6_371_000;
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
    return Math.abs((area * Rm * Rm) / 2) / 1_000_000;
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

  isEditMode = false;
  private _pendingDescription = '';

  ngAfterViewInit(): void {
    setTimeout(() => {
      this.initMiniMap();
      this._loadFromRouterState();

      if (this._pendingDescription && this.editorRef?.nativeElement) {
        this.editorRef.nativeElement.innerHTML = this._pendingDescription;
        this.syncEmptyState();
      }
    }, 0);
  }

  private _loadFromRouterState(): void {
    const state = history.state as any;
    if (!state) return;

    if (state.place?.id && (state.editMode || state.returnTo?.includes('events/detail'))) {
      this.isEditMode = true;
      const p = state.place;
      this.eventTitle          = p.name        ?? '';
      this.eventCategory       = p.category   ?? '';
      this.summary             = p.summary    ?? '';
      this._pendingDescription = p.description ?? '';
      this.startDate = this._parseDisplayDateToIso(p.startDate) ?? this.startDate;
      this.endDate   = this._parseDisplayDateToIso(p.endDate)   ?? this.endDate;
      if (p.startTime) this.startTime = p.startTime;
      if (p.endTime)   this.endTime   = p.endTime;
      this.allDay    = p.allDay    ?? false;
      this.freeEvent = p.freeEvent ?? false;
      this.recurring = p.recurring ?? false;

      if (p.coverImageUrl) {
        this.coverImageUrl  = p.coverImageUrl;
        this.coverImageName = p.coverImageName ?? 'cover';
      }

      if (Array.isArray(p.galleryImages)) {
        this.galleryImages = [...p.galleryImages];
      }

      // Classification / Tags
      if (Array.isArray(p.classification)) {
        this.classItems = this.classItems.map(c => ({
          ...c, checked: (p.classification as string[]).includes(c.label),
        }));
      }
      this.tags = Array.isArray(p.tags) ? [...p.tags] : [];

      // restore event-details fields
      this.customVenueName  = p.venueName    ?? '';
      this.organizerName    = p.organizerName   ?? '';
      this.contactEmail     = p.contactEmail    ?? '';
      this.attendanceLimit  = p.attendanceLimit ?? '';
      this.ageRestriction   = p.ageRestriction  ?? '';

      if (p.venueName || p.lat) {
        let found: MapPlace | undefined;

        if (p.lat != null && p.lng != null) {
          found = this.mapPlacesService.getAll().find(v =>
            Math.abs(v.lat - p.lat) < 0.001 && Math.abs(v.lng - p.lng) < 0.001
          );
        }

        if (!found && p.venueName) {
          found = this.mapPlacesService.getAll().find(v => v.name === p.venueName);
        }

        if (!found && p.lat != null && p.lng != null) {
          const temp: MapPlace = {
            id:          p.id ?? Date.now().toString(),
            name:        p.venueName ?? 'Venue',
            type:        'Place',
            category:    p.category ?? 'Place',
            description: '',
            status:      'Active',
            eventsToday: 0,
            lat:         p.lat,
            lng:         p.lng,
            address:     p.address,
            zone:        p.zone,
          };
          this.mapPlacesService.addPlace(temp);
          found = temp;
        }

        if (found) {
          if (p.venueType) {
            this.venueType = p.venueType;
          } else {
            this.venueType = found.type === 'Place' ? 'place' : found.type === 'Destination' ? 'destination' : 'location';
          }
          this.selectVenue(found);
        }
      }

      this.cdr.detectChanges();
      return;
    }

    if (state.place) {
      const p = state.place;
      const type = (p.type ?? '').toLowerCase();

      if (type === 'place') {
        this.venueType = 'place';
        const found = this.mapPlacesService.findById(p.id) ?? (p as MapPlace);
        this.selectVenue(found);
      } else if (type === 'destination') {
        this.venueType = 'destination';

        let found: MapPlace | undefined = this.mapPlacesService.findById(p.id) ??
          this.mapPlacesService.getDestinations().find(d => d.name === p.name);

        if (!found && p.lat != null) {
          const tempPlace: MapPlace = {
            id:          p.id   ?? Date.now().toString(),
            name:        p.name ?? 'Unknown Destination',
            type:        'Destination',
            category:    p.category ?? 'Destination',
            description: p.description ?? '',
            status:      'Active',
            eventsToday: 0,
            lat:         p.lat,
            lng:         p.lng,
            zone:        p.zone,
            address:     p.address,
          };
          this.mapPlacesService.addPlace(tempPlace);
          found = tempPlace;
        }

        if (found) this.selectVenue(found);
      } else {
        // Location
        this.venueType = 'location';

        let found: MapPlace | undefined =
          this.mapPlacesService.findById(p.id) ??
          this.mapPlacesService.getLocations().find(l =>
            l.name === p.name ||
            (p.lat != null && Math.abs(l.lat - p.lat) < 0.001 && Math.abs(l.lng - p.lng) < 0.001)
          );

        if (!found && p.lat != null) {
          const tempPlace: MapPlace = {
            id:          p.id   ?? Date.now().toString(),
            name:        p.name ?? 'Unknown Location',
            type:        'Location',
            category:    p.category ?? 'Location',
            description: p.description ?? '',
            status:      'Active',
            eventsToday: 0,
            lat:         p.lat,
            lng:         p.lng,
            zone:        p.zone,
            address:     p.address,
          };
          this.mapPlacesService.addPlace(tempPlace);
          found = tempPlace;
        }

        if (found) this.selectVenue(found);
      }

      this.cdr.detectChanges();
    }
  }

  private _parseDisplayDateToIso(display: string | undefined): string | null {
    if (!display) return null;
    try {
      const d = new Date(display);
      if (isNaN(d.getTime())) return null;
      return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
    } catch { return null; }
  }

  ngOnDestroy(): void {
    this.destroyModalMap();
    this.lightboxService.close();
    if (this.miniMap) { this.miniMap.remove(); this.miniMap = null; }
    if (this._venueSearchDebounce) clearTimeout(this._venueSearchDebounce);
  }
  isSaving = false;

  publish(): void {
    if (!this.selectedVenue) {
      alert('Please select a location or place for this event.');
      return;
    }
    if (!this.eventTitle.trim()) {
      alert('Please enter an event title.');
      return;
    }

    this.isSaving = true;
    const returnTo = history.state?.returnTo as string | undefined;

    const lat = this.pinLat ? parseFloat(this.pinLat) : this.selectedVenue?.lat;
    const lng = this.pinLng ? parseFloat(this.pinLng) : this.selectedVenue?.lng;

    const startDisp = this.startDate
      ? new Date(this.startDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
      : '';
    const endDisp = this.endDate
      ? new Date(this.endDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
      : '';
    const dateRange = startDisp === endDisp ? startDisp : `${startDisp}–${endDisp}`;

    const descriptionHtml = this.editorRef?.nativeElement?.innerHTML ?? '';
    const descriptionText = this.editorRef?.nativeElement?.innerText  ?? '';
    const classification  = this.classItems.filter(c => c.checked).map(c => c.label);

    setTimeout(() => {
      this.isSaving = false;

      const zone = this.selectedVenue?.zone ?? undefined;
      const sharedFields = {
        name:            this.eventTitle,
        category:        this.eventCategory || 'Event',
        summary:         this.summary || descriptionText.slice(0, 160),
        lat,
        lng,
        zone,
        venueType:       this.venueType,
        address:         this.streetAddress || this.selectedVenue?.address,
        venueName:       this.customVenueName || this.selectedVenue?.name,
        startDate:       startDisp,
        endDate:         endDisp,
        startTime:       this.startTime,
        endTime:         this.endTime,
        allDay:          this.allDay,
        freeEvent:       this.freeEvent,
        recurring:       this.recurring,
        dateRange,
        description:     descriptionHtml,
        classification:  classification.length ? classification : undefined,
        tags:            this.tags.length ? this.tags : undefined,
        coverImageUrl:   this.coverImageUrl ?? undefined,
        coverImageName:  this.coverImageName || undefined,
        galleryImages:   this.galleryImages.length ? [...this.galleryImages] : undefined,
        organizerName:   this.organizerName || undefined,
        contactEmail:    this.contactEmail || undefined,
        attendanceLimit: this.attendanceLimit || undefined,
        ageRestriction:  this.ageRestriction || undefined,
      };

      if (this.isEditMode && history.state?.place?.id) {
        this.eventsListSvc.updateItem(history.state.place.id, {
          ...sharedFields,
          status: 'Published',
          ...(this.coverImageUrl ? { imageBg: `url(${this.coverImageUrl}) center/cover` } : {}),
        });
      } else {
        this.eventsListSvc.addItem({
          ...sharedFields,
          id:          Date.now().toString(),
          region:      'Šumadija, Srbija',
          status:      'Published',
          lastUpdated: new Date().toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }),
          imageBg:     this.coverImageUrl
                         ? `url(${this.coverImageUrl}) center/cover`
                         : 'linear-gradient(135deg,#2563eb 0%,#1d4ed8 100%)',
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