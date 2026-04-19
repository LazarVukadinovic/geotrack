import { Component, input, output, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export interface DetailsData {
  description: string;
  phone: string;
  website: string;
  bookingPartners: string[];
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

  form: DetailsData = { description: '', phone: '', website: '', bookingPartners: [] };

  // ngOnInit() { if (this.data()) Object.assign(this.form, this.data()); }

  togglePartner(id: string): void {
    if (this.isPartnerSelected(id)) this.form.bookingPartners = this.form.bookingPartners.filter(p => p !== id);
    else this.form.bookingPartners = [...this.form.bookingPartners, id];
    this.changed.emit({ ...this.form });
  }

  isPartnerSelected(id: string): boolean { return this.form.bookingPartners.includes(id); }

  onContinue(): void { this.saved.emit({ ...this.form }); }
}