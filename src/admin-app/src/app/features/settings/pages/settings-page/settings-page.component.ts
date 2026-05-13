import { Component, signal, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './settings-page.component.html',
  styleUrl: './settings-page.component.css'
})
export class SettingsComponent {
  private readonly auth   = inject(AuthService);
  private readonly router = inject(Router);

  private readonly user = this.auth.getUser()!;

  readonly isNational = this.auth.isNationalManager;

  // ── User data for topbar / sidebar ────────────────────────────
  get region()   { return this.user.region; }
  get userName() { return `${this.user.firstName} ${this.user.lastName}`; }
  get userRole() { return this.user.role; }

  // ── Public Profile ─────────────────────────────────────────────
  readonly firstName    = signal(this.user.firstName);
  readonly lastName     = signal(this.user.lastName);
  readonly email        = signal(this.user.email);
  readonly bio          = signal(this.user.bio);
  readonly bioMaxLength = 300;
  readonly bioLength    = computed(() => this.bio().length);

  // ── Role info (read-only) ──────────────────────────────────────
  readonly currentRole    = this.user.role;
  readonly assignedRegion = this.user.region;
  readonly memberSince    = this.user.memberSince;

  // ── Preferences ───────────────────────────────────────────────
  readonly language        = signal(this.user.language);
  readonly timezone        = signal(this.user.timezone);
  readonly notifyRequests  = signal(this.user.notifyRequests);
  readonly notifyNewReview = signal(this.user.notifyNewReview);

  // ── UI state ───────────────────────────────────────────────────
  readonly isSaving      = signal(false);
  readonly saveSuccess   = signal(false);
  readonly avatarPreview = signal<string | null>(this.user.avatarUrl ?? null);

  // ── Handlers ───────────────────────────────────────────────────

  onBioChange(value: string): void {
    if (value.length <= this.bioMaxLength) this.bio.set(value);
  }

  onAvatarChange(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => this.avatarPreview.set(reader.result as string);
    reader.readAsDataURL(file);
  }

  removeAvatar(): void { this.avatarPreview.set(null); }

  onSave(): void {
    if (this.isSaving()) return;
    this.saveSuccess.set(false);
    this.isSaving.set(true);

    this.auth.updateUser({
      firstName:      this.firstName(),
      lastName:       this.lastName(),
      bio:            this.bio(),
      language:       this.language(),
      timezone:       this.timezone(),
      notifyRequests: this.notifyRequests(),
      notifyNewReview: this.notifyNewReview(),
      avatarUrl:      this.avatarPreview() ?? undefined,
    }).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.saveSuccess.set(true);
        setTimeout(() => this.saveSuccess.set(false), 3000);
      },
      error: () => this.isSaving.set(false),
    });
  }

  onCancel(): void {
    this.router.navigate([this.auth.getHomeRoute()]);
  }
}