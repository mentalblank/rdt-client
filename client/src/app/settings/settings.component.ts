import { Component, OnInit, inject } from '@angular/core';
import { SettingsService } from 'src/app/settings.service';
import { Setting } from '../models/setting.model';
import { NgClass, KeyValuePipe, CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Nl2BrPipe } from '../nl2br.pipe';
import { FileSizePipe } from '../filesize.pipe';

interface SettingSection {
  key: string;
  displayName: string;
  description: string;
  subMenus: {
    title: string;
    settings: Setting[];
    isVisible?: () => boolean;
  }[];
  isVisible?: () => boolean;
}

@Component({
  selector: 'app-settings',
  templateUrl: './settings.component.html',
  styleUrls: ['./settings.component.scss'],
  imports: [NgClass, FormsModule, KeyValuePipe, Nl2BrPipe, FileSizePipe, CommonModule],
  standalone: true,
})
export class SettingsComponent implements OnInit {
  private settingsService = inject(SettingsService);

  public activeTab = 0;
  public activeSubTab = 0;
  public sections: SettingSection[] = [];
  public allSettings: Setting[] = [];
  private originalSettingsJson: string = '';

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
  public selectedWorkflowSource: 'Gui' | 'Integrations' | 'Watch' | 'Provider' | null = 'Gui';
  public providerOptions: string[] = ['RealDebrid', 'AllDebrid', 'Premiumize', 'DebridLink', 'TorBox'];

  ngOnInit(): void {
    this.reset();
    this.canRegisterMagnetHandler = !!(window.isSecureContext && 'registerProtocolHandler' in navigator);
  }

  public setActiveTab(index: number): void {
    this.activeTab = index;
    this.activeSubTab = 0;
  }

  public get hasChanges(): boolean {
    if (!this.originalSettingsJson) return false;
    const currentJson = JSON.stringify(this.allSettings.map((s) => ({ key: s.key, value: s.value })));
    return currentJson !== this.originalSettingsJson;
  }

  public reset(): void {
    this.settingsService.get().subscribe((settings) => {
      this.allSettings = settings;
      this.originalSettingsJson = JSON.stringify(settings.map((s) => ({ key: s.key, value: s.value })));

      const findSetting = (key: string) => settings.find((s) => s.key === key);
      const filterSettings = (keys: string[]) => keys.map((k) => findSetting(k)).filter((s) => !!s);

      this.sections = [
        {
          key: 'system',
          displayName: 'System',
          description: 'Core application engine settings and global behaviors.',
          subMenus: [
            {
              title: 'General',
              settings: filterSettings([
                'General:LogLevel',
                'General:DownloadLimit',
                'General:UnpackLimit',
                'General:AuthenticationType',
                'General:DisableUpdateNotifications',
                'General:Categories',
                'General:TrackerEnrichmentList',
                'General:BannedTrackers',
                'General:TrackerEnrichmentCacheExpiration',
                'General:CopyAddedTorrents',
                'General:RcloneRefreshCommand',
                'General:FairUseLimitCooldown',
              ]),
            },
            {
              title: 'Download Client',
              settings: filterSettings([
                'DownloadClient:Client',
                'DownloadClient:MaxSpeed',
                'DownloadClient:ParallelCount',
                'DownloadClient:ChunkCount',
                'DownloadClient:BufferSize',
                'DownloadClient:Timeout',
                'DownloadClient:ProxyServer',
                'DownloadClient:Aria2cUrl',
                'DownloadClient:Aria2cSecret',
                'DownloadClient:Aria2cDownloadPath',
                'DownloadClient:DownloadStationUrl',
                'DownloadClient:DownloadStationUsername',
                'DownloadClient:DownloadStationPassword',
                'DownloadClient:DownloadStationDownloadPath',
                'DownloadClient:LogLevel',
              ]),
            },
          ],
        },
        {
          key: 'torrents',
          displayName: 'Debrid & Torrents',
          description: 'Provider configuration and torrent processing logic.',
          subMenus: [
            {
              title: 'Providers',
              settings: [], // Special UI
            },
            {
              title: 'Behavior',
              settings: filterSettings([
                'Provider:AutoImport',
                'Provider:AutoDelete',
                'Provider:Timeout',
                'Provider:CheckInterval',
                'Provider:MaxParallelDownloads',
                'Provider:StalledAction',
                'Provider:StalledTimeout',
                'Provider:StalledDeleteData',
                'Provider:StalledDeleteRdTorrent',
                'Provider:StalledDeleteLocalFiles',
                'Provider:InfringingAction',
                'Provider:InfringingDeleteData',
                'Provider:InfringingDeleteRdTorrent',
                'Provider:InfringingDeleteLocalFiles',
              ]),
            },
            {
              title: 'External Scripts',
              settings: filterSettings([
                'General:RunOnTorrentCompleteFileName',
                'General:RunOnTorrentCompleteArguments',
              ]),
            },
          ],
        },
        {
          key: 'storage',
          displayName: 'Storage & Paths',
          description: 'Configure where files are saved and how disk space is managed.',
          subMenus: [
            {
              title: 'Global Paths',
              settings: filterSettings([
                'DownloadClient:DownloadPath',
                'DownloadClient:MappedPath',
                'DownloadClient:RcloneMountPath',
              ]),
            },
            {
              title: 'Disk Management',
              settings: filterSettings([
                'DownloadClient:MinimumFreeSpaceGB',
                'DownloadClient:DiskSpaceCheckIntervalMinutes',
              ]),
            },
          ],
        },
        {
          key: 'workflow',
          displayName: 'Automation Workflow',
          description: 'Default settings for different ways of adding torrents.',
          subMenus: [
            {
              title: 'Defaults Matrix',
              settings: [], // Matrix UI
            },
          ],
        },
        {
          key: 'watch',
          displayName: 'Watch Folder',
          description: 'Monitor directories for new torrent and magnet files.',
          subMenus: [
            {
              title: 'Configuration',
              settings: filterSettings(['Watch:Path', 'Watch:ErrorPath', 'Watch:ProcessedPath', 'Watch:Interval']),
            },
          ],
        },
      ];
    });
  }

