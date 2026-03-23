using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DestinacijaController : ControllerBase
    {
        private readonly IDestinacijaService destservis;

        public DestinacijaController(IDestinacijaService servis)
        {
            destservis = servis;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Destinacija>>> GetAll()
        {
            return Ok(await destservis.GetAll());
        }

        [HttpGet("{id}")]

        public async Task<ActionResult<Destinacija>> GetByID(int id)
        {
            var destinacija = await destservis.GetById(id);
            if (destinacija == null) return NotFound();

            return Ok(destinacija);
        }

        [HttpPost]
        public async Task<ActionResult<Destinacija>> Create(Destinacija d)
        {
            var nova = await destservis.Create(d);
            return CreatedAtAction(nameof(GetByID), new { id = nova.Id }, nova);
        }

        [HttpPut("{id}")]

        public async Task<ActionResult<Destinacija>> Update(int id,Destinacija d)
        {
            var izmenjena = await destservis.Update(id,d);
            if(izmenjena == null) return NotFound();
            return Ok(izmenjena);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(int id)
        {
            var rez = await destservis.Delete(id);

            if(!rez) return NotFound();
            return NoContent();
        }
    }
}
