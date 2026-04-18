import { Component, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export interface BasicsData {
  name: string;
  stayType: string;
  address: string;
  city: string;
  region: string;
  country: string;
  imageUrl: string;
}

@Component({
  selector: 'app-step-basics',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './step-basics.component.html',
})
export class StepBasicsComponent {
  data = input<BasicsData | null>(null);
  saved = output<BasicsData>();

  stayTypes = ['Hotel', 'Villa', 'Apartment', 'Resort', 'Cabin', 'Glamping'];
  previewUrl = '';

  form: BasicsData = { name: '', stayType: 'Hotel', address: '', city: '', region: '', country: '', imageUrl: '' };

  ngOnInit() {
    if (this.data()) Object.assign(this.form, this.data());
  }

  onImageSelected(e: Event): void {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => { this.previewUrl = reader.result as string; this.form.imageUrl = this.previewUrl; };
    reader.readAsDataURL(file);
  }

  isValid(): boolean {
    return !!this.form.name.trim() && !!this.form.city.trim() && !!this.form.country.trim();
  }

  onContinue(): void {
    if (this.isValid()) this.saved.emit({ ...this.form });
  }
}