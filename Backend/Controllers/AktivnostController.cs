using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AktivnostController : ControllerBase
    {
        private readonly IAktivnost akservis;

        public AktivnostController(IAktivnost servis)
        {
            akservis = servis;
        }

        [HttpGet]

        public async Task<ActionResult<IEnumerable<Aktivnost>>>GetAll()
        {
            return Ok(await akservis.GetAll());
        }

        [HttpGet("{id}")]

        public async Task<ActionResult<Aktivnost>> GetByID(int id)
        { 
            var vrati = await akservis.GetById(id);
            if (vrati == null) return NotFound();
            return Ok(vrati);
        }

        [HttpPost]

        public async Task<ActionResult<Aktivnost>>Create(Aktivnost a)
        {
            var nova = await akservis.Create(a);
            if (nova == null) return BadRequest();
            return CreatedAtAction(nameof(GetByID), new { id = nova.Id }, nova);
        }

        [HttpPut("{id}")]

        public async Task<ActionResult<Aktivnost>> Update(int id, Aktivnost a)
        {
            var azuriraj = await akservis.Update(id, a);
            if(azuriraj == null)return NotFound();
            return Ok(azuriraj);
        }

        [HttpDelete("{id}")]

        public async Task<ActionResult> Delete(int id)
        {
            var rez = await akservis.Delete(id);
            if(!rez) return NotFound();
            return NoContent();
        }
    }
}
