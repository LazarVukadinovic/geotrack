namespace Backend.Models
{
    public class Aktivnost
    {
        public int Id { get; set; }

        public required string ImeAktivnosti { get; set; }

        public string? TipAktivnosti { get; set; }

        public string? Opis { get; set; }
        public int? DestinacijaID { get; set; } 
        public Destinacija? Destinacija { get; set; }
        public int? DogadjajID { get; set; } 
        public Dogadjaj? Dogadjaj { get; set; }
        public Objekat? Objekat { get; set; }
        public int? ObjekatID { get;set; }
    }
}
