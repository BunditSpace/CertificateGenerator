import { inject, Injectable, NgZone } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { JobStatusUpdate } from '../models/document.models';

@Injectable({
  providedIn: 'root',
})
export class SseProgressService {
  private readonly ngZone = inject(NgZone);
  private readonly baseUrl = environment.apiBaseUrl.replace(/\/+$/, '');

  streamJobProgress(jobId: string): Observable<JobStatusUpdate> {
    return new Observable<JobStatusUpdate>((observer) => {
      const eventSource = new EventSource(`${this.baseUrl}/api/documents/stream/${jobId}`);

      eventSource.onmessage = (event: MessageEvent<string>) => {
        this.ngZone.run(() => {
          try {
            const update = JSON.parse(event.data) as JobStatusUpdate;
            observer.next(update);
            if (update.status === 'Completed' || update.status === 'Failed') {
              eventSource.close();
              observer.complete();
            }
          } catch (err) {
            observer.error(err);
          }
        });
      };

      eventSource.onerror = (error) => {
        this.ngZone.run(() => {
          eventSource.close();
          observer.error(error);
        });
      };

      return () => {
        eventSource.close();
      };
    });
  }
}
