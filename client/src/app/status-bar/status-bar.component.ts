import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { TorrentService } from '../torrent.service';
import { SettingsService } from '../settings.service';
import { Torrent } from '../models/torrent.model';
import { Profile } from '../models/profile.model';
import { DiskSpaceStatus } from '../models/disk-space-status.model';
import { FileSizePipe } from '../filesize.pipe';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-status-bar',
  standalone: true,
  imports: [CommonModule, FileSizePipe, DatePipe],
  templateUrl: './status-bar.component.html',
  styleUrls: ['./status-bar.component.scss'],
})
export class StatusBarComponent implements OnInit, OnDestroy {
  private torrentService = inject(TorrentService);
  private settingsService = inject(SettingsService);

  public totalSpeed = 0;
  public totalRemaining = 0;
  public eta = '';
  public diskSpace: DiskSpaceStatus | null = null;
  public profile: Profile | null = null;
  public isExpired = false;

  private subscriptions: Subscription = new Subscription();

  ngOnInit(): void {
    this.subscriptions.add(
      this.torrentService.update$.subscribe((torrents) => {
        this.calculateStats(torrents);
        this.checkProfile();
      }),
    );

    this.subscriptions.add(
      this.torrentService.diskSpaceStatus$.subscribe((status) => {
        this.diskSpace = status;
      }),
    );

    this.checkProfile();
  }

  private checkProfile(): void {
    this.settingsService.getProfile().subscribe((result: Profile) => {
      this.profile = result;
      this.isExpired = result.expiration && new Date(result.expiration) < new Date();
    });
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  private calculateStats(torrents: Torrent[]): void {
    let speed = 0;
    let remaining = 0;

    torrents.forEach((torrent) => {
      torrent.downloads?.forEach((download) => {
        if (!download.downloadFinished) {
          speed += download.speed || 0;
          remaining += Math.max(0, (download.bytesTotal || 0) - (download.bytesDone || 0));
        }
      });
      // Also include provider speed if downloading on provider (Status 3 = Downloading)
      if (torrent.rdStatus === 3) {
        speed += torrent.rdSpeed || 0;
        remaining += (torrent.rdSize || 0) * (1 - (torrent.rdProgress || 0) / 100);
      }
    });

    this.totalSpeed = speed;
    this.totalRemaining = remaining;

    if (speed > 0) {
      const seconds = remaining / speed;
      this.eta = this.formatDuration(seconds);
    } else {
      this.eta = 'Idle';
    }
  }

  private formatDuration(seconds: number): string {
    if (seconds === Infinity || isNaN(seconds) || seconds < 0) return 'Unknown';
    if (seconds === 0) return '0s';

    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = Math.floor(seconds % 60);

    const hDisplay = h > 0 ? h + 'h ' : '';
    const mDisplay = m > 0 ? m + 'm ' : '';
    const sDisplay = s > 0 ? s + 's' : '';
    return (hDisplay + mDisplay + sDisplay).trim() || '0s';
  }
}
