import { Component, OnInit, HostListener, inject } from '@angular/core';
import { NavbarComponent } from '../navbar/navbar.component';
import { StatusBarComponent } from '../status-bar/status-bar.component';
import { RouterOutlet } from '@angular/router';
import { Profile } from '../models/profile.model';
import { SettingsService } from '../settings.service';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-main-layout',
  templateUrl: './main-layout.component.html',
  styleUrls: ['./main-layout.component.scss'],
  imports: [NavbarComponent, StatusBarComponent, RouterOutlet, CommonModule],
  standalone: true,
})
export class MainLayoutComponent implements OnInit {
  private settingsService = inject(SettingsService);

  public profile: Profile;
  public showUpdateBanner = true;
  public showBackToTop = false;

  ngOnInit(): void {
    this.settingsService.getProfile().subscribe((result) => {
      this.profile = result;
    });
  }

  @HostListener('window:scroll', [])
  onWindowScroll() {
    const scrollPos = window.pageYOffset || document.documentElement.scrollTop || document.body.scrollTop || 0;
    this.showBackToTop = scrollPos > 200;
  }

  scrollToTop() {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  closeBanner() {
    this.showUpdateBanner = false;
  }
}
