import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../auth.service';
import { NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-setup',
  templateUrl: './setup.component.html',
  styleUrls: ['./setup.component.scss'],
  imports: [FormsModule, NgClass],
  standalone: true,
})
export class SetupComponent {
  public userName: string;
  public password: string;
  public providers: { id: number; name: string; token: string; enabled: boolean }[] = [
    { id: 0, name: 'Real-Debrid', token: '', enabled: false },
    { id: 1, name: 'AllDebrid', token: '', enabled: false },
    { id: 2, name: 'Premiumize', token: '', enabled: false },
    { id: 3, name: 'TorBox', token: '', enabled: false },
    { id: 4, name: 'DebridLink', token: '', enabled: false },
  ];

  public error: string;
  public working: boolean;

  public step: number = 1;

  constructor(
    private authService: AuthService,
    private router: Router,
  ) {}

  public setup(): void {
    this.error = null;
    this.working = true;

    this.authService.create(this.userName, this.password).subscribe({
      next: () => {
        this.step = 2;
        this.working = false;
      },
      error: (err) => {
        this.working = false;
        this.error = err.error;
      },
    });
  }

  public setToken(): void {
    const enabledProviders = this.providers.filter((p) => p.enabled && p.token);
    if (enabledProviders.length === 0) {
      this.error = 'Please enable at least one provider and provide an API token';
      return;
    }

    this.working = true;
    this.error = null;

    const setupNextProvider = (index: number) => {
      if (index >= enabledProviders.length) {
        this.step = 3;
        this.working = false;
        return;
      }

      const p = enabledProviders[index];
      this.authService.setupProvider(p.id, p.token).subscribe({
        next: () => setupNextProvider(index + 1),
        error: (err: any) => {
          this.working = false;
          this.error = err.error;
        },
      });
    };

    setupNextProvider(0);
  }

  public close(): void {
    this.router.navigate(['/']);
  }
}
