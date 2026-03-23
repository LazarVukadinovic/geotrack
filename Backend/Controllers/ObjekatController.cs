using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ObjekatController : ControllerBase
    {
        private readonly IObjekatService objservis;

        public ObjekatController(IObjekatService servis)
        {
            objservis = servis;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Objekat>>> GetAll()
        {
            return Ok(await objservis.GetAll());
        }

        [HttpGet("{id}")]

        public async Task<ActionResult<Objekat>> GetByID(int id)
        {

            var vracen = await objservis.GetById(id);

            if (vracen == null) return NotFound();


            return Ok(vracen);

        }

        [HttpPost]
        public async Task<ActionResult<Objekat>> Create(Objekat o)
        {
            var novi = await objservis.Create(o);
            if (novi == null) return BadRequest();
            return CreatedAtAction(nameof(GetByID), new { id = novi.Id }, novi);
        }

        [HttpPut("{id}")]

        public async Task<ActionResult<Objekat>> Update(int id,Objekat o)
        {
            var azuz = await objservis.Update(id, o);
            if (azuz == null) return NotFound();
            return Ok(azuz);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult>Delete(int id)
        {
            var rez = await objservis.Delete(id);
            if(!rez) return NotFound();
            return NoContent();
        }
    }
}
