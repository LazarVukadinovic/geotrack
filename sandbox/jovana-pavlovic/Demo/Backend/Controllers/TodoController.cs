using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Backend.Data;
using Backend.Models;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TodoController : ControllerBase
{
    private readonly AppDBContext _context;

    public TodoController(AppDBContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(_context.Todos.ToList());
    }

    [HttpPost]
    public IActionResult Add(Todo todo)
    {
        _context.Todos.Add(todo);
        _context.SaveChanges();
        return Ok(todo);
    }
    [HttpPut("{id}")]
    public IActionResult Update(int id, Todo updatedTodo)
    {
        var todo = _context.Todos.Find(id);

        if (todo == null)
            return NotFound();

        todo.Text = updatedTodo.Text;

        _context.SaveChanges();

        return Ok(todo);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var todo = _context.Todos.Find(id);

        if (todo == null)
            return NotFound();

        _context.Todos.Remove(todo);
        _context.SaveChanges();

        return Ok();
    }
}
