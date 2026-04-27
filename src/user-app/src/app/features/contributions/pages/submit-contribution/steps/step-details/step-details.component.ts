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
  pricePerNight: string;
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
  saved = output<DetailsData>();
  back = output<void>();
  changed = output<DetailsData>();

  bookingPartners = [
    { id: 'booking', label: 'Booking.com' },
    { id: 'airbnb', label: 'Airbnb' },
    { id: 'hotel', label: 'Hotel Website' },
  ];
    constructor() {
    effect(() => {
      const d = this.data();
      if (d) Object.assign(this.form, d);
    });
  }
  form: DetailsData = {
    description: '',
    phone: '',
    contactEmail: '',
    website: '',
    pricePerNight: '',
    bookingPartners: [],
  };
  onContinue(): void { this.saved.emit({ ...this.form }); }

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
}
