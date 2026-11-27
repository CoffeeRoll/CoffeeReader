namespace CoffeeReader.Api.Middleware;

internal static class SecurityHeaders
{
    /// <summary>
    /// The policy for the web app. The reader renders book chapters in a same-origin iframe, which inherits
    /// this policy, so no script from a book can ever run.
    /// </summary>
    public const string AppPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "font-src 'self' data:; connect-src 'self'; media-src 'self'; frame-src 'self'; object-src 'none'; " +
        "base-uri 'self'; form-action 'self'; frame-ancestors 'self'; manifest-src 'self'";

    /// <summary>Files embedded in books are inert if opened directly (e.g. an SVG with a script in it).</summary>
    public const string BookFilePolicy = "default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; font-src 'self'; sandbox";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "same-origin";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                if (!headers.ContainsKey("Content-Security-Policy"))
                {
                    headers.ContentSecurityPolicy = AppPolicy;
                }

                return Task.CompletedTask;
            });
            await next();
        });

    /// <summary>
    /// Requires a custom header on state-changing API calls. Browsers won't send one cross-site without a
    /// CORS preflight, which this app never approves — a second line of defence after SameSite cookies.
    /// </summary>
    public static IApplicationBuilder UseCsrfHeaderCheck(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (request.Path.StartsWithSegments("/api")
                && !HttpMethods.IsGet(request.Method)
                && !HttpMethods.IsHead(request.Method)
                && !HttpMethods.IsOptions(request.Method)
                && !string.Equals(request.Headers["X-Requested-With"], "CoffeeReader", StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { title = "Missing request header", status = 400 });
                return;
            }

            await next();
        });
}
