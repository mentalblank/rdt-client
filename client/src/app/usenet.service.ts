import { HttpClient } from '@angular/common/http';
import { Inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { UsenetJob } from './models/usenet-job.model';
import { APP_BASE_HREF } from '@angular/common';

@Injectable({
  providedIn: 'root',
})
export class UsenetService {
  constructor(
    private http: HttpClient,
    @Inject(APP_BASE_HREF) private baseHref: string,
  ) {}

  public getList(): Observable<UsenetJob[]> {
    return this.http.get<UsenetJob[]>(`${this.baseHref}api/usenet`);
  }

  public get(id: string): Observable<UsenetJob> {
    return this.http.get<UsenetJob>(`${this.baseHref}api/usenet/${id}`);
  }

  public uploadFile(file: File, category: string | null | undefined, priority: number | null | undefined): Observable<void> {
    const formData: FormData = new FormData();
    formData.append('file', file);
    formData.append('category', category || '');
    formData.append('priority', (priority || 0).toString());
    return this.http.post<void>(`${this.baseHref}api/usenet/upload`, formData);
  }

  public delete(hash: string, deleteData: boolean): Observable<void> {
    return this.http.delete<void>(`${this.baseHref}api/usenet/${hash}?deleteData=${deleteData}`);
  }

  public deleteAll(deleteData: boolean): Observable<void> {
    return this.http.delete<void>(`${this.baseHref}api/usenet/all?deleteData=${deleteData}`);
  }

  public getWebdavList(path: string | null): Observable<{ items: any[]; readOnly: boolean }> {
    return this.http.get<{ items: any[]; readOnly: boolean }>(`${this.baseHref}api/usenet/webdav?path=${encodeURIComponent(path || '/')}`);
  }

  public downloadWebdavFile(path: string): string {
    return `${this.baseHref}api/usenet/webdav/download?path=${encodeURIComponent(path)}`;
  }

  public deleteWebdavItem(path: string): Observable<void> {
    return this.http.delete<void>(`${this.baseHref}api/usenet/webdav?path=${encodeURIComponent(path)}`);
  }
}
