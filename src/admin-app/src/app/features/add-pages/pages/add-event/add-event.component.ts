import { Component, ElementRef, ViewChild, NgZone, ChangeDetectorRef, AfterViewInit, OnDestroy, HostListener } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';

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

  constructor(private zone: NgZone, private cdr: ChangeDetectorRef) {}

  @ViewChild('editorRef')         editorRef!:            ElementRef<HTMLDivElement>;
  @ViewChild('miniMapContainer')  miniMapContainer!:     ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  //Basic fields
  eventTitle = 'Summer Festival';
  venueType  = 'location';
  allDay     = false;
  freeEvent  = false;
  recurring  = false;
  startDate  = this.todayIso();
  endDate    = this.todayIso();
  startTime  = '21:00';
  endTime    = '05:00';

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
    this.editorIsEmpty = text.trim() === '' && el.innerHTML !== '<img' && !el.querySelector('img');
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

  //Lightbox
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

  activePicker: string | null = null;

  calendarYear  = new Date().getFullYear();
  calendarMonth = new Date().getMonth();

  readonly monthNames = [
    'January','February','March','April','May','June',
    'July','August','September','October','November','December'
  ];
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
    const iso = `${this.calendarYear}-${m}-${d}`;
    if (field === 'startDate') this.startDate = iso;
    else                       this.endDate   = iso;
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
    return now.getFullYear() === this.calendarYear &&
           now.getMonth()    === this.calendarMonth &&
           now.getDate()     === day;
  }

  formatDisplayDate(iso: string): string {
    if (!iso) return '';
    const [y, m, d] = iso.split('-').map(Number);
    return `${this.monthNames[m - 1].slice(0, 3)} ${d}, ${y}`;
  }
  private todayIso(): string {
    const now = new Date();
    const y = now.getFullYear();
    const m = String(now.getMonth() + 1).padStart(2, '0');
    const d = String(now.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }
  get startDateDisplay(): string { return this.isoToDisplay(this.startDate); }
  get endDateDisplay():   string { return this.isoToDisplay(this.endDate); }

  private isoToDisplay(iso: string): string {
    if (!iso) return '';
    const [y, m, d] = iso.split('-');
    return `${d}.${m}.${y}`;
  }

  private displayToIso(display: string): string | null {
    const clean = display.replace(/[\/\-\s]/g, '.');
    const parts = clean.split('.');
    if (parts.length !== 3) return null;
    const [d, m, y] = parts.map(p => p.padStart(2, '0'));
    if (!d || !m || !y || y.length < 4) return null;
    const day = parseInt(d), month = parseInt(m), year = parseInt(y);
    if (isNaN(day) || isNaN(month) || isNaN(year)) return null;
    if (month < 1 || month > 12 || day < 1 || day > 31) return null;
    return `${y}-${m.padStart(2,'0')}-${d.padStart(2,'0')}`;
  }

  onDateTextInput(event: Event, field: 'startDate' | 'endDate'): void {
    const input = event.target as HTMLInputElement;
    let val = input.value.replace(/[^\d\.\/]/g, '');
    const digits = val.replace(/\D/g, '');
    if (digits.length >= 3 && digits.length <= 4 && !val.includes('.')) {
      val = digits.slice(0, 2) + '.' + digits.slice(2);
    } else if (digits.length >= 5 && val.split('.').length < 3) {
      const d = digits.slice(0, 2), m = digits.slice(2, 4), y = digits.slice(4);
      val = `${d}.${m}.${y ? '.' + y : ''}`.replace('..', '.');
    }
    input.value = val;
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
  clockMode: 'hour' | 'minute' = 'hour';
  selectedHour   = 12;
  selectedMinute = 0;
  selectedAmPm: 'AM' | 'PM' = 'AM';

  readonly clockCX = CX;
  readonly clockCY = CY;
  readonly clockR  = R;

  get clockHourMarkers(): { value: number; x: number; y: number; selected: boolean }[] {
    return Array.from({ length: 12 }, (_, i) => {
      const h = i + 1;
      const angle = (h / 12) * Math.PI * 2 - Math.PI / 2;
      return { value: h, x: CX + R * Math.cos(angle), y: CY + R * Math.sin(angle), selected: h === this.selectedHour };
    });
  }

  get clockMinuteMarkers(): { value: number; x: number; y: number; selected: boolean }[] {
    return Array.from({ length: 12 }, (_, i) => {
      const m = i * 5;
      const angle = (m / 60) * Math.PI * 2 - Math.PI / 2;
      return { value: m, x: CX + R * Math.cos(angle), y: CY + R * Math.sin(angle), selected: m === this.selectedMinute };
    });
  }

  get clockHandEnd(): { x: number; y: number } {
    if (this.clockMode === 'hour') {
      const angle = (this.selectedHour / 12) * Math.PI * 2 - Math.PI / 2;
      return { x: CX + R * Math.cos(angle), y: CY + R * Math.sin(angle) };
    }
    const angle = (this.selectedMinute / 60) * Math.PI * 2 - Math.PI / 2;
    return { x: CX + R * Math.cos(angle), y: CY + R * Math.sin(angle) };
  }

  setClockMode(m: 'hour' | 'minute'): void { this.clockMode = m; }
  setClockAmPm(ap: 'AM' | 'PM'): void { this.selectedAmPm = ap; }

  onClockInteraction(event: MouseEvent | TouchEvent, svgEl: Element): void {
    event.preventDefault();
    event.stopPropagation();

    const rect  = svgEl.getBoundingClientRect();
    const clientX = event instanceof TouchEvent ? event.touches[0].clientX : event.clientX;
    const clientY = event instanceof TouchEvent ? event.touches[0].clientY : event.clientY;

    const svgX = (clientX - rect.left)  / rect.width  * 220;
    const svgY = (clientY - rect.top)   / rect.height * 220;
    const angle = Math.atan2(svgY - CY, svgX - CX);

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
    const curr = this.selectedMinute;
    const diff = Math.abs(m - curr);
    return diff === 5 || diff === 55;
  }

  @HostListener('document:mousemove', ['$event'])
  onDocumentMouseMove(event: MouseEvent): void {
    if (this._clockDragging && this._clockSvgEl) {
      this.onClockInteraction(event, this._clockSvgEl);
    }
  }

  @HostListener('document:mouseup')
  onDocumentMouseUp(): void { this._clockDragging = false; this._clockSvgEl = null; }

  confirmTime(field: 'startTime' | 'endTime'): void {
    let h = this.selectedHour;
    if (this.selectedAmPm === 'AM' && h === 12) h = 0;
    if (this.selectedAmPm === 'PM' && h !== 12) h += 12;
    const timeStr = `${String(h).padStart(2, '0')}:${String(this.selectedMinute).padStart(2, '0')}`;
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
    return `${h}:${String(mm).padStart(2, '0')} ${ampm}`;
  }


  togglePicker(name: string, event: MouseEvent): void {
    event.stopPropagation();
    if (this.activePicker === name) { this.activePicker = null; return; }
    this.activePicker = name;

    if (name === 'startDate' || name === 'endDate') {
      const iso = name === 'startDate' ? this.startDate : this.endDate;
      if (iso) {
        const [y, m] = iso.split('-').map(Number);
        this.calendarYear  = y;
        this.calendarMonth = m - 1;
      } else {
        const now = new Date();
        this.calendarYear  = now.getFullYear();
        this.calendarMonth = now.getMonth();
      }
    }

    if (name === 'startTime' || name === 'endTime') {
      const t = name === 'startTime' ? this.startTime : this.endTime;
      this._loadClockState(t);
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
    if (this.activePicker) {
      this.activePicker = null;
      this.cdr.detectChanges();
    }
  }

  //Geographic pin / Leaflet
  pinModalOpen = false;
  pinConfirmed = false;
  pinLat: string = '';
  pinLng: string = '';
  streetAddress: string = '';
  isGeocodingAddress = false;
  modalPinLat: number | null = null;
  modalPinLng: number | null = null;

  private miniMap    : any = null;
  private modalMap   : any = null;
  private miniMarker : any = null;
  private modalMarker: any = null;
  private _geocodeDebounce: any = null;

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


  private initModalMap(el: HTMLDivElement): void {
    if (this.modalMap) return;
    const lat = parseFloat(this.pinLat), lng = parseFloat(this.pinLng);
    const center: [number, number] = (!isNaN(lat) && !isNaN(lng)) ? [lat, lng] : [44.0, 20.9];
    const zoom = !isNaN(lat) ? 15 : 9;
    this.modalMap = L.map(el, { zoomControl: true, attributionControl: false }).setView(center, zoom);
    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', { maxZoom: 19 }).addTo(this.modalMap);
    setTimeout(() => this.modalMap?.invalidateSize(), 200);
    if (!isNaN(lat) && !isNaN(lng)) {
      this.modalMarker = L.marker([lat, lng], { icon: this.makePinIcon(), draggable: false }).addTo(this.modalMap);
    }
  }

  private destroyModalMap(): void {
    if (this.modalMap) { this.modalMap.remove(); this.modalMap = null; this.modalMarker = null; }
  }

  private updateMiniMapPin(lat: number, lng: number): void {
    if (!this.miniMap) this.initMiniMap();
    if (!this.miniMap) return;
    if (this.miniMarker) this.miniMarker.setLatLng([lat, lng]);
    else this.miniMarker = L.marker([lat, lng], { icon: this.makePinIcon(), draggable: false }).addTo(this.miniMap);
    this.miniMap.setView([lat, lng], 14);
  }

  //Classification
  classItems: ClassItem[] = [
    { label: 'Music',           checked: false },
    { label: 'Outdoor',         checked: false },
    { label: 'Culture',         checked: false },
    { label: 'Family Friendly', checked: false },
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

  //Lifecycle
  ngAfterViewInit(): void { setTimeout(() => this.initMiniMap(), 0); }

  ngOnDestroy(): void {
    this.destroyModalMap();
    this.closeLightbox();
    this.galleryImages.forEach(img => URL.revokeObjectURL(img.url));
    if (this.coverImageUrl) URL.revokeObjectURL(this.coverImageUrl);
    if (this.miniMap) { this.miniMap.remove(); this.miniMap = null; }
    if (this._geocodeDebounce) clearTimeout(this._geocodeDebounce);
  }
  // Mock data
  private readonly mockLocations = [
    { id: 'l1', name: 'Šumarice Memorial Park', address: 'Kragujevac, Serbia', lat: 44.0213, lng: 20.8943 },
    { id: 'l2', name: 'City Center Kragujevac', address: 'Trg Radomira Putnika, Kragujevac', lat: 44.011028, lng: 20.916156 },
    { id: 'l3', name: 'Veliki Park', address: 'Kragujevackog oktobra bb, Kragujevac', lat: 44.0178, lng: 20.9042 },
    { id: 'l4', name: 'Bubanj Lake', address: 'Lepenicki bulevar bb, Kragujevac', lat: 44.0169, lng: 20.9258 },
  ];

  private readonly mockPlaces = [
    { id: 'p1', name: 'Sumadija sajam', address: 'Save Kovacevica bb, Kragujevac', lat: 44.024755, lng: 20.930476 },
    { id: 'p2', name: 'Knjazevsko-srpski teatar', address: 'Daniciceva 3, Kragujevac', lat: 44.0106, lng: 20.9103 },
    { id: 'p3', name: 'Hala Jezero', address: 'Grada Sirena 15, Kragujevac', lat: 44.013856, lng: 20.924125 },
    { id: 'p4', name: 'Stadion Cika Daca', address: 'Kralja Milana 4, Kragujevac', lat: 44.021171, lng: 20.899104 },
  ];

  // Venue search state
  venueSearchQuery    = '';
  venueDropdownOpen   = false;
  isSearchingVenue    = false;
  filteredVenueResults: { id: string; name: string; address: string; lat: number; lng: number }[] = [];
  selectedVenue: { id: string; name: string; address: string; lat: number; lng: number } | null = null;

  // Map modal
  mapModalOpen = false;

  private _venueSearchDebounce: any = null;

  onVenueTypeChange(): void {
    this.venueSearchQuery = '';
    this.filteredVenueResults = [];
    this.venueDropdownOpen = false;
    this.selectedVenue = null;
    this.clearGeoFields();
  }

  onVenueSearch(): void {
    if (this._venueSearchDebounce) clearTimeout(this._venueSearchDebounce);
    const q = this.venueSearchQuery.trim().toLowerCase();
    if (q.length < 1) { this.venueDropdownOpen = false; this.filteredVenueResults = []; return; }
    this.isSearchingVenue = true;
    this.venueDropdownOpen = true;
    this._venueSearchDebounce = setTimeout(() => {
      const source = this.venueType === 'place' ? this.mockPlaces : this.mockLocations;
      this.filteredVenueResults = source.filter(
        item => item.name.toLowerCase().includes(q) || item.address.toLowerCase().includes(q)
      );
      this.isSearchingVenue = false;
      this.cdr.detectChanges();
    }, 300);
  }

  onVenueSearchFocus(): void {
    if (this.venueSearchQuery.trim().length > 0) {
      this.venueDropdownOpen = true;
    }
  }

  selectVenue(item: { id: string; name: string; address: string; lat: number; lng: number }): void {
    this.selectedVenue     = item;
    this.venueSearchQuery  = item.name;
    this.venueDropdownOpen = false;
    this.pinLat            = item.lat.toFixed(6);
    this.pinLng            = item.lng.toFixed(6);
    this.streetAddress     = item.address;
    this.pinConfirmed      = true;
    this.updateMiniMapPin(item.lat, item.lng);
  }

  clearVenue(): void {
    this.selectedVenue    = null;
    this.venueSearchQuery = '';
    this.clearGeoFields();
    if (this.miniMarker) { this.miniMap?.removeLayer(this.miniMarker); this.miniMarker = null; }
    if (this.miniMap) this.miniMap.setView([44.0, 20.9], 8);
    this.pinConfirmed = false;
  }

  private clearGeoFields(): void {
    this.pinLat       = '';
    this.pinLng       = '';
    this.streetAddress = '';
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
}