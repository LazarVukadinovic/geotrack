using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DogadjajController : ControllerBase
    {
        private readonly IDogadjajService dogservis;

        public DogadjajController(IDogadjajService servis)
        {
            dogservis = servis;
        }

        [HttpGet]

        public async Task<ActionResult<IEnumerable<Dogadjaj>>>GetAll()
        {
            return Ok(await dogservis.GetAll());
        }

        [HttpGet("{id}")]

        public async Task<ActionResult<Dogadjaj>>GetByID(int id)
        {
            var vracen = await dogservis.GetById(id);
            if(vracen == null)return NotFound();
            return Ok(vracen);
        }

        [HttpPost]

        public async Task<ActionResult<Dogadjaj>> Create(Dogadjaj d)
        {
            var nov = await dogservis.Create(d);
            if (nov == null) return BadRequest();
            return CreatedAtAction(nameof(GetByID), new { id = nov.Id }, nov);
        }

        [HttpPut("{id}")]

        public async Task<ActionResult<Dogadjaj>> Update(int id,Dogadjaj d)
        { 
            var azurirano = await dogservis.Update(id, d);

            if(azurirano == null) return NotFound();
            return Ok(azurirano);
        }

        [HttpDelete("{id}")]

        public async Task<ActionResult> Delete(int id)
        {
            var rez = await dogservis.Delete(id);
            if(!rez) return NotFound();
            return NoContent();
        }

    }
}
