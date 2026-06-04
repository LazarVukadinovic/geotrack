import { Component, input, output, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export interface BookingPartner {
  id: string;
  url: string;
}
export interface DetailsData {
  description: string;
  phone: string;
  contactEmail: string;
  website: string;
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

  bookingPartners = [
    { id: 'booking', label: 'Booking.com' },
    { id: 'airbnb', label: 'Airbnb' },
    { id: 'hotel', label: 'Official Website' },
  ];
    constructor() {
    effect(() => {
      const d = this.data();
      if (d) {
        Object.assign(this.form, d);
        this.moveAccommodationWebsiteToBookingPartner();
      }
    });
  }
  form: DetailsData = {
    description: '',
    phone: '',
    contactEmail: '',
    website: '',
    bookingPartners: [],
  };

  onContinue(): void {
    this.submitted = true;
    if (!this.isValid()) return;

    this.saved.emit(this.normalizedForm());
  }

  isValid(): boolean {
    return (this.isAccommodation() || !this.websiteError()) &&
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

  private normalizedForm(): DetailsData {
    return {
      description: this.form.description.trim(),
      phone: this.form.phone.trim(),
      contactEmail: this.form.contactEmail.trim(),
      website: this.isAccommodation() ? '' : this.form.website.trim(),
      bookingPartners: this.isAccommodation() ? this.form.bookingPartners.map(partner => ({
        id: partner.id,
        url: partner.url.trim(),
      })) : [],
    };
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
