import { Component, computed, inject, ViewChild } from '@angular/core';
import { Location } from '@angular/common';
import { Router } from '@angular/router';
import { CollectionsService } from '../../services/collections';
import { NewCollectionSheetComponent } from '../new-collection-sheet/new-collection-sheet';

@Component({
  selector: 'app-my-collections-page',
  standalone: true,
  imports: [NewCollectionSheetComponent],
  templateUrl: './my-collections-page.html',
  styleUrl: './my-collections-page.css',
})
export class MyCollectionsPageComponent {
  private readonly location    = inject(Location);
  private readonly router      = inject(Router);
  private readonly collService = inject(CollectionsService);

  @ViewChild('newSheet') newSheet!: NewCollectionSheetComponent;

  protected readonly collections = computed(() =>
    this.collService.collections().map((col) => ({
      ...col,
      coverImages: col.places.slice(0, 3).map((p) => p.image),
      placeCount: col.places.length,
    })),
  );

  protected onTileClick(id: string): void {
    this.router.navigate(['/profile/collections', id]);
  }

  protected goBack(): void {
    this.location.back();
  }

  protected onCollectionCreated(id: string): void {
    this.router.navigate(['/profile/collections', id]);
  }
}