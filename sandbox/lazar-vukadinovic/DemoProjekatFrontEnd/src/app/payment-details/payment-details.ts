import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PaymentDetailForm } from './payment-detail-form/payment-detail-form';
import { PaymentDetailService } from '../shared/payment-detail.service';
import { PaymentDetail } from '../shared/payment-detail.model';
import { ToastrService } from 'ngx-toastr';

@Component({
  selector: 'app-payment-details',
  standalone: true,
  imports: [PaymentDetailForm, CommonModule],
  templateUrl: './payment-details.html',
  styles: ``,
})
export class PaymentDetails implements OnInit {
  constructor(public paymentDetailService: PaymentDetailService, private toastr: ToastrService) { }

  ngOnInit(): void {
    this.paymentDetailService.refreshList();
  }

  populateForm(selectedRecord: PaymentDetail) {
    this.paymentDetailService.formData = { ...selectedRecord };
  }

  deleteRecord(id: number){
    this.paymentDetailService.deletePaymentDetail(id).subscribe({
        next: (res) => {
          this.paymentDetailService.refreshList();
          this.toastr.error('Payment detail deleted successfully', 'Deleted');
        },
        error: (err) => console.log(err)
      });
  }
}
