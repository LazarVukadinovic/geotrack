import { Component, computed, inject } from '@angular/core';
import { Location } from '@angular/common';
import { Router } from '@angular/router';
import { CollectionsService } from '../../services/collections';

@Component({
  selector: 'app-my-collections-page',
  standalone: true,
  imports: [],
  templateUrl: './my-collections-page.html',
  styleUrl: './my-collections-page.css',
})
export class MyCollectionsPageComponent {
  private readonly location    = inject(Location);
  private readonly router      = inject(Router);
  private readonly collService = inject(CollectionsService);

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

  protected onNewCollection(): void {
    this.router.navigate(['/profile/collections/new']);
  }

  protected goBack(): void {
    this.location.back();
  }
}