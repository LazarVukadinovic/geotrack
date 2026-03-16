using System.ComponentModel.DataAnnotations.Schema;

namespace storeAPI.Models
{
    [Table("products")]
    public class Product
    {
        [Column("id")]        
        public int Id { get; set; }
        [Column("name")]
        public string Name { get; set; }
        [Column("price")]
        public decimal Price { get; set; }
        [Column("category")]
        public string Category { get; set; }
    }
}
