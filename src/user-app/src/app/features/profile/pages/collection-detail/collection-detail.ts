import { Component, computed, inject } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { CollectionsService } from '../../services/collections';

@Component({
  selector: 'app-collection-detail',
  standalone: true,
  imports: [],
  templateUrl: './collection-detail.html',
})
export class CollectionDetailComponent {
  private readonly location    = inject(Location);
  private readonly router      = inject(Router);
  private readonly route       = inject(ActivatedRoute);
  private readonly collService = inject(CollectionsService);

  private readonly id = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly collection = computed(() =>
    this.collService.collections().find(c => c.id === this.id)
  );

  protected goBack(): void { this.location.back(); }

  protected onRemovePlace(placeId: string): void {
    this.collService.removePlace(this.id, placeId);
  }

  protected onAddPlace(): void {
    //ekran za dodavanje mesta
    this.router.navigate(['/profile/collections', this.id, 'add-place']);
  }

  protected onMenuOpen(): void {
    //za rename/delete kolekcije
  }
}