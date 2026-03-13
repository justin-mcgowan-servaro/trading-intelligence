import { Injectable, signal, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { tap } from 'rxjs/operators';
import { environment } from '../../../environments/environment';

export interface AuthResponse {
  token: string;
  email: string;
  tier: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);
  private readonly TOKEN_KEY = 'ti_token';

  isAuthenticated = signal<boolean>(this.hasValidToken());
  currentUser = signal<{ email: string; tier: string } | null>(null);

  constructor() {
    this.loadUserFromToken();
  }

  requestOtp(email: string) {
    return this.http.post<{ message: string }>(
      `${environment.apiUrl}/api/auth/request-otp`,
      { email, website: '' }
    );
  }

  verifyOtp(email: string, code: string) {
    return this.http.post<AuthResponse>(
      `${environment.apiUrl}/api/auth/verify-otp`,
      { email, code }
    ).pipe(
      tap(response => this.handleAuthResponse(response))
    );
  }

  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    this.isAuthenticated.set(false);
    this.currentUser.set(null);
    this.router.navigate(['/login']);
  }

  getToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  private handleAuthResponse(response: AuthResponse): void {
    localStorage.setItem(this.TOKEN_KEY, response.token);
    this.isAuthenticated.set(true);
    this.currentUser.set({ email: response.email, tier: response.tier });
  }

  private hasValidToken(): boolean {
    const token = localStorage.getItem(this.TOKEN_KEY);
    if (!token) return false;
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return payload.exp * 1000 > Date.now();
    } catch { return false; }
  }

  private loadUserFromToken(): void {
    const token = this.getToken();
    if (!token || !this.hasValidToken()) return;
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      this.currentUser.set({
        email: payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'],
        tier: payload['tier']
      });
    } catch { }
  }
}
