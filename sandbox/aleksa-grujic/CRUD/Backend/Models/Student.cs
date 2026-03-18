namespace Backend.Models
{
    public class Student
    {
        public int Id { get; set; }
        public string Ime { get; set; } = null!;
        public string Prezime { get; set; } = null!;
        public int Indeks { get; set; }
        public int Upisan { get; set; }


    }
}
