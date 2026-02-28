import { Component, OnInit } from '@angular/core';
import { UsenetService } from '../usenet.service';
import { NgFor, NgIf, CommonModule } from '@angular/common';
import { FileSizePipe } from '../filesize.pipe';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

@Component({
  selector: 'app-webdav-browser',
  templateUrl: './webdav-browser.component.html',
  standalone: true,
  imports: [NgFor, NgIf, CommonModule, FileSizePipe, FormsModule, RouterLink],
})
export class WebdavBrowserComponent implements OnInit {
  public items: any[] = [];
  public loading = false;
  public path = '/';
  public error: string | null = null;
  public readOnly = true;
  public selectedItems: string[] = [];

  constructor(
    private usenetService: UsenetService,
    private route: ActivatedRoute,
    private router: Router,
  ) {}

  ngOnInit(): void {
    this.route.queryParams.subscribe((params) => {
      this.path = params['path'] || '/';
      this.selectedItems = [];
      this.load();
    });
  }

  public load(): void {
    this.loading = true;
    this.error = null;
    this.usenetService.getWebdavList(this.path).subscribe({
      next: (result) => {
        this.items = result.items;
        this.readOnly = result.readOnly;
        this.loading = false;
      },
      error: (err) => {
        this.error = err.error || err.message;
        this.loading = false;
      },
    });
  }

  public toggleSelect(name: string): void {
    if (this.selectedItems.includes(name)) {
      this.selectedItems = this.selectedItems.filter((s) => s !== name);
    } else {
      this.selectedItems.push(name);
    }
  }

  public toggleSelectAll(event: any): void {
    if (event.target.checked) {
      this.selectedItems = this.items.map((i) => i.name);
    } else {
      this.selectedItems = [];
    }
  }

  public download(item: any): void {
    const fullPath = this.getFullPath(item.name);
    const url = this.usenetService.downloadWebdavFile(fullPath);
    window.open(url, '_blank');
  }

  public downloadSelected(): void {
    const selected = this.items.filter((i) => this.selectedItems.includes(i.name) && !i.isDirectory);
    if (selected.length === 0) return;
    
    selected.forEach((item, index) => {
      // Delay multiple windows to avoid browser blocking
      setTimeout(() => {
        this.download(item);
      }, index * 500);
    });
  }

  public delete(item: any): void {
    if (confirm(`Are you sure you want to delete ${item.name}?`)) {
      const fullPath = this.getFullPath(item.name);
      this.usenetService.deleteWebdavItem(fullPath).subscribe({
        next: () => this.load(),
        error: (err) => alert('Error deleting item: ' + (err.error || err.message)),
      });
    }
  }

  public async deleteSelected(): Promise<void> {
    if (this.selectedItems.length === 0) return;
    if (confirm(`Are you sure you want to delete ${this.selectedItems.length} items?`)) {
      this.loading = true;
      try {
        for (const name of this.selectedItems) {
          const fullPath = this.getFullPath(name);
          await this.usenetService.deleteWebdavItem(fullPath).toPromise();
        }
        this.selectedItems = [];
        this.load();
      } catch (err: any) {
        alert('Error deleting items: ' + (err.error || err.message));
        this.loading = false;
      }
    }
  }

  private getFullPath(name: string): string {
    return this.path.endsWith('/') ? `${this.path}${name}` : `${this.path}/${name}`;
  }

  public navigate(item: any): void {
    if (item.isDirectory) {
      const newPath = this.getFullPath(item.name);
      this.router.navigate([], { queryParams: { path: newPath } });
    }
  }

  public navigateUp(): void {
    if (this.path === '/') return;
    const segments = this.path.split('/').filter(s => s.length > 0);
    segments.pop();
    const newPath = '/' + segments.join('/');
    this.router.navigate([], { queryParams: { path: newPath } });
  }

  public get pathSegments(): { name: string; path: string }[] {
    const segments = this.path.split('/').filter((s) => s.length > 0);
    const result: { name: string; path: string }[] = [{ name: 'Root', path: '/' }];
    let currentPath = '';
    for (const segment of segments) {
      currentPath += '/' + segment;
      result.push({ name: segment, path: currentPath });
    }
    return result;
  }

  public navigateToPath(path: string): void {
    this.router.navigate([], { queryParams: { path: path } });
  }
}
