import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { PaymentDetail } from './payment-detail.model';
import { environment } from '../../environments/environment.development';

@Injectable({
  providedIn: 'root'
})
export class PaymentDetailService {
  url = environment.apiBaseUrl + '/PaymentDetail';

  list = signal<PaymentDetail[]>([]);
  formData: PaymentDetail = new PaymentDetail();
  formSubmitted: boolean = false;

  constructor(private http: HttpClient) {}

  refreshList() {
    this.http.get<PaymentDetail[]>(this.url).subscribe({
      next: (res) => {
        console.log('API RESPONSE', res);
        this.list.set(res);
      },
      error: (err) => console.log(err)
    });
  }

  postPaymentDetail() {
    return this.http.post(this.url, this.formData);
  }

  putPaymentDetail() {
    return this.http.put(`${this.url}/${this.formData.paymentDetailId}`, this.formData);
  }

  deletePaymentDetail(id: number) {
    return this.http.delete(`${this.url}/${id}`);
  }
}