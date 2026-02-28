import { Component, OnInit } from '@angular/core';
import { UsenetJob } from '../models/usenet-job.model';
import { UsenetService } from '../usenet.service';
import { NgFor, NgIf, DatePipe, DecimalPipe } from '@angular/common';
import { FileSizePipe } from '../filesize.pipe';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-usenet',
  templateUrl: './usenet.component.html',
  standalone: true,
  imports: [NgFor, NgIf, DatePipe, DecimalPipe, FileSizePipe, FormsModule],
})
export class UsenetComponent implements OnInit {
  public jobs: UsenetJob[] = [];
  public loading = false;
  public selectedJobs: string[] = [];
  public isDeleteModalActive = false;
  public deleting = false;
  public deleteError: string | null = null;
  public deleteData = false;

  public sortProperty = 'added';
  public sortDirection = 'desc';
  public tab: 'queue' | 'history' = 'queue';

  constructor(private usenetService: UsenetService) {}

  ngOnInit(): void {
    this.load();
  }

  public get filteredJobs(): UsenetJob[] {
    if (this.tab === 'queue') {
      return this.jobs.filter(j => j.status !== 4);
    } else {
      return this.jobs.filter(j => j.status === 4);
    }
  }

  public load(): void {
    this.loading = true;
    this.usenetService.getList().subscribe({
      next: (jobs) => {
        this.jobs = jobs;
        this.applySort();
        this.loading = false;
      },
      error: () => (this.loading = false),
    });
  }

  public changeTab(tab: 'queue' | 'history'): void {
    this.tab = tab;
    this.selectedJobs = [];
  }

  public toggleSelect(hash: string): void {
    if (this.selectedJobs.includes(hash)) {
      this.selectedJobs = this.selectedJobs.filter((s) => s !== hash);
    } else {
      this.selectedJobs.push(hash);
    }
  }

  public toggleSelectAll(event: any): void {
    if (event.target.checked) {
      this.selectedJobs = this.filteredJobs.map((j) => j.hash);
    } else {
      this.selectedJobs = [];
    }
  }

  public showDeleteModal(): void {
    this.isDeleteModalActive = true;
    this.deleteError = null;
    this.deleteData = false;
  }

  public deleteCancel(): void {
    this.isDeleteModalActive = false;
  }

  public async deleteOk(): Promise<void> {
    this.deleting = true;
    try {
      for (const hash of this.selectedJobs) {
        await this.usenetService.delete(hash, this.deleteData).toPromise();
      }
      this.isDeleteModalActive = false;
      this.selectedJobs = [];
      this.load();
    } catch (err: any) {
      this.deleteError = err.message;
    } finally {
      this.deleting = false;
    }
  }

  public deleteAll(): void {
    const deleteMsg = this.tab === 'queue' ? 'Are you sure you want to delete all Usenet jobs in the queue?' : 'Are you sure you want to clear all history?';
    if (confirm(deleteMsg)) {
      this.usenetService.deleteAll(false).subscribe(() => {
        this.selectedJobs = [];
        this.load();
      });
    }
  }

  public getStatus(status: number): string {
    switch (status) {
      case 0: return 'Queued';
      case 1: return 'Processing';
      case 2: return 'Downloading';
      case 3: return 'Uploading';
      case 4: return 'Finished';
      case 99: return 'Error';
      default: return 'Unknown';
    }
  }

  public sort(property: string): void {
    if (this.sortProperty === property) {
      this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortProperty = property;
      this.sortDirection = 'asc';
    }
    this.applySort();
  }

  private applySort(): void {
    this.jobs.sort((a: any, b: any) => {
      let valA = a[this.sortProperty];
      let valB = b[this.sortProperty];

      if (this.sortProperty === 'fileCount') {
        valA = a.fileCount;
        valB = b.fileCount;
      }

      if (valA < valB) {
        return this.sortDirection === 'asc' ? -1 : 1;
      }
      if (valA > valB) {
        return this.sortDirection === 'asc' ? 1 : -1;
      }
      return 0;
    });
  }
}
