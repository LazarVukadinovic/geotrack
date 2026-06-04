import { Component, input, output, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

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

export interface DetailsData {
  description: string;
  phone: string;
  contactEmail: string;
  website: string;
  openingHoursEnabled: boolean;
  openingHours: string;
  bookingPartners: BookingPartner[];
}
@Component({
  selector: 'app-step-details',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './step-details.component.html',
})
export class StepDetailsComponent {
  data = input<DetailsData | null>(null);
  isAccommodation = input(false);
  saved = output<DetailsData>();
  back = output<void>();
  changed = output<DetailsData>();
  submitted = false;
  openingHoursError = '';

  bookingPartners = [
    { id: 'booking', label: 'Booking.com' },
    { id: 'airbnb', label: 'Airbnb' },
    { id: 'hotel', label: 'Official Website' },
  ];
  allHours = Array.from({ length: 24 }, (_, hour) => `${hour.toString().padStart(2, '0')}:00`);
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
        Object.assign(this.form, d);
        this.applyOpeningHoursText(d.openingHours);
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
    bookingPartners: [],
  };

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

  onOpeningHoursChanged(): void {
    this.openingHoursError = '';
    this.changed.emit(this.normalizedForm());
  }

  private normalizedForm(): DetailsData {
    const openingHours = this.form.openingHoursEnabled ? this.buildOpeningHoursText() : '';
    return {
      description: this.form.description.trim(),
      phone: this.form.phone.trim(),
      contactEmail: this.form.contactEmail.trim(),
      website: this.isAccommodation() ? '' : this.form.website.trim(),
      openingHoursEnabled: this.form.openingHoursEnabled,
      openingHours,
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

  private validateOpeningHours(): string {
    if (!this.form.openingHoursEnabled) return '';

    for (const day of this.days) {
      if (day.open && this.toMinutes(day.openTime) >= this.toMinutes(day.closeTime)) {
        return `${day.name} opening time must be before closing time.`;
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

    this.days = this.days.map(day => ({ ...day, ...(byDay.get(day.name.toLowerCase()) ?? {}) }));
  }

  private toMinutes(value: string): number {
    const [hours, minutes] = value.split(':').map(part => Number(part));
    return hours * 60 + minutes;
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
