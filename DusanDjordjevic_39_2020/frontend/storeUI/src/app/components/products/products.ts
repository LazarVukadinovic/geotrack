import { Component, OnInit } from '@angular/core';
import { ProductService } from '../../services/product.service';
import { Product } from '../../models/product';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-products',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './products.html',
  styleUrls: ['./products.css']
})
export class ProductsComponent implements OnInit {

  products: Product[] = [];

  newProduct: Product = {
    id: 0,
    name: '',
    price: 0,
    category: ''
  };

  editingProduct: Product | null = null;

  constructor(private productService: ProductService) {}

  ngOnInit() {
    this.loadProducts();
  }

  loadProducts() {
    this.productService.getProducts()
      .subscribe(data => this.products = data);
  }

  addProduct() {
    this.productService.addProduct(this.newProduct)
      .subscribe(() => {
        this.loadProducts();
        this.newProduct = { id: 0, name: '', price: 0, category: '' };
      });
  }

  deleteProduct(id: number) {
    this.productService.deleteProduct(id)
      .subscribe(() => this.loadProducts());
  }

  editProduct(product: Product) {
    this.editingProduct = { ...product };
  }

  updateProduct() {
    if (!this.editingProduct) return;

    this.productService.updateProduct(this.editingProduct)
      .subscribe(() => {
        const index = this.products.findIndex(p => p.id === this.editingProduct!.id);
        if (index !== -1) {
          this.products[index] = this.editingProduct!;
        }

        this.editingProduct = null;
      });
  }

}