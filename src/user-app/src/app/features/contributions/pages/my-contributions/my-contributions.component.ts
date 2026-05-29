import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { ContributionListItem, ContributionsService } from '../../services/contributions.service';
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';

@Component({
  selector: 'app-my-contributions',
  standalone: true,
  imports: [CommonModule, ImageFallbackDirective],
  templateUrl: './my-contributions.component.html',
  styleUrl: './my-contributions.component.css',
})
export class MyContributionsPageComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly contributions = inject(ContributionsService);

  private readonly PAGE_INITIAL = 2;
  private readonly PAGE_STEP = 5;

  protected readonly items = this.contributions.items;
  protected readonly contributionItems = computed(() => this.items().filter(item => !item.isOwnership));
  protected readonly ownershipItems = computed(() => this.items().filter(item => item.isOwnership));
  protected readonly ownershipsVisible = signal(this.PAGE_INITIAL);
  protected readonly contributionsVisible = signal(this.PAGE_INITIAL);
  protected readonly sections = computed(() => [
    {
      title: 'Ownerships',
      items: this.ownershipItems().slice(0, this.ownershipsVisible()),
      total: this.ownershipItems().length,
      visible: this.ownershipsVisible(),
      key: 'ownerships' as const,
    },
    {
      title: 'Contributions',
      items: this.contributionItems().slice(0, this.contributionsVisible()),
      total: this.contributionItems().length,
      visible: this.contributionsVisible(),
      key: 'contributions' as const,
    },
  ].filter(section => section.total > 0));
  protected readonly isLoading = this.contributions.isLoading;
  protected readonly loadError = this.contributions.loadError;
  protected readonly pendingDelete = signal<ContributionListItem | null>(null);
  protected readonly deleteError = signal<string | null>(null);
  protected readonly isDeleting = signal(false);

  ngOnInit(): void {
    this.contributions.loadMine();
  }

  protected startContribution(): void {
    this.router.navigate(['/contributions/submit']);
  }

  protected editItem(event: Event, item: ContributionListItem): void {
    event.stopPropagation();
    this.router.navigate(['/contributions', item.id, 'edit']);
  }

  protected requestDelete(event: Event, item: ContributionListItem): void {
    event.stopPropagation();
    this.deleteError.set(null);
    this.pendingDelete.set(item);
  }

  protected cancelDelete(): void {
    if (this.isDeleting()) return;
    this.pendingDelete.set(null);
    this.deleteError.set(null);
  }

  protected confirmDelete(): void {
    const item = this.pendingDelete();
    if (!item || this.isDeleting()) return;

    this.isDeleting.set(true);
    this.deleteError.set(null);

    this.contributions.deleteContribution(item.id).subscribe({
      next: () => {
        this.pendingDelete.set(null);
        this.isDeleting.set(false);
      },
      error: () => {
        this.deleteError.set('Could not delete this contribution. Please try again.');
        this.isDeleting.set(false);
      },
    });
  }

  protected goBack(): void {
    this.router.navigate(['/profile']);
  }

  protected openDetail(item: ContributionListItem): void {
    this.router.navigate(['/contributions', item.id]);
  }

  protected reload(): void {
    this.contributions.loadMine();
  }

  protected loadMore(key: 'ownerships' | 'contributions'): void {
    const sig = key === 'ownerships' ? this.ownershipsVisible : this.contributionsVisible;
    const total = key === 'ownerships' ? this.ownershipItems().length : this.contributionItems().length;
    sig.set(Math.min(sig() + this.PAGE_STEP, total));
  }

  protected showLess(key: 'ownerships' | 'contributions'): void {
    const sig = key === 'ownerships' ? this.ownershipsVisible : this.contributionsVisible;
    sig.set(Math.max(sig() - this.PAGE_STEP, this.PAGE_INITIAL));
  }
}
