using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DemoProjekat.Models
{
    public class PaymentDetail
    {
        [Key]
        public int PaymentDetailId { get; set; }

        [Column(TypeName = "varchar(100)")]
        public string CardOwnerName { get; set; } = "";

        [Column(TypeName = "varchar(16)")]
        public string CardNumber { get; set; } = "";

        [Column(TypeName = "varchar(5)")] // mm/yy
        public string ExpirationDate { get; set; } = "";

        [Column(TypeName = "varchar(3)")]
        public string SecurityCode { get; set; } = "";
    }
}
