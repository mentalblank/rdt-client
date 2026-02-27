import { Component, OnInit } from '@angular/core';
import { SettingsService } from 'src/app/settings.service';
import { Setting } from '../models/setting.model';
import { NgClass, KeyValuePipe, NgIf } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Nl2BrPipe } from '../nl2br.pipe';
import { FileSizePipe } from '../filesize.pipe';

@Component({
  selector: 'app-settings',
  templateUrl: './settings.component.html',
  styleUrls: ['./settings.component.scss'],
  imports: [NgClass, FormsModule, KeyValuePipe, Nl2BrPipe, FileSizePipe, NgIf],
  standalone: true,
})
export class SettingsComponent implements OnInit {
  public activeTab = 0;

  public tabs: Setting[] = [];

  public saving = false;
  public error: string;

  public testPathError: string;
  public testPathSuccess: boolean;

  public testDownloadSpeedError: string;
  public testDownloadSpeedSuccess: number;

  public testWriteSpeedError: string;
  public testWriteSpeedSuccess: number;

  public testAria2cConnectionError: string = null;
  public testAria2cConnectionSuccess: string = null;

  public canRegisterMagnetHandler = false;

  public categoryMappings: { category: string; provider: string }[] = [];
  public providerOptions: string[] = ['RealDebrid', 'AllDebrid', 'Premiumize', 'DebridLink', 'TorBox'];

  constructor(private settingsService: SettingsService) {}

  ngOnInit(): void {
    this.reset();
    this.canRegisterMagnetHandler = !!(window.isSecureContext && 'registerProtocolHandler' in navigator);
  }

  public reset(): void {
    this.settingsService.get().subscribe((settings) => {
      this.tabs = settings.filter((m) => m.key.indexOf(':') === -1);

      for (let tab of this.tabs) {
        tab.settings = settings.filter((m) => m.key.indexOf(`${tab.key}:`) > -1);
        if (tab.key === 'Provider') {
          const groupOrder = ['RealDebrid', 'AllDebrid', 'Premiumize', 'DebridLink', 'TorBox', 'Other'];
          const getGroup = (key: string) => {
            if (key.includes('RealDebrid')) return 'RealDebrid';
            if (key.includes('AllDebrid')) return 'AllDebrid';
            if (key.includes('Premiumize')) return 'Premiumize';
            if (key.includes('DebridLink')) return 'DebridLink';
            if (key.includes('TorBox') || key.includes('PreferZippedDownloads')) return 'TorBox';
            return 'Other';
          };

          tab.settings.sort((a, b) => {
            const groupA = getGroup(a.key);
            const groupB = getGroup(b.key);

            if (groupA !== groupB) {
              return groupOrder.indexOf(groupA) - groupOrder.indexOf(groupB);
            }

            // Within same group, Enabled comes first
            if (a.key.endsWith('Enabled')) return -1;
            if (b.key.endsWith('Enabled')) return 1;

            // Then ApiKey
            if (a.key.includes('ApiKey')) return -1;
            if (b.key.includes('ApiKey')) return 1;

            return 0;
          });

          const mappingSetting = tab.settings.find((s) => s.key === 'Provider:CategoryMapping');
          if (mappingSetting && mappingSetting.value) {
            this.categoryMappings = (mappingSetting.value as string).split(',').map((m) => {
              const parts = m.split(':');
              return { category: parts[0]?.trim(), provider: parts[1]?.trim() };
            }).filter(m => m.category !== '*');
          } else {
            this.categoryMappings = [];
          }
        }
      }
    });
  }

  public ok(): void {
    this.saving = true;

    const providerTab = this.tabs.find((t) => t.key === 'Provider');
    if (providerTab) {
      const mappingSetting = providerTab.settings.find((s) => s.key === 'Provider:CategoryMapping');
      if (mappingSetting) {
        mappingSetting.value = this.categoryMappings
          .filter((m) => m.category && m.provider)
          .map((m) => `${m.category}:${m.provider}`)
          .join(',');
      }
    }

    const settingsToSave = this.tabs.flatMap((m) => m.settings).filter((m) => m.type !== 'Object');

    this.settingsService.update(settingsToSave).subscribe({
      next: () =>
        setTimeout(() => {
          this.saving = false;
        }, 1000),
      error: (err) => {
        this.saving = false;
        this.error = err;
      },
    });
  }

  public addCategoryMapping(): void {
    this.categoryMappings.push({ category: '', provider: 'RealDebrid' });
  }

  public removeCategoryMapping(index: number): void {
    this.categoryMappings.splice(index, 1);
  }

  public testDownloadPath(): void {
    const settingDownloadPath = this.tabs
      .find((m) => m.key === 'DownloadClient')
      .settings.find((m) => m.key === 'DownloadClient:DownloadPath').value as string;

    this.saving = true;
    this.testPathError = null;
    this.testPathSuccess = false;

    this.settingsService.testPath(settingDownloadPath).subscribe({
      next: () => {
        this.saving = false;
        this.testPathSuccess = true;
      },
      error: (err) => {
        this.testPathError = err.error;
        this.saving = false;
      },
    });
  }

