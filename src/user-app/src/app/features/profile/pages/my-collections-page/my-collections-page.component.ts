import { Component, computed, inject, ViewChild } from '@angular/core';
import { Location } from '@angular/common';
import { Router } from '@angular/router';
import { CollectionsService } from '../../services/collections';
import { NewCollectionSheetComponent } from '../new-collection-sheet/new-collection-sheet.component';

@Component({
  selector: 'app-my-collections-page',
  standalone: true,
  imports: [NewCollectionSheetComponent],
  templateUrl: './my-collections-page.component.html',
  styleUrl: './my-collections-page.component.css',
})
export class MyCollectionsPageComponent {
  private readonly location    = inject(Location);
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
    this.location.back();
  }

  protected onCollectionCreated(id: number): void {
    this.router.navigate(['/profile/collections', id]);
  }
}