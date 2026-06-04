import { Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

export interface AdminSavedFormDraft<TPayload = unknown> {
  formType: string;
  route: string;
  userId: string;
  savedAt: string;
  payload: TPayload;
}

interface DraftSource<TPayload = unknown> {
  formType: string;
  snapshot: () => TPayload | null | undefined;
}

@Injectable({ providedIn: 'root' })
export class AdminFormDraftService {
  private static readonly keyPrefix = 'geotrack_admin_form_draft';
  private activeSource: DraftSource | null = null;

  constructor(
    private readonly router: Router,
    private readonly auth: AuthService,
  ) {}

  register<TPayload>(source: DraftSource<TPayload>): () => void {
    this.activeSource = source as DraftSource;

    return () => {
      if (this.activeSource === source) {
        this.activeSource = null;
      }
    };
  }

  saveActiveDraftOnSessionExpiry(): boolean {
    const source = this.activeSource;
    if (!source) return false;

    const payload = source.snapshot();
    if (!payload) return false;

    const draft: AdminSavedFormDraft = {
      formType: source.formType,
      route: this.currentRoute(),
      userId: this.currentUserId(),
      savedAt: new Date().toISOString(),
      payload,
    };

    try {
      sessionStorage.setItem(this.keyFor(source.formType), JSON.stringify(draft));
      return true;
    } catch {
      return false;
    }
  }

  loadDraft<TPayload>(formType: string): AdminSavedFormDraft<TPayload> | null {
    try {
      const raw = sessionStorage.getItem(this.keyFor(formType));
      if (!raw) return null;

      const draft = JSON.parse(raw) as AdminSavedFormDraft<TPayload>;
      if (
        draft.formType !== formType ||
        draft.route !== this.currentRoute() ||
        draft.userId !== this.currentUserId()
      ) {
        return null;
      }

      return draft;
    } catch {
      return null;
    }
  }

  clearDraft(formType: string): void {
    try {
      sessionStorage.removeItem(this.keyFor(formType));
    } catch {
      // Storage may be unavailable.
    }
  }

  private keyFor(formType: string): string {
    return [
      AdminFormDraftService.keyPrefix,
      this.currentUserId(),
      this.currentRoute(),
      formType,
    ].join(':');
  }

  private currentRoute(): string {
    const path = this.router.url.split('?')[0].split('#')[0];
    return path || '/';
  }

  private currentUserId(): string {
    return this.auth.getUser()?.id ?? 'anonymous';
  }
}
