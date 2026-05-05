import { Injectable, signal } from '@angular/core';
import { Observable, of, throwError } from 'rxjs';
import { delay } from 'rxjs/operators';
import { User } from '../models/user.model';

@Injectable({ providedIn: 'root' })
export class AuthService {

  // --- MOCK: zameniti sa HttpClient pozivom pri integraciji ---
  private readonly _mockUser: User = {
    id: '1',
    firstName: 'Sarah',
    lastName: 'Jenkins',
    email: 'sarah.jenkins@voyagesphere.com',
    role: 'Regional Manager',
    region: 'Šumadija, Srbija',
    memberSince: 'October 12, 2023',
    bio: 'Passionate local explorer and photographer based in Kragujevac...',
    language: 'English (US)',
    timezone: 'Central European Time (CET)',
    notifyRequests: true,
    notifyNewReview: false,
  };

  private currentUser = signal<User | null>(this._mockUser);

  getUser(): User { return this.currentUser()!; }
  get fullName(): string { const u = this.getUser(); return `${u.firstName} ${u.lastName}`; }

  login(email: string, password: string): Observable<User> {
    // MOCK: zameniti sa: return this.http.post<User>('/api/auth/login', { email, password });
    if (email && password) {
      this.currentUser.set(this._mockUser);
      return of(this._mockUser).pipe(delay(1200));
    }
    return throwError(() => new Error('Invalid credentials'));
  }

  updateUser(data: Partial<User>): Observable<User> {
    // MOCK: zameniti sa: return this.http.put<User>('/api/user/me', data);
    const updated = { ...this.getUser(), ...data };
    this.currentUser.set(updated);
    return of(updated).pipe(delay(1200));
  }

  logout(): void {
    // MOCK: zameniti sa HTTP pozivom
    this.currentUser.set(null);
  }
}