import { Component, computed, inject, ViewChild } from '@angular/core';
import { Router } from '@angular/router';
import { CollectionsService } from '../../services/collections';
import { NewCollectionSheetComponent } from '../new-collection-sheet/new-collection-sheet.component';
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';

@Component({
  selector: 'app-my-collections-page',
  standalone: true,
  imports: [NewCollectionSheetComponent, ImageFallbackDirective],
  templateUrl: './my-collections-page.component.html',
  styleUrl: './my-collections-page.component.css',
})
export class MyCollectionsPageComponent {
  private readonly router      = inject(Router);
  private readonly collService = inject(CollectionsService);

  @ViewChild('newSheet') newSheet!: NewCollectionSheetComponent;

  protected readonly collections = computed(() =>
    this.collService.collections().map((col) => ({
      id: col.id,
      title: col.title,
      coverImages: col.coverImages,
      placeCount: col.itemsCount,
    })),
  );

  protected onTileClick(id: number): void {
    this.router.navigate(['/profile/collections', id]);
  }

  protected goBack(): void {
    this.router.navigate(['/profile']);
  }

  protected onCollectionCreated(id: number): void {
    this.router.navigate(['/profile/collections', id]);
  }
}
