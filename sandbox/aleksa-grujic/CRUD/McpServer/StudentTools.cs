using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using MySql.Data.MySqlClient;
using System.ComponentModel;
using System.Text.Json;

namespace McpServer
{
    [McpServerToolType]
    internal class StudentTools
    {
        private const string ConnectionString = "Server=localhost;Port=3306;Database=FakultetDB;User=root;Password=Gruja123!;";

        [McpServerTool, Description("Vraca listu svih studenata iz baze podataka")]
        public static async Task<string> GetStudenti()
        {
            var studenti = new List<object>();
            using var conn = new MySqlConnection(ConnectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand("SELECT * FROM Studenti", conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                studenti.Add(new
                {
                    id = reader["Id"],
                    ime = reader["Ime"],
                    prezime = reader["Prezime"],
                    indeks = reader["Indeks"],
                    upisan = reader["Upisan"]
                });
            }
            return JsonSerializer.Serialize(studenti);
        }

        [McpServerTool, Description("Dodaje novog studenta u bazu podataka")]
        public static async Task<string> DodajStudenta(
            [Description("Ime studenta")] string ime,
            [Description("Prezime studenta")] string prezime,
            [Description("Broj indeksa studenta")] int indeks,
            [Description("Godina upisa studenta")] int upisan)
        {
            using var conn = new MySqlConnection(ConnectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(
                "INSERT INTO Studenti (Ime, Prezime, Indeks, Upisan) VALUES (@ime, @prezime, @indeks, @upisan)", conn);
            cmd.Parameters.AddWithValue("@ime", ime);
            cmd.Parameters.AddWithValue("@prezime", prezime);
            cmd.Parameters.AddWithValue("@indeks", indeks);
            cmd.Parameters.AddWithValue("@upisan", upisan);
            await cmd.ExecuteNonQueryAsync();
            return "Student uspesno dodat!";
        }

        [McpServerTool, Description("Brise studenta iz baze podataka po ID-u")]
        public static async Task<string> ObrisiStudenta(
            [Description("ID studenta kojeg treba obrisati")] int id)
        {
            using var conn = new MySqlConnection(ConnectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand("DELETE FROM Studenti WHERE Id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            int affected = await cmd.ExecuteNonQueryAsync();
            if (affected == 0)
                return $"Student sa ID {id} nije pronadjen!";
            return $"Student sa ID {id} je uspesno obrisan!";
        }

        [McpServerTool, Description("Izmena podataka postojeceg studenta po ID-u")]
        public static async Task<string> IzmeniStudenta(
            [Description("ID studenta kojeg treba izmeniti")] int id,
            [Description("Novo ime studenta")] string ime,
            [Description("Novo prezime studenta")] string prezime,
            [Description("Novi broj indeksa")] int indeks,
            [Description("Nova godina upisa")] int upisan)
        {
            using var conn = new MySqlConnection(ConnectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(
                "UPDATE Studenti SET Ime=@ime, Prezime=@prezime, Indeks=@indeks, Upisan=@upisan WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@ime", ime);
            cmd.Parameters.AddWithValue("@prezime", prezime);
            cmd.Parameters.AddWithValue("@indeks", indeks);
            cmd.Parameters.AddWithValue("@upisan", upisan);
            int affected = await cmd.ExecuteNonQueryAsync();
            if (affected == 0)
                return $"Student sa ID {id} nije pronadjen!";
            return $"Student sa ID {id} je uspesno izmenjen!";
        }

        [McpServerTool, Description("Trazi studente po imenu ili prezimenu")]
        public static async Task<string> TraziStudenta(
            [Description("Ime ili prezime studenta za pretragu")] string pretraga)
        {
            var studenti = new List<object>();
            using var conn = new MySqlConnection(ConnectionString);
            await conn.OpenAsync();
            using var cmd = new MySqlCommand(
                "SELECT * FROM Studenti WHERE Ime LIKE @p OR Prezime LIKE @p", conn);
            cmd.Parameters.AddWithValue("@p", $"%{pretraga}%");
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                studenti.Add(new
                {
                    id = reader["Id"],
                    ime = reader["Ime"],
                    prezime = reader["Prezime"],
                    indeks = reader["Indeks"],
                    upisan = reader["Upisan"]
                });
            }
            if (studenti.Count == 0)
                return $"Nije pronadjen nijedan student za pretragu: {pretraga}";
            return JsonSerializer.Serialize(studenti);
        }
    }
}
