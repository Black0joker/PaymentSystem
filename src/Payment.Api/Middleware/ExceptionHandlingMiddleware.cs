using System.Text.Json;
using Payment.Application.Abstractions.Payments;
using Payment.Domain.Exceptions;

namespace Payment.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, exception.Message),
            InvalidStateTransitionException => (StatusCodes.Status409Conflict, exception.Message),
            WebhookVerificationException => (StatusCodes.Status400BadRequest, exception.Message),
            FluentValidation.ValidationException ve => (StatusCodes.Status400BadRequest,
                string.Join("; ", ve.Errors.Select(e => e.ErrorMessage))),
            ArgumentException => (StatusCodes.Status400BadRequest, exception.Message),
            _ => (StatusCodes.Status500InternalServerError, "An internal server error occurred.")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        }
        else
        {
            _logger.LogWarning(exception, "Handled exception ({StatusCode}): {Message}", statusCode, exception.Message);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        var response = new
        {
            error = message,
            statusCode
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
