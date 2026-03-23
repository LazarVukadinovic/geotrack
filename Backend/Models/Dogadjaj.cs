namespace Backend.Models
{
    public class Dogadjaj
    {

        public int Id { get; set; }

        public int ObjekatID { get; set; }

        public Objekat? Objekat { get; set; }

        public required string TipDogadjaja { get; set; }

        public required string ImeDogadjaja { get; set; }

        public string? Opis { get; set; }
        
        public int? maxBrojLjudi { get; set; }
        public DateTime? DatumPocetka { get; set; }   
        public DateTime? DatumZavrsetka { get; set; }

        public ICollection<Korisnik> Prijavljeni { get; set; } = new List<Korisnik>();

        public ICollection<Aktivnost> AktivnostiNaDogadjaju { get; set; } = new List<Aktivnost>();
    }
}
