using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RecipeWarden.Web.Pages;

/// <summary>SEC-73: one generic error page with a correlation ID; never a stack trace.</summary>
[IgnoreAntiforgeryToken] // re-executed for POSTs that failed; changes no state
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorModel : PageModel
{
    public string CorrelationId { get; private set; } = string.Empty;

    public string Title { get; private set; } = "Something went wrong";

    public string Message { get; private set; } = "The request could not be completed.";

    public void OnGet(int? code) => Populate(code);

    public void OnPost(int? code) => Populate(code);

    private void Populate(int? code)
    {
        CorrelationId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
        var status = code ?? HttpContext.Response.StatusCode;
        (Title, Message) = status switch
        {
            StatusCodes.Status400BadRequest => ("Bad request", "The request was not valid. Reload the page and try again."),
            StatusCodes.Status403Forbidden => ("Access denied", "Your account does not have access to this page."),
            StatusCodes.Status404NotFound => ("Not found", "That page does not exist."),
            StatusCodes.Status429TooManyRequests => ("Slow down", "Too many requests. Wait a moment and try again."),
            _ => (Title, Message),
        };

        if (code is >= 400 and < 600)
        {
            Response.StatusCode = code.Value;
        }
    }
}
