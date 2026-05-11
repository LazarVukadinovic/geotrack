import { Injectable, signal, computed } from '@angular/core';
import { Observable, of, throwError } from 'rxjs';
import { delay, tap } from 'rxjs/operators';
import { User, UserRole } from '../models/user.model';

// MOCK USERS 
// replace login() with HttpClient call.
const MOCK_USERS: Record<string, User> = {
  'national@geotrack.com': {
    id: '1',
    firstName: 'Sarah',
    lastName: 'Jenkins',
    email: 'national@geotrack.com',
    role: 'National Manager',
    region: 'Republika Srbija',
    memberSince: 'March 5, 2022',
    bio: 'National platform manager overseeing all regional organizations across Serbia.',
    language: 'English (US)',
    timezone: 'Central European Time (CET)',
    notifyRequests: true,
    notifyNewReview: true,
  },
  'regional@geotrack.com': {
    id: '2',
    firstName: 'Alex',
    lastName: 'Popović',
    email: 'regional@geotrack.com',
    role: 'Regional Manager',
    region: 'Šumadija, Srbija',
    memberSince: 'October 12, 2023',
    bio: 'Passionate local explorer and photographer based in Kragujevac.',
    language: 'English (US)',
    timezone: 'Central European Time (CET)',
    notifyRequests: true,
    notifyNewReview: false,
  },
};

@Injectable({ providedIn: 'root' })
export class AuthService {

  private readonly _currentUser = signal<User | null>(null);

  readonly currentUser = computed(() => this._currentUser());
  readonly isLoggedIn  = computed(() => this._currentUser() !== null);
  readonly isNationalManager = computed(() => this._currentUser()?.role === 'National Manager');
  readonly isRegionalManager = computed(() => this._currentUser()?.role === 'Regional Manager');

  getUser(): User | null { return this._currentUser(); }
  get fullName(): string {
    const u = this._currentUser();
    return u ? `${u.firstName} ${u.lastName}` : '';
  }

  // MOCK: 
  // replace with:
  // return this.http
  // .post<User>('/api/auth/login', { email, password })
  // .pipe(tap(user => this._currentUser.set(user)));
   
  login(email: string, password: string): Observable<User> {
    const found = MOCK_USERS[email.toLowerCase().trim()];
    if (found && password.length >= 6) {
      return of(found).pipe(
        delay(1200),
        tap(user => this._currentUser.set(user)),
      );
    }
    return throwError(() => new Error('Invalid email or password.'));
  }

  // UPDATE USER
  // replace with:
  // return this.http.put<User>('/api/user/me', data).pipe(tap(...))
  updateUser(data: Partial<User>): Observable<User> {
    const current = this._currentUser();
    if (!current) return throwError(() => new Error('Not authenticated'));
    const updated: User = { ...current, ...data };
    return of(updated).pipe(
      delay(1200),
      tap(u => this._currentUser.set(u)),
    );
  }

  // LOGOUT
  // add:
  // this.http.post('/api/auth/logout', {}).subscribe();

  logout(): void {
    this._currentUser.set(null);
  }

  getHomeRoute(): string {
    const role: UserRole | undefined = this._currentUser()?.role;
    switch (role) {
      case 'National Manager': return '/national-dashboard';
      case 'Regional Manager': return '/dashboard';
      default:                 return '/login';
    }
  }
}