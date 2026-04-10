import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

export interface FavoritePlace {
  name: string;
  country: string;
  rating: number;
}

@Component({
  selector: 'app-profile',
  imports: [CommonModule],
  templateUrl: './profile.html',
  styleUrl: './profile.css',
})
export class Profile {
  fullName = 'Marko Petrović';
  email = 'marko.p@example.com';

  favorites: FavoritePlace[] = [
    { name: 'Dubrovnik Old Town', country: 'Croatia', rating: 4.9 },
    { name: 'Lago di Braies',     country: 'Italy',   rating: 4.8 },
    { name: 'Santorini',          country: 'Greece',  rating: 4.7 },
  ];

  constructor(private router: Router) {
    const saved = localStorage.getItem('userProfile');
    if (saved) {
      const p = JSON.parse(saved);
      this.fullName = p.fullName ?? this.fullName;
      this.email    = p.email    ?? this.email;
    }
  }

  goToSettings():      void { this.router.navigate(['/settings']); }
  editProfile():       void { this.router.navigate(['/edit-profile']); }
  goToInterests():     void { this.router.navigate(['/my-interests'], { queryParams: { from: 'profile' } }); }
  goToContributions(): void { this.router.navigate(['/contributions']); }
  seeAllFavorites():   void { this.router.navigate(['/favorites']); }
}