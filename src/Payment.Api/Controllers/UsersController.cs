using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Persistence;
using Payment.Domain.Entities;

namespace Payment.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IAppDbContext _context;

    public UsersController(IAppDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = new User(request.Email, request.FirstName, request.LastName);
        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, new { userId = user.Id });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(Guid id, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null)
            return NotFound(new { error = "User not found." });

        return Ok(new { user.Id, user.Email, user.FirstName, user.LastName });
    }
}

public record CreateUserRequest(string Email, string FirstName, string LastName);
