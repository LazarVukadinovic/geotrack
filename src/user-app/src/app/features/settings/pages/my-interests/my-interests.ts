import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { ActivatedRoute } from '@angular/router';

export interface Interest {
  id: string;
  label: string;
}
const ALL_INTERESTS: Interest[] = [
  { id: 'food',        label: 'Food & Drink' },
  { id: 'history',     label: 'History' },
  { id: 'beaches',     label: 'Beaches' },
  { id: 'nature',      label: 'Nature' },
  { id: 'museums',     label: 'Museums' },
  { id: 'events',      label: 'Local Events' },
  { id: 'nightlife',   label: 'Nightlife' },
  { id: 'walks',       label: 'Scenic Walks' },
  { id: 'festivals',   label: 'Festivals' },
  { id: 'adventure',   label: 'Adventure' },
  { id: 'shopping',    label: 'Shopping' },
  { id: 'culture',     label: 'Culture' },
  { id: 'wellness',    label: 'Wellness & Spa' },
  { id: 'sports',      label: 'Sports' },
  { id: 'photography', label: 'Photography' },
  { id: 'architecture',label: 'Architecture' },
  { id: 'roadtrips',   label: 'Road Trips' },
  { id: 'wine',        label: 'Wine & Tasting' },
  { id: 'art',         label: 'Art & Galleries' },
  { id: 'hiking',      label: 'Hiking' },
];

@Component({
  selector: 'app-my-interests',
  imports: [CommonModule],
  templateUrl: './my-interests.html',
  styleUrl: './my-interests.css',
})
export class MyInterests {
  isSaving = false;
  selectedInterests: Interest[] = [];
  private returnTo = '/settings';
  constructor(private router: Router, private route: ActivatedRoute) {
    this.route.queryParams.subscribe(p => {
      if (p['from']) this.returnTo = '/' + p['from'];
    });
    this.loadSavedInterests();
  }

  private loadSavedInterests(): void {
    const saved = localStorage.getItem('userInterests');
    if (saved) {
      const ids: string[] = JSON.parse(saved);
      this.selectedInterests = ALL_INTERESTS.filter(i => ids.includes(i.id));
    }
  }

  get availableInterests(): Interest[] {
    const selectedIds = this.selectedInterests.map(i => i.id);
    return ALL_INTERESTS.filter(i => !selectedIds.includes(i.id));
  }

  addInterest(interest: Interest): void {
    if (!this.isSelected(interest)) {
      this.selectedInterests = [...this.selectedInterests, interest];
    }
  }

  removeInterest(interest: Interest): void {
    this.selectedInterests = this.selectedInterests.filter(i => i.id !== interest.id);
  }

  isSelected(interest: Interest): boolean {
    return this.selectedInterests.some(i => i.id === interest.id);
  }

  saveChanges(): void {
    if (this.isSaving) return;
    this.isSaving = true;
    setTimeout(() => {
      localStorage.setItem('userInterests', JSON.stringify(this.selectedInterests.map(i => i.id)));
      this.isSaving = false;
      this.router.navigate([this.returnTo]);
    }, 800);
  }

  goBack(): void {
    this.router.navigate([this.returnTo]);
  }
}