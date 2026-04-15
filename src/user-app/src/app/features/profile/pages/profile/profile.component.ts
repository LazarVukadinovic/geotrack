import { Component, OnInit, ViewChild, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { CollectionsService } from '../../services/collections';
import { NewCollectionSheetComponent } from '../new-collection-sheet/new-collection-sheet.component';

interface MeResponse {
  id: number;
  username: string;
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber: string;
  avatarUrl?: string;
  location?: string;
}

@Component({
  selector: 'app-profile',
  imports: [CommonModule, NewCollectionSheetComponent],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.css',
})
export class ProfileComponent implements OnInit {
  private readonly collService = inject(CollectionsService);
  private readonly router      = inject(Router);
  private readonly http        = inject(HttpClient);

  @ViewChild('newSheet') newSheet!: NewCollectionSheetComponent;

  readonly fullName = signal('');
  readonly email    = signal('');
  readonly loadError = signal('');

  get collections() {
    return this.collService.collections();
  }

  ngOnInit(): void {
    this.http.get<MeResponse>('/api/user/me').subscribe({
      next: (res) => {
        const name = `${res.firstName} ${res.lastName}`.trim();
        this.fullName.set(name || res.email);
        this.email.set(res.email);
      },
      error: () => {
        const saved = localStorage.getItem('userProfile');
        if (saved) {
          const p = JSON.parse(saved);
          this.fullName.set(p.fullName ?? '');
          this.email.set(p.email ?? '');
        } else {
          this.loadError.set('Could not load profile data.');
        }
      },
    });
  }

  onCollectionCreated(id: string): void {
    this.router.navigate(['/profile/collections', id]);
  }

  goToSettings():      void { this.router.navigate(['/settings']); }
  editProfile():       void { this.router.navigate(['/profile/edit']); }
  goToInterests():     void { this.router.navigate(['/settings/interests'], { queryParams: { from: 'profile' } }); }
  goToContributions(): void { this.router.navigate(['/contributions']); }
  goToCollection(id: string): void { this.router.navigate(['/profile/collections', id]); }
  seeAllCollections(): void { this.router.navigate(['/profile/collections']); }
}