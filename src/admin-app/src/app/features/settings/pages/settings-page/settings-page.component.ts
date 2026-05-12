import { Component, signal, computed } from '@angular/core';
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
  // ── User data ──────────────────────────────────────────────────
  region   = 'Šumadija, Srbija';
  userName = '';
  userRole = '';

  // ── Public Profile ─────────────────────────────────────────────
  readonly firstName    = signal('');
  readonly lastName     = signal('');
  readonly email        = signal('');
  readonly bio          = signal('');
  readonly bioMaxLength = 300;
  readonly bioLength    = computed(() => this.bio().length);

  // ── Role info (read-only) ──────────────────────────────────────
  get currentRole(): string {
    return this.auth.getUser()?.role ?? '';
  }

  get assignedRegion(): string {
    return this.auth.getUser()?.region || this.region;
  }

  get memberSince(): string {
    return this.auth.getUser()?.memberSince ?? '';
  }

  // ── Preferences ───────────────────────────────────────────────
  readonly language           = signal('English (US)');
  readonly timezone           = signal('Central European Time (CET)');
  readonly notifyRequests     = signal(true);
  readonly notifyNewReview    = signal(false);

  // ── UI state ───────────────────────────────────────────────────
  readonly isSaving       = signal(false);
  readonly saveSuccess    = signal(false);
  readonly avatarPreview  = signal<string | null>(null);

  constructor(private readonly router: Router, private readonly auth: AuthService) {
    const user = this.auth.getUser();
    if (user) {
      this.firstName.set(user.firstName);
      this.lastName.set(user.lastName);
      this.email.set(user.email);
      this.language.set(user.language);
      this.timezone.set(user.timezone);
      this.notifyRequests.set(user.notifyRequests);
      this.notifyNewReview.set(user.notifyNewReview);
    }
  }

  // ── Handlers ───────────────────────────────────────────────────

  onBioChange(value: string): void {
    if (value.length <= this.bioMaxLength) {
      this.bio.set(value);
    }
  }

  onAvatarChange(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => this.avatarPreview.set(reader.result as string);
    reader.readAsDataURL(file);
  }

  removeAvatar(): void {
    this.avatarPreview.set(null);
  }

  onSave(): void {
    if (this.isSaving()) return;
    this.saveSuccess.set(false);
    this.isSaving.set(true);

    setTimeout(() => {
      this.isSaving.set(false);
      this.saveSuccess.set(true);
      setTimeout(() => this.saveSuccess.set(false), 3000);
    }, 1200);
  }

  onCancel(): void {
    this.router.navigate(['/dashboard']);
  }
}
