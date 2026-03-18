using Microsoft.AspNetCore.Mvc;
using storeAPI.Data;
using storeAPI.Models;

namespace storeAPI.Controllers
{
    [ApiController]
    [Route("products")]
    public class ProductController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProductController(AppDbContext context)
        {
            _context = context;
        }

        // READ - svi proizvodi
        [HttpGet]
        public IActionResult GetProducts()
        {
            return Ok(_context.Products.OrderBy(p => p.Id).ToList());
        }

        // READ - jedan proizvod
        [HttpGet("{id}")]
        public IActionResult GetProduct(int id)
        {
            var product = _context.Products.Find(id);

            if (product == null)
                return NotFound();

            return Ok(product);
        }

        // CREATE
        [HttpPost]
        public IActionResult CreateProduct(Product product)
        {
            _context.Products.Add(product);
            _context.SaveChanges();

            return Ok(product);
        }

        // UPDATE
        [HttpPut("{id}")]
        public IActionResult UpdateProduct(int id, Product updatedProduct)
        {
            var product = _context.Products.Find(id);

            if (product == null)
                return NotFound();

            product.Name = updatedProduct.Name;
            product.Price = updatedProduct.Price;
            product.Category = updatedProduct.Category;

            _context.SaveChanges();

            return Ok(product);
        }

        // DELETE
        [HttpDelete("{id}")]
        public IActionResult DeleteProduct(int id)
        {
            var product = _context.Products.Find(id);

            if (product == null)
                return NotFound();

            _context.Products.Remove(product);
            _context.SaveChanges();

            return Ok();
        }
    }
}
