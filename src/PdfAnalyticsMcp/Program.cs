using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PdfAnalyticsMcp.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

builder.Services.AddSingleton<IInputValidationService, InputValidationService>();
builder.Services.AddSingleton<IPdfInfoService, PdfInfoService>();
builder.Services.AddSingleton<IPageTextService, PageTextService>();
builder.Services.AddSingleton<IPageGraphicsService, PageGraphicsService>();
builder.Services.AddSingleton<IPageImagesService, PageImagesService>();
builder.Services.AddSingleton<IRenderPagePreviewService, RenderPagePreviewService>();

await builder.Build().RunAsync();
