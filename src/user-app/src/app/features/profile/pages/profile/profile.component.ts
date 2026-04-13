import { Component, OnInit, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CollectionsService } from '../../services/collections';
import { NewCollectionSheetComponent } from '../new-collection-sheet/new-collection-sheet.component';

@Component({
  selector: 'app-profile',
  imports: [CommonModule, NewCollectionSheetComponent],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.css',
})
export class ProfileComponent implements OnInit {
  private readonly collService = inject(CollectionsService);
  private readonly router      = inject(Router);

  @ViewChild('newSheet') newSheet!: NewCollectionSheetComponent;

  fullName = 'Marko Petrović';
  email    = 'marko.p@example.com';

  get collections() {
    return this.collService.collections();
  }

  ngOnInit(): void {
    const saved = localStorage.getItem('userProfile');
    if (saved) {
      const p = JSON.parse(saved);
      this.fullName = p.fullName ?? this.fullName;
      this.email    = p.email    ?? this.email;
    }
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