  public ok(): void {
    this.saving = true;

    const settingsToSave = this.allSettings.filter((m) => m.type !== 'Object');

    this.settingsService.update(settingsToSave).subscribe({
      next: () =>
        setTimeout(() => {
          this.saving = false;
          this.reset();
        }, 1000),
      error: (err) => {
        this.saving = false;
        this.error = err;
      },
    });
  }

  public getSetting(key: string): Setting | undefined {
    return this.allSettings.find((s) => s.key === key);
  }

  public getProviderGroup(providerName: string): { enabled: boolean; apiKey: Setting; extras: Setting[] } {
    const providerEnumSetting = this.getSetting('Provider:Provider');
    const enabled =
      providerEnumSetting?.value == providerName ||
      this.getProviderEnumLabel(providerEnumSetting?.value) === providerName;
    const apiKey = this.getSetting('Provider:ApiKey');
    const extras = this.allSettings.filter(
      (s) =>
        (s.key === 'Provider:ApiHostname' && providerName === 'RealDebrid') ||
        (s.key === 'Provider:PreferZippedDownloads' && providerName === 'TorBox'),
    );
    return { enabled, apiKey, extras };
  }

  private getProviderEnumLabel(value: any): string {
    const setting = this.getSetting('Provider:Provider');
    if (!setting || !setting.enumValues) return '';
    return setting.enumValues[value] || '';
  }

  public setProvider(providerName: string): void {
    const setting = this.getSetting('Provider:Provider');
    if (!setting || !setting.enumValues) return;

    // Find the key for the providerName
    for (const key in setting.enumValues) {
      if (setting.enumValues[key] === providerName || key === providerName) {
        setting.value = isNaN(Number(key)) ? key : Number(key);
        break;
      }
    }
  }