  public testDownloadSpeed(): void {
    this.saving = true;
    this.testDownloadSpeedError = null;
    this.testDownloadSpeedSuccess = 0;

    this.settingsService.testDownloadSpeed().subscribe({
      next: (result) => {
        this.saving = false;
        this.testDownloadSpeedSuccess = result;
      },
      error: (err) => {
        this.testDownloadSpeedError = err.error;
        this.saving = false;
      },
    });
  }
  public testWriteSpeed(): void {
    this.saving = true;
    this.testWriteSpeedError = null;
    this.testWriteSpeedSuccess = 0;

    this.settingsService.testWriteSpeed().subscribe({
      next: (result) => {
        this.saving = false;
        this.testWriteSpeedSuccess = result;
      },
      error: (err) => {
        this.testWriteSpeedError = err.error;
        this.saving = false;
      },
    });
  }

  public testAria2cConnection(): void {
    const settingAria2cUrl = this.tabs
      .find((m) => m.key === 'DownloadClient')
      .settings.find((m) => m.key === 'DownloadClient:Aria2cUrl').value as string;
    const settingAria2cSecret = this.tabs
      .find((m) => m.key === 'DownloadClient')
      .settings.find((m) => m.key === 'DownloadClient:Aria2cSecret').value as string;

    this.saving = true;
    this.testAria2cConnectionError = null;
    this.testAria2cConnectionSuccess = null;

    this.settingsService.testAria2cConnection(settingAria2cUrl, settingAria2cSecret).subscribe({
      next: (result) => {
        this.saving = false;
        this.testAria2cConnectionSuccess = result.version;
      },
      error: (err) => {
        this.testAria2cConnectionError = err.error;
        this.saving = false;
      },
    });
  }

  public registerMagnetHandler(): void {
    try {
      navigator.registerProtocolHandler('magnet', `${window.location.origin}/add?magnet=%s`);
      alert(
        'Success! Your browser will now prompt you to confirm and add the client as the default handler for magnet links.',
      );
    } catch (error) {
      alert('Magnet link registration failed.');
    }
  }

  public isSettingVisible(setting: Setting): boolean {
    const providerEnabledMappings: { [key: string]: string } = {
      'Provider:RealDebridApiKey': 'Provider:RealDebridEnabled',
      'Provider:ApiHostname': 'Provider:RealDebridEnabled',
      'DownloadClient:RcloneMountPathRealDebrid': 'Provider:RealDebridEnabled',

      'Provider:AllDebridApiKey': 'Provider:AllDebridEnabled',
      'DownloadClient:RcloneMountPathAllDebrid': 'Provider:AllDebridEnabled',

      'Provider:PremiumizeApiKey': 'Provider:PremiumizeEnabled',
      'DownloadClient:RcloneMountPathPremiumize': 'Provider:PremiumizeEnabled',

      'Provider:DebridLinkApiKey': 'Provider:DebridLinkEnabled',
      'DownloadClient:RcloneMountPathDebridLink': 'Provider:DebridLinkEnabled',

      'Provider:TorBoxApiKey': 'Provider:TorBoxEnabled',
      'Provider:PreferZippedDownloads': 'Provider:TorBoxEnabled',
      'DownloadClient:RcloneMountPathTorBox': 'Provider:TorBoxEnabled',
    };

    const dependencyKey = providerEnabledMappings[setting.key];
    if (dependencyKey) {
      const providerTab = this.tabs.find((t) => t.key === 'Provider');
      if (providerTab) {
        const enabledSetting = providerTab.settings.find((s) => s.key === dependencyKey);
        return enabledSetting?.value === true;
      }
    }

    return true;
  }

  public isDependentSetting(setting: Setting): boolean {
    const providerEnabledMappings: { [key: string]: string } = {
      'Provider:RealDebridApiKey': 'Provider:RealDebridEnabled',
      'Provider:ApiHostname': 'Provider:RealDebridEnabled',
      'DownloadClient:RcloneMountPathRealDebrid': 'Provider:RealDebridEnabled',

      'Provider:AllDebridApiKey': 'Provider:AllDebridEnabled',
      'DownloadClient:RcloneMountPathAllDebrid': 'Provider:AllDebridEnabled',

      'Provider:PremiumizeApiKey': 'Provider:PremiumizeEnabled',
      'DownloadClient:RcloneMountPathPremiumize': 'Provider:PremiumizeEnabled',

      'Provider:DebridLinkApiKey': 'Provider:DebridLinkEnabled',
      'DownloadClient:RcloneMountPathDebridLink': 'Provider:DebridLinkEnabled',

      'Provider:TorBoxApiKey': 'Provider:TorBoxEnabled',
      'Provider:PreferZippedDownloads': 'Provider:TorBoxEnabled',
      'DownloadClient:RcloneMountPathTorBox': 'Provider:TorBoxEnabled',
    };

    return !!providerEnabledMappings[setting.key];
  }
}
