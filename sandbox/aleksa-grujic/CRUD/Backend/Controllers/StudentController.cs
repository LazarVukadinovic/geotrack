using Microsoft.AspNetCore.Mvc;
using Backend.Models;
using Backend.Services;

namespace Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StudentController : ControllerBase
    {
        private readonly IServices studentService;

        public StudentController(IServices studentService)
        {
            this.studentService = studentService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Student>>> getAll()
        {
            var rezultat = await studentService.getStudenti();
            return Ok(rezultat);
        }

        [HttpPost]
        public async Task<ActionResult> Create(Student s)
        {
            await studentService.Dodaj(s);
            return Ok("Student uspesno dodat!");
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(int id)
        {
            await studentService.Izbrisi(id);
            return Ok("Student uspesno obrisan!");
        }

        [HttpPut("{id}")]
        public async Task<ActionResult> Update(int id, Student s)
        {
            s.Id = id;
            await studentService.Azuriraj(s);
            return Ok("Student uspesno izmenjen!");
        }
    }
}
