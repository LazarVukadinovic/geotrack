using System.Runtime.CompilerServices;
using System.ComponentModel.DataAnnotations;
namespace Backend.Models
{
    public class Destinacija
    {
        public int Id { get; set; }
        
        public required string Ime { get; set; }

        public required string Tip { get; set; }

        public string? Slika { get; set; } // slika destinacije

        public string? Opis { get; set; }

        public double? Xkoordinata { get; set; } 
        public double? Ykoordinata { get; set; }

        public ICollection<Objekat> Objekti { get; set; } = new List<Objekat>();
        public ICollection<Aktivnost> Aktivnosti { get; set; } = new List<Aktivnost>();

    }
}
