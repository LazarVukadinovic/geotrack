import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Product } from '../models/product';
import { ProductService } from '../services/product';

@Component({
  selector: 'app-product-form',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './product-form.html',
  styleUrl: './product-form.css'
})
export class ProductForm implements OnInit {
  form: Product = {
    id: 0,
    name: '',
    price: 0,
    description: ''
  };

  editMode = false;

  constructor(
    private productService: ProductService,
    private route: ActivatedRoute,
    private router: Router
  ) {}

  ngOnInit(): void {
    const idParam = this.route.snapshot.paramMap.get('id');

    if (idParam) {
      this.editMode = true;
      const id = Number(idParam);

      this.productService.getById(id).subscribe({
        next: (product) => this.form = product,
        error: (err) => console.error(err)
      });
    }
  }

  save(): void {
    if (this.editMode) {
      this.productService.update(this.form).subscribe({
        next: () => this.router.navigate(['/products']),
        error: (err) => console.error(err)
      });
    } else {
      this.productService.create(this.form).subscribe({
        next: () => this.router.navigate(['/products']),
        error: (err) => console.error(err)
      });
    }
  }
}