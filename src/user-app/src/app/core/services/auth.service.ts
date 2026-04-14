import { Injectable, computed, inject } from '@angular/core';
import { TokenStorageService } from './token-storage.service';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly tokenStorage = inject(TokenStorageService);

  readonly isLoggedIn = computed(() => this.tokenStorage.hasToken());

  login(token: string, rememberMe = true): void {
    this.tokenStorage.setToken(token, rememberMe);
  }

  logout(): void {
    this.tokenStorage.clearToken();
  }
}
