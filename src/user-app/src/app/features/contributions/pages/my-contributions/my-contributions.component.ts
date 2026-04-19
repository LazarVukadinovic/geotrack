import { Component, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { ContributionDraft } from '../submit-contribution/submit-contribution.component';
import { HttpClient } from '@angular/common/http';

export interface ContributionItem {
  id: string;
  name: string;
  location: string;
  imageUrl: string;
  status: 'APPROVED' | 'UNDER REVIEW' | 'REJECTED' | 'PENDING';
  updatedLabel: string;
  entityType: 'Stay' | 'Place' | 'Event' | 'Location';
}

@Component({
  selector: 'app-my-contributions',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './my-contributions.component.html',
})
export class MyContributionsPageComponent {
  constructor(private router: Router, private http: HttpClient) {}
  
  showEntityPicker = signal(false);

  items: ContributionItem[] = [
    { id: '1', name: 'Sunny Seaside Villa', location: 'Malibu, California', imageUrl: 'https://images.unsplash.com/photo-1582268611958-ebfd161ef9cf?w=600&q=80', status: 'APPROVED', updatedLabel: 'Updated 2 days ago', entityType: 'Stay' },
    { id: '2', name: 'Cozy Mountain Cabin', location: 'Aspen, Colorado', imageUrl: 'https://images.unsplash.com/photo-1510798831971-661eb04b3739?w=600&q=80', status: 'UNDER REVIEW', updatedLabel: 'Submitted 5 hours ago', entityType: 'Stay' },
    { id: '3', name: 'Urban Loft Downtown', location: 'New York City, NY', imageUrl: 'https://images.unsplash.com/photo-1502672260266-1c1ef2d93688?w=600&q=80', status: 'APPROVED', updatedLabel: 'Last edited 1 week ago', entityType: 'Stay' },
    { id: '4', name: 'Riverside Glamping Tent', location: 'Bend, Oregon', imageUrl: 'https://images.unsplash.com/photo-1504280390367-361c6d9f38f4?w=600&q=80', status: 'REJECTED', updatedLabel: 'Updated 3 weeks ago', entityType: 'Stay' },
  ];
  // API verzija:
  // items = signal<ContributionItem[]>([]);
  // ngOnInit(): void {
  //   this.http.get<ContributionItem[]>('/api/contributions').subscribe({
  //     next: (data) => { this.items.set(data); }
  //   });
  // }
  // openEntityPicker(): void { this.showEntityPicker.set(true); }

  startContribution(type: string): void {
    this.showEntityPicker.set(false);
    this.router.navigate(['/contributions/submit'], { state: { entityType: type } });
  }

  editItem(item: ContributionItem): void {
    // API verzija:
    // this.http.get<any>(`/api/contributions/${item.id}`).subscribe({
    //   next: (fullData) => {
    //     const editDraft: ContributionDraft = { 
    //       basics: { name: fullData.name, stayType: fullData.stayType, ... },
    //       features: { tags: fullData.tags ?? [], amenities: fullData.amenities ?? [] },
    //       details: { description: fullData.description ?? '', ... }
    //     };
    //     this.router.navigate(['/contributions/submit'], {
    //       state: { entityType: item.entityType, editId: item.id, editDraft }
    //     });
    //   },
    //   error: (err) => console.error(err)
    // });
    const editDraft: ContributionDraft = {
      basics: {
        name: item.name,
        stayType: 'Hotel',
        address: '',
        city: item.location.split(',')[0]?.trim() ?? '',
        region: '',
        country: item.location.split(',')[1]?.trim() ?? '',
        imageUrl: item.imageUrl,
        lat: null,
        lng: null,
        imgBgPosition: '50% 50%',
        imgBgSize: 'cover'
      },
      features: { tags: [], amenities: [] },
      details: { description: '', phone: '', website: '', bookingPartners: [] }
    };

    this.router.navigate(['/contributions/submit'], {
      state: { entityType: item.entityType, editId: item.id, editDraft }
    });
  }
  deleteItem(item: ContributionItem): void {
    this.items = this.items.filter(i => i.id !== item.id);
    // API verzija:
    // this.http.delete(`/api/contributions/${item.id}`).subscribe({
    //   next: () => {
    //     this.items = this.items.filter(i => i.id !== item.id);
    //   },
    //   error: () => {
    //   }
    // });
  }

  goBack(): void { this.router.navigate(['/profile']); }
}