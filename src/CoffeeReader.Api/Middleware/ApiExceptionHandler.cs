using CoffeeReader.Application.Abstractions;
using CoffeeReader.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CoffeeReader.Api.Middleware;

/// <summary>
/// Turns application errors into RFC 7807 problem responses.
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            ValidationException ex => (StatusCodes.Status400BadRequest, "Please check your input", ex.Message),
            NotFoundException ex => (StatusCodes.Status404NotFound, "Not found", ex.Message),
            ConflictException ex => (StatusCodes.Status409Conflict, "Conflict", ex.Message),
            ForbiddenException ex => (StatusCodes.Status403Forbidden, "Not allowed", ex.Message),
            UnauthorizedException ex => (StatusCodes.Status401Unauthorized, "Sign in required", ex.Message),
            EbookFormatException ex => (StatusCodes.Status422UnprocessableEntity, "This book couldn't be opened", ex.Message),
            BadHttpRequestException ex => (ex.StatusCode, "Bad request", ex.Message),
            UnauthorizedAccessException => (StatusCodes.Status404NotFound, "Not found", "That file isn't available."),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => (499, "Cancelled", null),
            _ => (StatusCodes.Status500InternalServerError, "Something went wrong", null),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled error for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        if (status == 499)
        {
            return true;
        }

        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        if (exception is ValidationException validation && validation.Errors.Count > 0)
        {
            problem.Extensions["errors"] = validation.Errors;
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
