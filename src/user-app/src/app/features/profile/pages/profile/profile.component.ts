import { Component, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CollectionsService } from '../../services/collections';
import { NewCollectionSheetComponent } from '../new-collection-sheet/new-collection-sheet.component';
import { UserService } from '../../../../core/services/user.service';
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';

@Component({
  selector: 'app-profile',
  imports: [CommonModule, NewCollectionSheetComponent, ImageFallbackDirective],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.css',
})
export class ProfileComponent {
  private readonly collService = inject(CollectionsService);
  private readonly router      = inject(Router);

  @ViewChild('newSheet') newSheet!: NewCollectionSheetComponent;

  // UserService is pre-populated by provideAppInitializer before any route
  // renders, so fullName() and email() are already set on first paint.
  protected readonly fullName = inject(UserService).fullName;
  protected readonly email    = inject(UserService).email;
  protected readonly avatarUrl = inject(UserService).avatarUrl;

  get collections() {
    return this.collService.collections();
  }

  onCollectionCreated(id: number): void {
    this.router.navigate(['/profile/collections', id]);
  }

  goToSettings():      void { this.router.navigate(['/settings']); }
  editProfile():       void { this.router.navigate(['/profile/edit']); }
  goToInterests():     void { this.router.navigate(['/settings/interests'], { queryParams: { from: 'profile' } }); }
  goToContributions(): void { this.router.navigate(['/contributions']); }
  goToCollection(id: number): void { this.router.navigate(['/profile/collections', id]); }
  seeAllCollections(): void { this.router.navigate(['/profile/collections']); }
}
