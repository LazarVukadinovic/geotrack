namespace Backend.Models
{
    public class Objekat
    {
        public int Id { get; set; }
        public int DestinacijaID { get; set; }
        public Destinacija? Destinacija { get; set; }
        public required string ImeObjekta { get; set; }
        public required string TipObjekta { get; set; }
        public required string Adresa { get; set; }

        public string? Opis { get; set; }
        public double? Xkoordinata { get; set; }
        public double? YKoordinata { get; set; }

        public TimeOnly? OtvaranjeU { get; set; }
        public TimeOnly? ZatvaranjeU { get; set; }

        public int? kapacitet { get; set; }

        public ICollection<Dogadjaj>Dogadjaji { get; set; } = new List<Dogadjaj>();
        public ICollection<Aktivnost> Aktivnosti { get; set; } = new List<Aktivnost>();
    }
}
