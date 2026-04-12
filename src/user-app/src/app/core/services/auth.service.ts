import { Injectable, computed, inject } from '@angular/core';
import { TokenStorageService } from './token-storage.service';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly tokenStorage = inject(TokenStorageService);

  readonly isLoggedIn = computed(() => this.tokenStorage.hasToken());

  login(token: string): void {
    this.tokenStorage.setToken(token);
  }

  logout(): void {
    this.tokenStorage.clearToken();
  }
}
