using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Persistence;
using Payment.Domain.Entities;

namespace Payment.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CoursesController : ControllerBase
{
    private readonly IAppDbContext _context;

    public CoursesController(IAppDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> CreateCourse([FromBody] CreateCourseRequest request, CancellationToken cancellationToken)
    {
        var course = new Course(request.Title, request.Description, request.Price, request.Currency ?? "USD");
        _context.Courses.Add(course);
        await _context.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetCourse), new { id = course.Id }, new { courseId = course.Id });
    }

    [HttpGet]
    public async Task<IActionResult> GetCourses(CancellationToken cancellationToken)
    {
        var courses = await _context.Courses
            .Where(c => c.IsActive)
            .Select(c => new { c.Id, c.Title, c.Description, c.Price, c.Currency })
            .ToListAsync(cancellationToken);

        return Ok(courses);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetCourse(Guid id, CancellationToken cancellationToken)
    {
        var course = await _context.Courses
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (course is null)
            return NotFound(new { error = "Course not found." });

        return Ok(new { course.Id, course.Title, course.Description, course.Price, course.Currency });
    }
}

public record CreateCourseRequest(string Title, string Description, decimal Price, string? Currency = "USD");
