import { Component } from '@angular/core';
import { PaymentDetailService } from '../../shared/payment-detail.service';
import { FormsModule, NgForm } from '@angular/forms';
import { ToastrService } from 'ngx-toastr';

@Component({
  selector: 'app-payment-detail-form',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './payment-detail-form.html',
  styles: ``,
})
export class PaymentDetailForm {
  constructor(public paymentDetailService: PaymentDetailService, private toastr: ToastrService) { }
  
  onSubmit(form: NgForm){
    this.paymentDetailService.formSubmitted = true;

    if(form.valid) {
      if(this.paymentDetailService.formData.paymentDetailId === 0)
        this.insertedRecord(form);
      else
        this.updatedRecord(form);
    }
  }

  insertedRecord(form: NgForm){
    this.paymentDetailService.postPaymentDetail().subscribe({
        next: (res) => {
          this.paymentDetailService.refreshList();
          form.resetForm();
          this.toastr.success('Payment detail added successfully', 'Success');
          this.paymentDetailService.formSubmitted = false;
        },
        error: (err) => console.log(err)
      });
  }

  updatedRecord(form: NgForm){
    this.paymentDetailService.putPaymentDetail().subscribe({
        next: (res) => {
          this.paymentDetailService.refreshList();
          form.resetForm();
          this.toastr.info('Payment detail updated successfully', 'Updated');
          this.paymentDetailService.formSubmitted = false;
        },
        error: (err) => console.log(err)
      });
  }
}
