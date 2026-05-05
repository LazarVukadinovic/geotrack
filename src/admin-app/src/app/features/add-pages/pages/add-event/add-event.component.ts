import { Component, ElementRef, ViewChild, NgZone, ChangeDetectorRef, AfterViewInit, OnDestroy, HostListener } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';

declare const L: any;

interface ClassItem {
  label: string;
  checked: boolean;
}

const CX = 110, CY = 110, R = 80;

// Mock data types
interface VenueItem {
  id: string;
  name: string;
  address: string;
  lat: number;
  lng: number;
  // For locations: zone polygon latlngs; for places: just a pin
  zone?: [number, number][];
  radius?: number;
}

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
  ) {}

  @ViewChild('editorRef')         editorRef!:            ElementRef<HTMLDivElement>;
  @ViewChild('miniMapContainer')  miniMapContainer!:     ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  // Basic fields
  eventTitle = 'Summer Festival';
  venueType  = 'location';
  allDay     = false;
  freeEvent  = false;
  recurring  = false;
  startDate  = this.todayIso();
  endDate    = this.todayIso();
  startTime  = '21:00';
  endTime    = '05:00';

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

  // Lightbox
  private lightboxEl: HTMLElement | null = null;
  private _lightboxKeyHandler: ((e: KeyboardEvent) => void) | null = null;
  private _currentImages: { url: string; name: string }[] = [];
  private _currentIndex = 0;

  openLightbox(images: { url: string; name: string }[], index: number): void {
    this.zone.run(() => {
      this.closeLightbox();
      this._currentImages = [...images];
      this._currentIndex  = index;
      this._renderLightbox();
      this._lightboxKeyHandler = (e: KeyboardEvent) => {
        if (e.key === 'Escape')     { this.closeLightbox(); }
        if (e.key === 'ArrowRight') { this._currentIndex = (this._currentIndex + 1) % this._currentImages.length; this._renderLightbox(); }
        if (e.key === 'ArrowLeft')  { this._currentIndex = (this._currentIndex - 1 + this._currentImages.length) % this._currentImages.length; this._renderLightbox(); }
      };
      document.addEventListener('keydown', this._lightboxKeyHandler);
      document.body.style.overflow = 'hidden';
    });
  }

  private _renderLightbox(): void {
    if (this.lightboxEl) document.body.removeChild(this.lightboxEl);
    const imgs = this._currentImages, idx = this._currentIndex, single = imgs.length === 1;
    const overlay = document.createElement('div');
    overlay.style.cssText = 'position:fixed;inset:0;z-index:99999;background:rgba(0,0,0,0.92);display:flex;align-items:center;justify-content:center;';
    overlay.addEventListener('click', () => this.closeLightbox());
    const closeBtn = document.createElement('button');
    closeBtn.innerHTML = '&times;';
    closeBtn.style.cssText = 'position:absolute;top:16px;right:16px;width:40px;height:40px;border-radius:50%;background:rgba(255,255,255,0.15);border:none;color:white;font-size:24px;cursor:pointer;display:flex;align-items:center;justify-content:center;z-index:1;';
    closeBtn.addEventListener('click', (e) => { e.stopPropagation(); this.closeLightbox(); });
    overlay.appendChild(closeBtn);
    if (!single) {
      const counter = document.createElement('div');
      counter.textContent = `${idx + 1} / ${imgs.length}`;
      counter.style.cssText = 'position:absolute;top:16px;left:50%;transform:translateX(-50%);color:white;font-size:14px;font-weight:500;background:rgba(0,0,0,0.4);padding:4px 12px;border-radius:20px;z-index:1;';
      overlay.appendChild(counter);
      const prev = document.createElement('button');
      prev.innerHTML = '&#8249;';
      prev.style.cssText = 'position:absolute;left:16px;top:50%;transform:translateY(-50%);width:44px;height:44px;border-radius:50%;background:rgba(255,255,255,0.15);border:none;color:white;font-size:28px;cursor:pointer;display:flex;align-items:center;justify-content:center;z-index:1;';
      prev.addEventListener('click', (e) => { e.stopPropagation(); this._currentIndex = (idx - 1 + imgs.length) % imgs.length; this._renderLightbox(); });
      overlay.appendChild(prev);
    }
    const img = document.createElement('img');
    img.src = imgs[idx].url; img.alt = imgs[idx].name;
    img.style.cssText = 'max-width:88vw;max-height:80vh;object-fit:contain;border-radius:12px;box-shadow:0 25px 60px rgba(0,0,0,0.5);';
    img.addEventListener('click', (e) => e.stopPropagation());
    overlay.appendChild(img);
    if (!single) {
      const next = document.createElement('button');
      next.innerHTML = '&#8250;';
      next.style.cssText = 'position:absolute;right:16px;top:50%;transform:translateY(-50%);width:44px;height:44px;border-radius:50%;background:rgba(255,255,255,0.15);border:none;color:white;font-size:28px;cursor:pointer;display:flex;align-items:center;justify-content:center;z-index:1;';
      next.addEventListener('click', (e) => { e.stopPropagation(); this._currentIndex = (idx + 1) % imgs.length; this._renderLightbox(); });
      overlay.appendChild(next);
      const thumbRow = document.createElement('div');
      thumbRow.style.cssText = 'position:absolute;bottom:16px;left:50%;transform:translateX(-50%);display:flex;gap:8px;z-index:1;';
      thumbRow.addEventListener('click', (e) => e.stopPropagation());
      imgs.forEach((im, ti) => {
        const thumb = document.createElement('div');
        thumb.style.cssText = `width:48px;height:48px;border-radius:8px;overflow:hidden;cursor:pointer;border:2px solid ${ti === idx ? 'white' : 'transparent'};opacity:${ti === idx ? '1' : '0.5'};transition:all 0.15s;`;
        const tImg = document.createElement('img');
        tImg.src = im.url; tImg.style.cssText = 'width:100%;height:100%;object-fit:cover;';
        tImg.addEventListener('click', (e) => { e.stopPropagation(); this._currentIndex = ti; this._renderLightbox(); });
        thumb.appendChild(tImg); thumbRow.appendChild(thumb);
      });
      overlay.appendChild(thumbRow);
    }
    document.body.appendChild(overlay);
    this.lightboxEl = overlay;
  }

  closeLightbox(): void {
    if (this.lightboxEl) { document.body.removeChild(this.lightboxEl); this.lightboxEl = null; }
    if (this._lightboxKeyHandler) { document.removeEventListener('keydown', this._lightboxKeyHandler); this._lightboxKeyHandler = null; }
    document.body.style.overflow = '';
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

  onDateTextInput(event: Event, field: 'startDate' | 'endDate'): void {
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

  // Mock data

  private readonly mockLocations: VenueItem[] = [
    {
      id: 'l1', name: 'Šumarice Memorial Park', address: 'Kragujevac, Serbia',
      lat: 44.0213, lng: 20.8943,
      zone: [[44.0250, 20.8890], [44.0270, 20.8980], [44.0240, 20.9020], [44.0200, 20.9010], [44.0185, 20.8950], [44.0200, 20.8890]],
    },
    {
      id: 'l2', name: 'City Center Kragujevac', address: 'Trg Radomira Putnika, Kragujevac',
      lat: 44.011028, lng: 20.916156,
      zone: [[44.0135, 20.9090], [44.0140, 20.9200], [44.0100, 20.9220], [44.0085, 20.9130], [44.0110, 20.9080]],
    },
    {
      id: 'l3', name: 'Veliki Park', address: 'Kragujevackog oktobra bb, Kragujevac',
      lat: 44.0178, lng: 20.9042,
      zone: [[44.0195, 20.9010], [44.0200, 20.9075], [44.0165, 20.9080], [44.0155, 20.9015]],
    },
    {
      id: 'l4', name: 'Bubanj Lake', address: 'Lepenicki bulevar bb, Kragujevac',
      lat: 44.0169, lng: 20.9258,
      zone: [[44.0185, 20.9220], [44.0190, 20.9310], [44.0155, 20.9315], [44.0148, 20.9225]],
    },
    {
      id: '6', name: 'Kragujevac Old Town', address: 'Staro jezgro, Kragujevac',
      lat: 44.0128, lng: 20.9142,
      zone: [[44.0135, 20.9090], [44.0140, 20.9200], [44.0100, 20.9220], [44.0085, 20.9130], [44.0110, 20.9080]],
    },
  ];

  private readonly mockPlaces: VenueItem[] = [
    { id: 'p1', name: 'Sumadija sajam', address: 'Save Kovacevica bb, Kragujevac', lat: 44.024755, lng: 20.930476 },
    { id: 'p2', name: 'Knjazevsko-srpski teatar', address: 'Daniciceva 3, Kragujevac', lat: 44.0106, lng: 20.9103 },
    { id: 'p3', name: 'Hala Jezero', address: 'Grada Sirena 15, Kragujevac', lat: 44.013856, lng: 20.924125 },
    { id: 'p4', name: 'Stadion Cika Daca', address: 'Kralja Milana 4, Kragujevac', lat: 44.021171, lng: 20.899104 },
  ];

  // Venue search state
  venueSearchQuery     = '';
  venueDropdownOpen    = false;
  isSearchingVenue     = false;
  filteredVenueResults: VenueItem[] = [];
  selectedVenue: VenueItem | null   = null;
  selectedVenueAreaKm2: number | null = null;

  // Geo fields
  pinLat: string = '';
  pinLng: string = '';
  streetAddress: string = '';
  pinConfirmed = false;

  // Map modal
  mapModalOpen = false;

  private miniMap    : any = null;
  private modalMap   : any = null;
  private miniMarker : any = null;
  private miniZoneLayer: any = null;
  private modalZoneLayer: any = null;
  private modalMarker: any = null;

  private _venueSearchDebounce: any = null;


  navigateToAddVenue(): void {
    this.venueDropdownOpen = false;
    this.router.navigate([this.venueType === 'place' ? '/places/new' : '/locations/new']);
  }

  // Venue search

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
    if (q.length < 1) {
      this.filteredVenueResults = (this.venueType === 'place' ? this.mockPlaces : this.mockLocations);
      return;
    }
    this.isSearchingVenue = true;
    this._venueSearchDebounce = setTimeout(() => {
      const source = this.venueType === 'place' ? this.mockPlaces : this.mockLocations;
      this.filteredVenueResults = source.filter(
        item => item.name.toLowerCase().includes(q) || item.address.toLowerCase().includes(q)
      );
      this.isSearchingVenue = false;
      this.cdr.detectChanges();
    }, 200);
  }

  onVenueSearchFocus(): void {
    const q = this.venueSearchQuery.trim().toLowerCase();
    const source = this.venueType === 'place' ? this.mockPlaces : this.mockLocations;
    this.filteredVenueResults = q.length === 0
      ? source
      : source.filter(item => item.name.toLowerCase().includes(q) || item.address.toLowerCase().includes(q));
    this.venueDropdownOpen = true;
  }

  selectVenue(item: VenueItem): void {
    this.selectedVenue    = item;
    this.venueSearchQuery = item.name;
    this.venueDropdownOpen = false;
    this.pinConfirmed = true;

    if (this.venueType === 'place') {
      this.pinLat       = item.lat.toFixed(6);
      this.pinLng       = item.lng.toFixed(6);
      this.streetAddress = item.address;
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
    this.pinLat       = '';
    this.pinLng       = '';
    this.streetAddress = '';
  }

  private clearMiniMapLayers(): void {
    if (this.miniMarker   && this.miniMap) { this.miniMap.removeLayer(this.miniMarker);    this.miniMarker    = null; }
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

  // Modal map

  private initModalMap(el: HTMLDivElement): void {
    if (this.modalMap) return;

    let center: [number, number] = [44.0, 20.9];
    let zoom = 9;

    if (this.selectedVenue) {
      if (this.venueType === 'place') {
        const lat = parseFloat(this.pinLat), lng = parseFloat(this.pinLng);
        if (!isNaN(lat) && !isNaN(lng)) { center = [lat, lng]; zoom = 15; }
      } else if (this.selectedVenue.zone && this.selectedVenue.zone.length) {
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

  // Area calculation

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

    const venue: VenueItem = {
      id:      p.id,
      name:    p.name,
      address: p.description,
      lat:     p.lat,
      lng:     p.lng,
    };

    if (p.type === 'Place') {
      this.venueType = 'place';
    } else {
      
      this.venueType = 'location';

      const found = this.mockLocations.find(l =>
        l.id === p.id ||
        l.name === p.name ||
        (Math.abs(l.lat - p.lat) < 0.001 && Math.abs(l.lng - p.lng) < 0.001)
      );
      if (found?.zone) venue.zone = found.zone;
    }

    this.selectVenue(venue);
    this.cdr.detectChanges();
  }

  ngOnDestroy(): void {
    this.destroyModalMap();
    this.closeLightbox();
    this.galleryImages.forEach(img => URL.revokeObjectURL(img.url));
    if (this.coverImageUrl) URL.revokeObjectURL(this.coverImageUrl);
    if (this.miniMap) { this.miniMap.remove(); this.miniMap = null; }
    if (this._venueSearchDebounce) clearTimeout(this._venueSearchDebounce);
  }
  
}