import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/services/auth.service';

type Step = 'email' | 'otp' | 'success';

@Component({
  selector: 'app-auth',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="auth-backdrop">
      <div class="auth-card">
        <div class="auth-logo">⚡ Servaro</div>
        <div class="auth-sub">Trading Intelligence Platform</div>

        <!-- Email Step -->
        @if (step() === 'email') {
          <div class="auth-body">
            <p class="auth-desc">Enter your email to receive a login code.</p>
            <input class="auth-input" type="email" placeholder="you@example.com"
                   [(ngModel)]="email" (keydown.enter)="requestOtp()"
                   [disabled]="loading()" autocomplete="email"/>
            <!-- Honeypot — hidden from real users -->
            <input style="display:none" type="text" [(ngModel)]="honeypot" tabindex="-1" autocomplete="off"/>
            @if (errorMsg()) {
              <div class="auth-error">{{ errorMsg() }}</div>
            }
            <button class="auth-btn" (click)="requestOtp()" [disabled]="loading()">
              {{ loading() ? 'Sending...' : 'Send Login Code' }}
            </button>
          </div>
        }

        <!-- OTP Step -->
        @if (step() === 'otp') {
          <div class="auth-body">
            <p class="auth-desc">
              A 6-digit code was sent to <strong>{{ email }}</strong>.
              It expires in 10 minutes.
            </p>
            <input class="auth-input otp-input" type="text" placeholder="000000"
                   [(ngModel)]="otp" (keydown.enter)="verifyOtp()"
                   [disabled]="loading()" maxlength="6"
                   autocomplete="one-time-code" inputmode="numeric"/>
            @if (errorMsg()) {
              <div class="auth-error">{{ errorMsg() }}</div>
            }
            <button class="auth-btn" (click)="verifyOtp()" [disabled]="loading()">
              {{ loading() ? 'Verifying...' : 'Verify Code' }}
            </button>
            <button class="auth-link" (click)="step.set('email')" [disabled]="loading()">
              ← Use a different email
            </button>
          </div>
        }

        <!-- Success Step -->
        @if (step() === 'success') {
          <div class="auth-body auth-success">
            <div class="success-icon">✓</div>
            <p>Signed in successfully.</p>
          </div>
        }
      </div>
    </div>
  `,
  styles: [`
    .auth-backdrop { position: fixed; inset: 0; background: #0d1117; display: flex; align-items: center; justify-content: center; z-index: 1000; }
    .auth-card { background: #161b22; border: 1px solid #30363d; border-radius: 12px; padding: 40px; width: 100%; max-width: 400px; box-shadow: 0 24px 64px rgba(0,0,0,0.6); }
    .auth-logo { font-size: 24px; font-weight: 800; color: #00c2ff; margin-bottom: 4px; }
    .auth-sub { font-size: 13px; color: #8b949e; margin-bottom: 32px; }
    .auth-desc { font-size: 14px; color: #8b949e; margin-bottom: 20px; line-height: 1.5; }
    .auth-body { display: flex; flex-direction: column; gap: 12px; }
    .auth-input { background: #0d1117; border: 1px solid #30363d; border-radius: 8px; padding: 12px 16px; font-size: 15px; color: #e6edf3; outline: none; transition: border-color 0.15s; width: 100%; box-sizing: border-box; }
    .auth-input:focus { border-color: #58a6ff; }
    .auth-input:disabled { opacity: 0.5; }
    .otp-input { font-size: 24px; letter-spacing: 8px; text-align: center; font-family: monospace; }
    .auth-btn { background: #1a3a5c; color: #58a6ff; border: 1px solid #58a6ff; border-radius: 8px; padding: 12px; font-size: 15px; font-weight: 600; cursor: pointer; transition: background 0.15s; }
    .auth-btn:hover:not(:disabled) { background: #1f4a7a; }
    .auth-btn:disabled { opacity: 0.5; cursor: not-allowed; }
    .auth-link { background: none; border: none; color: #8b949e; font-size: 13px; cursor: pointer; padding: 4px 0; text-align: left; }
    .auth-link:hover { color: #e6edf3; }
    .auth-error { background: #3d0f0e; border: 1px solid #f85149; border-radius: 6px; padding: 10px 14px; font-size: 13px; color: #f85149; }
    .auth-success { align-items: center; padding: 20px 0; }
    .success-icon { width: 56px; height: 56px; background: #1a4731; border-radius: 50%; display: flex; align-items: center; justify-content: center; font-size: 24px; color: #3fb950; margin-bottom: 12px; }
  `]
})
export class AuthComponent {
  private authService: AuthService;

  step = signal<Step>('email');
  loading = signal(false);
  errorMsg = signal<string | null>(null);
  email = '';
  otp = '';
  honeypot = ''; // Never shown to user — if filled, bot detected server-side

  constructor(auth: AuthService) {
    this.authService = auth;
  }

  requestOtp(): void {
    if (!this.email.includes('@') || this.loading()) return;
    this.loading.set(true);
    this.errorMsg.set(null);

    this.authService.requestOtp(this.email.trim())
      .subscribe({
        next: () => {
          this.loading.set(false);
          this.step.set('otp');
        },
        error: (err) => {
          this.loading.set(false);
          if (err.status === 429)
            this.errorMsg.set('Too many requests. Please wait a few minutes.');
          else
            this.errorMsg.set('Something went wrong. Please try again.');
        }
      });
  }

  verifyOtp(): void {
    if (this.otp.length !== 6 || this.loading()) return;
    this.loading.set(true);
    this.errorMsg.set(null);

    this.authService.verifyOtp(this.email.trim(), this.otp.trim())
      .subscribe({
        next: () => {
          this.loading.set(false);
          this.step.set('success');
          setTimeout(() => window.location.reload(), 800);
        },
        error: (err) => {
          this.loading.set(false);
          if (err.status === 429)
            this.errorMsg.set('Too many attempts. Please request a new code.');
          else
            this.errorMsg.set('Invalid or expired code. Please try again.');
        }
      });
  }
}
