using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TremorScope.Web;
using TremorScope.Web.State;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddSingleton<BrowserIo>();
builder.Services.AddSingleton<Lab>();
await builder.Build().RunAsync();
