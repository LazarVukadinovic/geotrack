import { Component, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './settings-page.component.html',
  styleUrl: './settings-page.component.css'
})
export class SettingsComponent {
  // ── User data ──────────────────────────────────────────────────
  region   = 'Šumadija, Srbija';
  userName = 'Sarah Jenkins';
  userRole = 'Regional Manager';

  // ── Public Profile ─────────────────────────────────────────────
  readonly firstName    = signal('Sarah');
  readonly lastName     = signal('Jenkins');
  readonly email        = signal('sarah.jenkins@voyagesphere.com');
  readonly bio          = signal(
    'Passionate local explorer and photographer based in Kragujevac. I love uncovering hidden gems and sharing the rich history of central Serbia with the world.'
  );
  readonly bioMaxLength = 300;
  readonly bioLength    = computed(() => this.bio().length);

  // ── Role info (read-only) ──────────────────────────────────────
  readonly currentRole     = 'Regional Manager';
  readonly assignedRegion  = 'Šumadija, Srbija';
  readonly memberSince     = 'October 12, 2023';

  // ── Preferences ───────────────────────────────────────────────
  readonly language                = signal('English (US)');
  readonly timezone                = signal('Central European Time (CET)');
  readonly notifyRequests          = signal(true);
  readonly notifyPublished         = signal(false);
  readonly notifyWeeklyDigest      = signal(true);

  // ── UI state ───────────────────────────────────────────────────
  readonly isSaving       = signal(false);
  readonly saveSuccess    = signal(false);
  readonly avatarPreview  = signal<string | null>(null);

  // ── Nav ────────────────────────────────────────────────────────
  navItems = [
    { label: 'Dashboard',      icon: 'grid',     route: '/dashboard',  badge: null },
    { label: 'Requests Queue', icon: 'inbox',    route: '/requests',   badge: 5    },
    { label: 'Map',            icon: 'map',      route: '/map',        badge: null },
  ];
  contentItems = [
    { label: 'Destinations', icon: 'flag',     route: '/destinations' },
    { label: 'Locations',    icon: 'pin',      route: '/locations'    },
    { label: 'Places',       icon: 'home',     route: '/places'       },
    { label: 'Events',       icon: 'calendar', route: '/events'       },
  ];

  constructor(private readonly router: Router) {}

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

    // Replace with real API call, e.g.:
    // this.profileService.save({ ... }).subscribe({ next: ..., error: ... });
    setTimeout(() => {
      this.isSaving.set(false);
      this.saveSuccess.set(true);
      setTimeout(() => this.saveSuccess.set(false), 3000);
    }, 1200);
  }

  onCancel(): void {
    this.router.navigate(['/dashboard']);
  }

  navigateTo(route: string): void {
    this.router.navigate([route]);
  }
}