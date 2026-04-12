import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

export interface FavoritePlace {
  name: string;
  country: string;
  rating: number;
  image: string;
}
export interface Collection {
  id: string;
  name: string;
  places: FavoritePlace[];
}
  const u = (id: string) =>
  `https://images.unsplash.com/${id}?ixlib=rb-4.0.3&auto=format&fit=crop&w=400&q=75`;
@Component({
  selector: 'app-profile',
  imports: [CommonModule],
  templateUrl: './profile.html',
  styleUrl: './profile.css',
})
export class Profile implements OnInit {
  fullName = 'Marko Petrović';
  email = 'marko.p@example.com';

  collections: Collection[] = [
    {
      id: '1',
      name: 'Dream Destinations',
      places: [
        { name: 'Dubrovnik Old Town', country: 'Croatia', rating: 4.9, image: u('photo-1499856871958-5b9627545d1a') },
        { name: 'Santorini',          country: 'Greece',  rating: 4.7, image: u('photo-1507525428034-b723cf961d3e') },
      ]
    },
    {
      id: '2',
      name: 'Nature & Hiking',
      places: [
        { name: 'Lago di Braies', country: 'Italy', rating: 4.8, image: u('photo-1467269204594-9661b134dd2b') },
      ]
    },
  ];

  constructor(private router: Router) {}

    ngOnInit(): void {
    const saved = localStorage.getItem('userProfile');
    if (saved) {
      const p = JSON.parse(saved);
      this.fullName = p.fullName ?? this.fullName;
      this.email    = p.email    ?? this.email;
    }
    const savedCollections = localStorage.getItem('userCollections');
    if (savedCollections) {
      this.collections = JSON.parse(savedCollections);
    }
  }

  goToSettings():      void { this.router.navigate(['/settings']); }
  editProfile():       void { this.router.navigate(['/edit-profile']); }
  goToInterests():     void { this.router.navigate(['/my-interests'], { queryParams: { from: 'profile' } }); }
  goToContributions(): void { this.router.navigate(['/contributions']); }
  goToCollection(id: string): void { this.router.navigate(['/collection', id]); }
  seeAllCollections(): void { this.router.navigate(['/profile/collections']); }
}