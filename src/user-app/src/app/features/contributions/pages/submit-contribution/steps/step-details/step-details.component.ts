import { AfterViewInit, Component, ElementRef, ViewChild, effect, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { formatDescriptionPlainText, normalizeEditorDescriptionHtml } from '../../../../../../shared/utils/description-formatting';

export interface BookingPartner {
  id: string;
  url: string;
}

interface DayHours {
  name: string;
  open: boolean;
  openTime: string;
  closeTime: string;
}

type TimeField = 'openTime' | 'closeTime';

export interface ContributionOperatingHour {
  dayOfWeek: string;
  isOpen: boolean;
  openTime?: string | null;
  closeTime?: string | null;
}

export interface DetailsData {
  description: string;
  phone: string;
  contactEmail: string;
  website: string;
  openingHoursEnabled: boolean;
  openingHours: string;
  operatingHours: ContributionOperatingHour[];
  bookingPartners: BookingPartner[];
}
@Component({
  selector: 'app-step-details',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './step-details.component.html',
})
export class StepDetailsComponent implements AfterViewInit {
  @ViewChild('descriptionEditor') private descriptionEditor?: ElementRef<HTMLDivElement>;

  data = input<DetailsData | null>(null);
  isAccommodation = input(false);
  saved = output<DetailsData>();
  back = output<void>();
  changed = output<DetailsData>();
  submitted = false;
  openingHoursError = '';
  activeTimePicker: { day: DayHours; field: TimeField } | null = null;
  timePickerMode: 'hour' | 'minute' = 'hour';
  isDraggingMinute = false;
  readonly minuteHandRadius = 100;
  readonly clockOuterHours = [12, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
  readonly clockInnerHours = [0, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23];
  readonly clockMinutes = Array.from({ length: 12 }, (_, index) => index * 5);

  bookingPartners = [
    { id: 'booking', label: 'Booking.com' },
    { id: 'airbnb', label: 'Airbnb' },
    { id: 'hotel', label: 'Official Website' },
  ];
  days: DayHours[] = [
    { name: 'Monday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Tuesday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Wednesday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Thursday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Friday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Saturday', open: true, openTime: '10:00', closeTime: '20:00' },
    { name: 'Sunday', open: false, openTime: '09:00', closeTime: '17:00' },
  ];
    constructor() {
    effect(() => {
      const d = this.data();
      if (d) {
        Object.assign(this.form, {
          ...d,
          description: normalizeEditorDescriptionHtml(d.description),
        });
        if (!this.descriptionEditorHasFocus()) {
          this.writeDescriptionEditor();
        }
        if (d.operatingHours?.length) {
          this.applyOperatingHours(d.operatingHours);
        } else {
          this.applyOpeningHoursText(d.openingHours);
        }
        this.moveAccommodationWebsiteToBookingPartner();
      }
    });
  }
  form: DetailsData = {
    description: '',
    phone: '',
    contactEmail: '',
    website: '',
    openingHoursEnabled: false,
    openingHours: '',
    operatingHours: [],
    bookingPartners: [],
  };

  ngAfterViewInit(): void {
    this.writeDescriptionEditor();
  }

  get descriptionTextLength(): number {
    return formatDescriptionPlainText(this.form.description).length;
  }

  get hasDescriptionText(): boolean {
    return this.descriptionTextLength > 0;
  }

  onContinue(): void {
    this.submitted = true;
    this.openingHoursError = '';
    if (!this.isValid()) return;

    this.saved.emit(this.normalizedForm());
  }

  isValid(): boolean {
    this.openingHoursError = this.validateOpeningHours();
    return !this.openingHoursError &&
      (this.isAccommodation() || !this.websiteError()) &&
      (!this.isAccommodation() || this.form.bookingPartners.every(partner => !this.partnerUrlError(partner.id)));
  }

  websiteError(): string {
    return this.validateOptionalUrl(this.form.website, 'Website URL');
  }

  partnerUrlError(id: string): string {
    if (!this.isPartnerSelected(id)) return '';

    return this.validateRequiredUrl(this.getPartnerUrl(id), 'Booking partner URL');
  }

  togglePartner(id: string): void {
    if (this.isPartnerSelected(id)) {
      this.form.bookingPartners = this.form.bookingPartners.filter(p => p.id !== id);
    } else {
      this.form.bookingPartners = [...this.form.bookingPartners, { id, url: '' }];
    }
    this.changed.emit({ ...this.form });
  }

  isPartnerSelected(id: string): boolean {
    return this.form.bookingPartners.some(p => p.id === id);
  }

  getPartnerUrl(id: string): string {
    return this.form.bookingPartners.find(p => p.id === id)?.url ?? '';
  }

  updatePartnerUrl(id: string, url: string): void {
    this.form.bookingPartners = this.form.bookingPartners.map(p =>
      p.id === id ? { ...p, url } : p
    );
    this.changed.emit({ ...this.form });
  }


  onDescriptionBeforeInput(event: InputEvent): void {
    if (event.inputType === 'insertParagraph' || event.inputType === 'insertLineBreak') {
      event.preventDefault();
      this.insertDescriptionHtml('<br>');
      const editor = this.descriptionEditor?.nativeElement;
      if (editor) {
        const last = editor.lastChild;
        const lastMeaningful =
          last?.nodeType === Node.TEXT_NODE && last.textContent === ''
            ? last.previousSibling
            : last;
        if (lastMeaningful?.nodeName === 'BR') {
          const phantom = document.createElement('br');
          editor.appendChild(phantom);
          const sel = window.getSelection();
          if (sel) {
            const r = document.createRange();
            r.setStartBefore(phantom);
            r.collapse(true);
            sel.removeAllRanges();
            sel.addRange(r);
          }
        }
      }
      this.onDescriptionChanged(false);
    }
  }

  onDescriptionPaste(event: ClipboardEvent): void {
    const clipboard = event.clipboardData;
    if (!clipboard) return;

    event.preventDefault();
    const html = clipboard.getData('text/html');
    const text = clipboard.getData('text/plain');
    document.execCommand('insertHTML', false, normalizeEditorDescriptionHtml(html || text));
    this.onDescriptionChanged(false);
  }

  onDescriptionBlur(): void {
    const editor = this.descriptionEditor?.nativeElement;
    if (!editor) return;

    editor.innerHTML = normalizeEditorDescriptionHtml(editor.innerHTML);
    this.onDescriptionChanged(false);
  }

  onDescriptionChanged(_syncToolbarState = false): void {
    this.form.description = normalizeEditorDescriptionHtml(this.descriptionEditor?.nativeElement.innerHTML);
    this.changed.emit(this.normalizedForm());
  }

  onOpeningHoursChanged(): void {
    this.openingHoursError = '';
    this.changed.emit(this.normalizedForm());
  }

  openTimePicker(day: DayHours, field: TimeField): void {
    this.activeTimePicker = { day, field };
    this.timePickerMode = 'hour';
  }

  closeTimePicker(): void {
    this.activeTimePicker = null;
  }

  onTimeInput(day: DayHours, field: TimeField, value: string): void {
    day[field] = value.replace(/[^\d:]/g, '').slice(0, 5);
    this.onOpeningHoursChanged();
  }

  onTimeBlur(day: DayHours, field: TimeField): void {
    const normalized = this.normalizeTimeInput(day[field]);
    if (normalized) {
      day[field] = normalized;
      this.onOpeningHoursChanged();
    }
  }

  selectClockHour(hour: number): void {
    const picker = this.activeTimePicker;
    if (!picker) return;

    const [, minutes] = picker.day[picker.field].split(':');
    picker.day[picker.field] = `${this.padTimePart(hour)}:${minutes || '00'}`;
    this.timePickerMode = 'minute';
    this.onOpeningHoursChanged();
  }

  minuteCircleStyle(): Record<string, string> {
    const minute = this.currentMinute();
    const angle = (minute / 60) * 360 - 90;
    const radius = 100;
    const x = Math.cos(angle * Math.PI / 180) * radius;
    const y = Math.sin(angle * Math.PI / 180) * radius;
    return {
      left: `calc(50% + ${x}px)`,
      top: `calc(50% + ${y}px)`,
      transform: 'translate(-50%, -50%)',
    };
  }

  minuteHandAngle(): number {
    const picker = this.activeTimePicker;
    if (!picker) return 0;
    return (Number(picker.day[picker.field].slice(3, 5)) || 0) / 60 * 360;
  }

  currentMinute(): number {
    const picker = this.activeTimePicker;
    if (!picker) return 0;
    return Number(picker.day[picker.field].slice(3, 5)) || 0;
  }

  onMinutePointerDown(event: MouseEvent | TouchEvent, el: HTMLElement): void {
    if (this.timePickerMode !== 'minute') return;
    this.isDraggingMinute = true;
    this.applyMinuteFromEvent(event, el);
    event.preventDefault();
  }

  onMinutePointerMove(event: MouseEvent | TouchEvent, el: HTMLElement): void {
    if (!this.isDraggingMinute) return;
    this.applyMinuteFromEvent(event, el);
    event.preventDefault();
  }

  onMinutePointerUp(): void {
    this.isDraggingMinute = false;
  }

  private applyMinuteFromEvent(event: MouseEvent | TouchEvent, el: HTMLElement): void {
    const rect = el.getBoundingClientRect();
    const cx = rect.left + rect.width / 2;
    const cy = rect.top + rect.height / 2;
    let clientX: number, clientY: number;
    if (window.TouchEvent && event instanceof TouchEvent) {
      if (!event.touches.length) return;
      clientX = event.touches[0].clientX;
      clientY = event.touches[0].clientY;
    } else {
      clientX = (event as MouseEvent).clientX;
      clientY = (event as MouseEvent).clientY;
    }
    const angle = Math.atan2(clientY - cy, clientX - cx) * 180 / Math.PI + 90;
    const minute = Math.round(((angle % 360) + 360) % 360 / 360 * 60) % 60;
    const picker = this.activeTimePicker;
    if (!picker) return;
    const [hours] = picker.day[picker.field].split(':');
    picker.day[picker.field] = `${hours || '00'}:${this.padTimePart(minute)}`;
    this.onOpeningHoursChanged();
  }

  selectClockMinute(minute: number): void {
    const picker = this.activeTimePicker;
    if (!picker) return;

    const [hours] = picker.day[picker.field].split(':');
    picker.day[picker.field] = `${hours || '00'}:${this.padTimePart(minute)}`;
    this.onOpeningHoursChanged();
  }

  clockHourStyle(hour: number, ring: 'outer' | 'inner'): Record<string, string> {
    const arr = ring === 'outer' ? this.clockOuterHours : this.clockInnerHours;
    const index = arr.indexOf(hour);
    const angle = (index / 12) * 360 - 90;
    const radius = ring === 'outer' ? 100 : 68;
    const x = Math.cos(angle * Math.PI / 180) * radius;
    const y = Math.sin(angle * Math.PI / 180) * radius;
    return {
      left: `calc(50% + ${x}px)`,
      top: `calc(50% + ${y}px)`,
      transform: 'translate(-50%, -50%)',
    };
  }

  clockButtonStyle(value: number, total: number): Record<string, string> {
    const angle = (value / total) * 360 - 90;
    const radius = 100;
    const x = Math.cos(angle * Math.PI / 180) * radius;
    const y = Math.sin(angle * Math.PI / 180) * radius;
    return {
      left: `calc(50% + ${x}px)`,
      top: `calc(50% + ${y}px)`,
      transform: 'translate(-50%, -50%)',
    };
  }

  isSelectedClockHour(hour: number): boolean {
    const picker = this.activeTimePicker;
    return !!picker && Number(picker.day[picker.field].slice(0, 2)) === hour;
  }

  isSelectedClockMinute(minute: number): boolean {
    const picker = this.activeTimePicker;
    return !!picker && Number(picker.day[picker.field].slice(3, 5)) === minute;
  }

  formatClockPart(value: number): string {
    return this.padTimePart(value);
  }

  private normalizedForm(): DetailsData {
    const openingHours = this.form.openingHoursEnabled ? this.buildOpeningHoursText() : '';
    const operatingHours = this.form.openingHoursEnabled ? this.buildOperatingHoursPayload() : [];
    return {
      description: normalizeEditorDescriptionHtml(this.form.description),
      phone: this.form.phone.trim(),
      contactEmail: this.form.contactEmail.trim(),
      website: this.isAccommodation() ? '' : this.form.website.trim(),
      openingHoursEnabled: this.form.openingHoursEnabled,
      openingHours,
      operatingHours,
      bookingPartners: this.isAccommodation() ? this.form.bookingPartners.map(partner => ({
        id: partner.id,
        url: partner.url.trim(),
      })) : [],
    };
  }

  private buildOpeningHoursText(): string {
    return this.days
      .map(day => day.open ? `${day.name}: ${day.openTime} - ${day.closeTime}` : `${day.name}: Closed`)
      .join('\n');
  }

  private writeDescriptionEditor(): void {
    const editor = this.descriptionEditor?.nativeElement;
    if (!editor) return;

    const html = normalizeEditorDescriptionHtml(this.form.description);
    if (editor.innerHTML !== html) {
      editor.innerHTML = html;
    }
  }

  private descriptionEditorHasFocus(): boolean {
    return document.activeElement === this.descriptionEditor?.nativeElement;
  }

  private insertDescriptionHtml(html: string): void {
    const editor = this.descriptionEditor?.nativeElement;
    if (!editor) return;

    editor.focus();
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0 || !editor.contains(selection.getRangeAt(0).commonAncestorContainer)) {
      const range = document.createRange();
      range.selectNodeContents(editor);
      range.collapse(false);
      selection?.removeAllRanges();
      selection?.addRange(range);
    }

    const range = window.getSelection()?.getRangeAt(0);
    if (!range) return;

    range.deleteContents();
    const fragment = range.createContextualFragment(html);
    const lastNode = fragment.lastChild;
    range.insertNode(fragment);
    if (lastNode) {
      range.setStartAfter(lastNode);
      range.collapse(true);
      selection?.removeAllRanges();
      selection?.addRange(range);
    }
  }

  private buildOperatingHoursPayload(): ContributionOperatingHour[] {
    return this.days.map(day => day.open
      ? {
          dayOfWeek: day.name,
          isOpen: true,
          openTime: day.openTime,
          closeTime: day.closeTime,
        }
      : {
          dayOfWeek: day.name,
          isOpen: false,
          openTime: null,
          closeTime: null,
        });
  }

  private validateOpeningHours(): string {
    if (!this.form.openingHoursEnabled) return '';

    for (const day of this.days) {
      if (day.open && day.openTime === day.closeTime) {
        return `${day.name} opening time and closing time cannot be the same.`;
      }

      if (day.open && (!this.isValidClockTime(day.openTime) || !this.isValidClockTime(day.closeTime))) {
        return `${day.name} opening hours must use 24-hour HH:mm format.`;
      }
    }

    return '';
  }

  private applyOpeningHoursText(value: string): void {
    if (!value.trim()) return;

    const byDay = new Map<string, Partial<DayHours>>();
    for (const line of value.split(/\r?\n/)) {
      const match = line.match(/^\s*([^:]+):\s*(closed|(\d{2}:\d{2})\s*[-–]\s*(\d{2}:\d{2}))\s*$/i);
      if (!match) continue;

      byDay.set(match[1].trim().toLowerCase(), match[2].toLowerCase() === 'closed'
        ? { open: false }
        : { open: true, openTime: match[3], closeTime: match[4] });
    }

    if (byDay.size === 0) return;

    for (const day of this.days) {
      const patch = byDay.get(day.name.toLowerCase());
      if (patch) Object.assign(day, patch);
    }
  }

  private applyOperatingHours(hours: ContributionOperatingHour[]): void {
    const byDay = new Map(hours.map(hour => [hour.dayOfWeek.toLowerCase(), hour]));
    for (const day of this.days) {
      const incoming = byDay.get(day.name.toLowerCase());
      if (!incoming) continue;
      day.open = incoming.isOpen;
      day.openTime = incoming.openTime ?? day.openTime;
      day.closeTime = incoming.closeTime ?? day.closeTime;
    }
  }

  private normalizeTimeInput(value: string): string | null {
    const trimmed = value.trim();
    const match = trimmed.match(/^(\d{1,2})(?::?(\d{0,2}))?$/);
    if (!match) return null;

    const hours = Number(match[1]);
    const minutes = Number((match[2] || '0').padEnd(2, '0'));
    if (!Number.isInteger(hours) || !Number.isInteger(minutes) || hours < 0 || hours > 23 || minutes < 0 || minutes > 59) {
      return null;
    }

    return `${this.padTimePart(hours)}:${this.padTimePart(minutes)}`;
  }

  private isValidClockTime(value: string): boolean {
    return /^([01]\d|2[0-3]):[0-5]\d$/.test(value);
  }

  private padTimePart(value: number): string {
    return value.toString().padStart(2, '0');
  }

  private moveAccommodationWebsiteToBookingPartner(): void {
    if (!this.isAccommodation()) return;

    const website = this.form.website.trim();
    if (!website || this.form.bookingPartners.some(partner => partner.id === 'hotel')) {
      this.form.website = '';
      return;
    }

    this.form.bookingPartners = [
      ...this.form.bookingPartners,
      { id: 'hotel', url: website },
    ];
    this.form.website = '';
  }

  private validateOptionalUrl(value: string, label: string): string {
    const trimmed = value.trim();
    if (!trimmed) return '';

    return this.validateUrl(trimmed, label);
  }

  private validateRequiredUrl(value: string, label: string): string {
    const trimmed = value.trim();
    if (!trimmed) return `${label} is required.`;

    return this.validateUrl(trimmed, label);
  }

  private validateUrl(value: string, label: string): string {
    if (value.length > 500) return `${label} must be 500 characters or fewer.`;

    try {
      const parsed = new URL(value);
      if (parsed.protocol !== 'https:' || !parsed.hostname || parsed.username || parsed.password) {
        return `${label} must be a valid https URL.`;
      }
    } catch {
      return `${label} must be a valid https URL.`;
    }

    return '';
  }
}
