import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../auth.service';
import { NgClass, CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-setup',
  templateUrl: './setup.component.html',
  styleUrls: ['./setup.component.scss'],
  imports: [FormsModule, NgClass, CommonModule],
  standalone: true,
})
export class SetupComponent {
  private authService = inject(AuthService);
  private router = inject(Router);

  public userName: string = 'admin';
  public password: string;
  public confirmPassword: string;
  public provider = 0;
  public token: string;

  public error: string | null = null;
  public working: boolean;

  public step: number = 1;

  public nextStep(): void {
      if (this.step === 1) {
          if (!this.password || this.password !== this.confirmPassword) {
              this.error = "Passwords do not match";
              return;
          }
          this.error = null;
          this.step = 2;
      } else if (this.step === 2) {
          if (!this.token) {
              this.error = "Please provide an API key";
              return;
          }
          this.finish();
      }
  }

  public prevStep(): void {
      if (this.step > 1) {
          this.step--;
          this.error = null;
      }
  }

  public async finish(): Promise<void> {
    this.working = true;
    this.error = null;

    try {
        // Step 1: Create Admin Account
        await firstValueFrom(this.authService.create(this.userName, this.password));

        // Step 2: Configure Provider
        await firstValueFrom(this.authService.setupProvider(this.provider, this.token));

        this.step = 3; // Success step
    } catch (err: any) {
        this.error = err.error || "An unexpected error occurred during setup";
    } finally {
        this.working = false;
    }
  }

  public close(): void {
    this.router.navigate(['/']);
  }
}
