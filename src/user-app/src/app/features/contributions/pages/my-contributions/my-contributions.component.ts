import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { ContributionListItem, ContributionsService } from '../../services/contributions.service';

@Component({
  selector: 'app-my-contributions',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './my-contributions.component.html',
  styleUrl: './my-contributions.component.css',
})
export class MyContributionsPageComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly contributions = inject(ContributionsService);

  protected readonly items = this.contributions.items;
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
}
