import { Component, OnInit, signal } from '@angular/core';
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

  products = signal<Product[]>([]);

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
      .subscribe(data => this.products.set(data));
  }

  addProduct() {

    if (!this.newProduct.name.trim() ||
        !this.newProduct.category.trim() ||
        this.newProduct.price <= 0) {

      alert("Morate popuniti sva polja proizvoda!");
      return;
    }

    this.productService.addProduct(this.newProduct)
      .subscribe(() => {

        this.loadProducts();

        this.newProduct = {
          id: 0,
          name: '',
          price: 0,
          category: ''
        };

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

    if (!this.editingProduct.name.trim() ||
        !this.editingProduct.category.trim() ||
        this.editingProduct.price <= 0) {

      alert("Sva polja moraju biti popunjena!");
      return;
    }

    this.productService.updateProduct(this.editingProduct)
      .subscribe(() => {

        const updated = this.products().map(p =>
          p.id === this.editingProduct!.id ? this.editingProduct! : p
        );

        this.products.set(updated);

        this.editingProduct = null;

      });

  }

}