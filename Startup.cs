using JDBots.Bots;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Bot.Builder.ApplicationInsights;
using Microsoft.Bot.Builder.BotFramework;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Adaptive;
using Microsoft.Bot.Builder.Integration.ApplicationInsights.Core;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


namespace JDBots
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient().AddMvc();

            ComponentRegistration.Add(new DialogsComponentRegistration());

            ComponentRegistration.Add(new AdaptiveComponentRegistration());

            ComponentRegistration.Add(new LanguageGenerationComponentRegistration());

            ComponentRegistration.Add(new LuisComponentRegistration());

            services.AddSingleton<ICredentialProvider, ConfigurationCredentialProvider>();

            services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();

            services.AddSingleton<IStorage, MemoryStorage>();

            services.AddSingleton<UserState>();

            services.AddSingleton<ConversationState>();

            services.AddSingleton<Dialogs.RootDialog.RootDialog>();

            services.AddSingleton<IBot, DialogBot<Dialogs.RootDialog.RootDialog>>();

            services.AddApplicationInsightsTelemetry();

            services.AddSingleton<IBotTelemetryClient, BotTelemetryClient>();

            services.AddSingleton<ITelemetryInitializer, OperationCorrelationTelemetryInitializer>();

            services.AddSingleton<ITelemetryInitializer, TelemetryBotIdInitializer>();

            services.AddSingleton<TelemetryInitializerMiddleware>();

            services.AddSingleton<TelemetryLoggerMiddleware>(sp =>
            {
                var telemetryClient = sp.GetService<IBotTelemetryClient>();
                return new TelemetryLoggerMiddleware(telemetryClient, logPersonalInformation: true);
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseDefaultFiles()
                .UseStaticFiles()
                .UseWebSockets()
                .UseRouting()
                .UseAuthorization()
                .UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
        }
    }
}
