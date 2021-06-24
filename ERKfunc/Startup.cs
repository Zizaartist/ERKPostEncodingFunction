using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(ERKfunc.Startup))]

namespace ERKfunc
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddOptions<ConfigWrapper>()
                        .Configure<IConfiguration>((settings, configuration) =>
                        {
                            configuration.GetSection("ConfigWrapper").Bind(settings);
                        });


        }
    }
}