import { Component, OnInit, AfterViewInit } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { AuthService } from '../auth.service';
import { Profile } from '../models/profile.model';
import { SettingsService } from '../settings.service';

@Component({
  selector: 'app-navbar',
  templateUrl: './navbar.component.html',
  styleUrls: ['./navbar.component.scss'],
  standalone: false,
})
export class NavbarComponent implements OnInit, AfterViewInit {
  public showMobileMenu = false;
  public profile: Profile;
  public providerLink: string;
  public version: string;

  constructor(
    private settingsService: SettingsService,
    private authService: AuthService,
    private router: Router,
  ) {
    this.router.events.subscribe((event) => {
      if (event instanceof NavigationEnd) {
        this.showMobileMenu = false;
      }
    });
  }

  ngOnInit(): void {
    this.settingsService.getProfile().subscribe((result) => {
      this.profile = result;
      switch (result.provider) {
        case 'RealDebrid':
          this.providerLink = 'https://real-debrid.com/';
          break;
        case 'AllDebrid':
          this.providerLink = 'https://alldebrid.com/';
          break;
        case 'Premiumize':
          this.providerLink = 'https://www.premiumize.me/';
          break;
        case 'TorBox':
          this.providerLink = 'https://torbox.app/';
          break;
        case 'DebridLink':
          this.providerLink = 'https://debrid-link.com/';
          break;
      }
    });

    this.settingsService.getVersion().subscribe((result) => {
      this.version = result.version;
    });
  }

  ngAfterViewInit(): void {
    document.getElementById("register-magnet")?.addEventListener("click", () => {
      const handlerUrl = `${window.location.origin}/add?magnet=%s`;
      try {
        if (navigator.registerProtocolHandler) {
          navigator.registerProtocolHandler("magnet", handlerUrl);
          alert("Magnet links will now open with RDT-Client.");
        } else {
          throw new Error("Your browser does not support automatic magnet link registration.");
        }
      } catch {
        alert(`Your browser does not support automatic magnet link registration.\n\nFor Firefox:\n1. Open about:config\n2. Search for 'network.protocol-handler.external.magnet'\n3. Set to 'true'\n4. Add handler in 'Applications' settings.`);
      }
    });
  }

  public logout(): void {
    this.authService.logout().subscribe(
      () => {
        this.router.navigate(['/login']);
      },
      (err) => {}
    );
  }
}
