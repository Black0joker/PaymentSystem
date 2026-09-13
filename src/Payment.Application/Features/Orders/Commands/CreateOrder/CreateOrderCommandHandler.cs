using MediatR;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Persistence;
using Payment.Application.Common;
using Payment.Domain.Entities;
using Payment.Domain.Exceptions;

namespace Payment.Application.Features.Orders.Commands.CreateOrder;

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Result<Guid>>
{
    private readonly IAppDbContext _context;

    public CreateOrderCommandHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<Guid>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // Validate user exists
        var userExists = await _context.Users
            .AnyAsync(u => u.Id == request.UserId, cancellationToken);

        if (!userExists)
            throw new NotFoundException(nameof(User), request.UserId);

        // Validate courses exist and get prices from server (never trust client price)
        var courseIds = request.Items.Select(i => i.CourseId).ToList();
        var courses = await _context.Courses
            .Where(c => courseIds.Contains(c.Id) && c.IsActive)
            .ToListAsync(cancellationToken);

        if (courses.Count != courseIds.Distinct().Count())
            return Result<Guid>.Failure("One or more courses not found or inactive.");

        // Create order
        var order = new Order(request.UserId, request.Currency);

        // Add items with server-side price
        foreach (var item in request.Items)
        {
            var course = courses.First(c => c.Id == item.CourseId);
            order.AddItem(course, item.Quantity);
        }

        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(order.Id);
    }
}
