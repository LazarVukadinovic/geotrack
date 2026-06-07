import { ChangeDetectionStrategy, Component, HostBinding, computed, input, signal } from '@angular/core';
import { buildAvatarDisplayUrl } from '../../../core/utils/avatar-url';

@Component({
  selector: 'app-user-avatar',
  standalone: true,
  templateUrl: './user-avatar.component.html',
  styleUrl: './user-avatar.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserAvatarComponent {
  readonly imageUrl = input<string | null | undefined>(null);
  readonly avatarVersion = input<number | string | null | undefined>(null);
  readonly displayName = input<string | null | undefined>(null);
  readonly firstName = input<string | null | undefined>(null);
  readonly lastName = input<string | null | undefined>(null);
  readonly email = input<string | null | undefined>(null);
  readonly size = input<number | string | null | undefined>(null);
  readonly alt = input<string | null | undefined>(undefined);

  private readonly failedImageUrl = signal('');

  protected readonly resolvedImageUrl = computed(() =>
    buildAvatarDisplayUrl(this.imageUrl(), this.avatarVersion()),
  );

  protected readonly displayImageUrl = computed(() => {
    const url = this.resolvedImageUrl();
    return url && url !== this.failedImageUrl() ? url : '';
  });

  protected readonly accessibleName = computed(() =>
    this.clean(this.displayName()) ||
    [this.clean(this.firstName()), this.clean(this.lastName())].filter(Boolean).join(' ') ||
    this.clean(this.email()) ||
    'User',
  );

  protected readonly initials = computed(() => {
    const nameParts = [this.clean(this.firstName()), this.clean(this.lastName())].filter(Boolean);
    const sourceParts = nameParts.length
      ? nameParts
      : this.nameParts(this.clean(this.displayName()));

    const initials = sourceParts
      .slice(0, 2)
      .map(part => part.charAt(0).toUpperCase())
      .join('');

    if (initials) {
      return initials;
    }

    const email = this.clean(this.email());
    return email ? email.charAt(0).toUpperCase() : '';
  });

  protected readonly imageAlt = computed(() => {
    const explicitAlt = this.alt();
    if (explicitAlt !== undefined && explicitAlt !== null) {
      return explicitAlt;
    }

    return `Profile photo of ${this.accessibleName()}`;
  });

  protected readonly fallbackAriaLabel = computed(() => this.accessibleName());

  @HostBinding('class.user-avatar--fixed')
  protected get hasFixedSize(): boolean {
    return this.hostSize !== null;
  }

  @HostBinding('style.--user-avatar-size')
  protected get hostSize(): string | null {
    const value = this.size();
    if (value === null || value === undefined || value === '') {
      return null;
    }

    if (typeof value === 'number') {
      return Number.isFinite(value) && value > 0 ? `${value}px` : null;
    }

    const trimmed = value.trim();
    if (!trimmed) {
      return null;
    }

    return /^\d+(\.\d+)?$/.test(trimmed) ? `${trimmed}px` : trimmed;
  }

  protected onImageError(): void {
    const url = this.displayImageUrl();
    if (url) {
      this.failedImageUrl.set(url);
    }
  }

  private clean(value: string | null | undefined): string {
    return value?.trim() ?? '';
  }

  private nameParts(value: string): string[] {
    return value.split(/\s+/).map(part => part.trim()).filter(Boolean);
  }
}