  public getWorkflowMatrix(): { label: string; keySuffix: string; type: string }[] {
    return [
      { label: 'Category', keySuffix: 'Category', type: 'String' },
      { label: 'Host Download Action', keySuffix: 'HostDownloadAction', type: 'Enum' },
      { label: 'Download Action', keySuffix: 'OnlyDownloadAvailableFiles', type: 'Boolean' },
      { label: 'Allow Compressed (Symlink)', keySuffix: 'DownloadCompressedSymlink', type: 'Boolean' },
      { label: 'Finished Action', keySuffix: 'FinishedAction', type: 'Enum' },
      { label: 'Finished Action Delay', keySuffix: 'FinishedActionDelay', type: 'Int32' },
      { label: 'Min File Size (MB)', keySuffix: 'MinFileSize', type: 'Int32' },
      { label: 'Priority', keySuffix: 'Priority', type: 'Int32' },
      { label: 'Torrent Retries', keySuffix: 'TorrentRetryAttempts', type: 'Int32' },
      { label: 'Download Retries', keySuffix: 'DownloadRetryAttempts', type: 'Int32' },
      { label: 'Delete On Error (min)', keySuffix: 'DeleteOnError', type: 'Int32' },
      { label: 'Lifetime (min)', keySuffix: 'TorrentLifetime', type: 'Int32' },
      { label: 'Include Regex', keySuffix: 'IncludeRegex', type: 'String' },
      { label: 'Exclude Regex', keySuffix: 'ExcludeRegex', type: 'String' },
      { label: 'Trim Folder Extensions', keySuffix: 'TrimRegex', type: 'String' },
    ];
  }

  public getMatrixSetting(source: string, suffix: string): Setting | undefined {
    return this.allSettings.find((s) => s.key === `${source}:Default:${suffix}`);
  }

  public testDownloadPath(): void {
    const settingDownloadPath = this.getSetting('DownloadClient:DownloadPath')?.value as string;

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
    const settingAria2cUrl = this.getSetting('DownloadClient:Aria2cUrl')?.value as string;
    const settingAria2cSecret = this.getSetting('DownloadClient:Aria2cSecret')?.value as string;

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
    const activeClient = this.getSetting('DownloadClient:Client')?.value;
    const providerEnumSetting = this.getSetting('Provider:Provider');
    const activeProviderValue = providerEnumSetting?.value;
    const activeProviderName = providerEnumSetting?.enumValues
      ? providerEnumSetting.enumValues[activeProviderValue as any]
      : '';

    // Mapping keys to their required clients
    // 0: Bezzad, 1: Aria2c, 2: Symlink, 3: DownloadStation
    const clientSpecific: { [key: string]: number[] } = {
      'DownloadClient:MaxSpeed': [0],
      'DownloadClient:ParallelCount': [0],
      'DownloadClient:ChunkCount': [0],
      'DownloadClient:BufferSize': [0],
      'DownloadClient:Timeout': [0],
      'DownloadClient:ProxyServer': [0],
      'DownloadClient:Aria2cUrl': [1],
      'DownloadClient:Aria2cSecret': [1],
      'DownloadClient:Aria2cDownloadPath': [1],
      'DownloadClient:DownloadStationUrl': [3],
      'DownloadClient:DownloadStationUsername': [3],
      'DownloadClient:DownloadStationPassword': [3],
      'DownloadClient:DownloadStationDownloadPath': [3],
      'DownloadClient:MinimumFreeSpaceGB': [0],
      'DownloadClient:RcloneMountPath': [2],
      DownloadCompressedSymlink: [2],
    };

    for (const key in clientSpecific) {
      if (setting.key === key || setting.key.endsWith(':Default:' + key)) {
        if (!clientSpecific[key].includes(Number(activeClient))) return false;
      }
    }

    // Provider specific settings
    if (setting.key === 'Provider:PreferZippedDownloads' && activeProviderName !== 'TorBox') {
      return false;
    }
    if (setting.key === 'Provider:ApiHostname' && activeProviderName !== 'RealDebrid') {
      return false;
    }

    if (setting.key.startsWith('Provider:Stalled') && setting.key !== 'Provider:StalledAction') {
      const stalledAction = this.getSetting('Provider:StalledAction')?.value;
      if (stalledAction == 0) {
        // 0 is None
        return false;
      }
    }

    if (setting.key.startsWith('Provider:Infringing') && setting.key !== 'Provider:InfringingAction') {
      const infringingAction = this.getSetting('Provider:InfringingAction')?.value;
      if (infringingAction == 0) {
        // 0 is None
        return false;
      }
    }

    if (setting.key === 'General:TrackerEnrichmentCacheExpiration') {
      const enrichmentList = this.getSetting('General:TrackerEnrichmentList')?.value as string;
      const bannedTrackers = this.getSetting('General:BannedTrackers')?.value as string;

      const isUrl = (val: string) => val && (val.startsWith('http://') || val.startsWith('https://'));

      if (!isUrl(enrichmentList) && !isUrl(bannedTrackers)) {
        return false;
      }
    }

    return true;
  }
}
