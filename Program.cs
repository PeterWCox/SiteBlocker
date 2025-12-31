var builder = WebApplication.CreateBuilder(args);

// Get port from command line args or default to 80
var port = 80;
if (args.Length > 0 && int.TryParse(args[0], out var parsedPort))
{
    port = parsedPort;
}

// Get HTML file path from args or use default
var htmlFilePath = args.Length > 1 ? args[1] : "blocked.html";

// Bind to all interfaces on the specified port
builder.WebHost.UseUrls($"http://*:{port}");

var app = builder.Build();

// Serve the blocked.html page for all requests (any path, any method)
app.Use(async (HttpContext context, Func<Task> next) =>
{
    var htmlContent = System.IO.File.Exists(htmlFilePath)
        ? await System.IO.File.ReadAllTextAsync(htmlFilePath)
        : "<html><body><h1>Focus Mode Active</h1><p>This site is blocked.</p></body></html>";
    
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(htmlContent);
});

app.Run();

