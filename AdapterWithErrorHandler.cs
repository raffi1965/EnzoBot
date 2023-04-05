using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.ApplicationInsights.Core;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Builder.LanguageGeneration;
using Microsoft.Bot.Builder.TraceExtensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;

namespace JDBots
{
    public class AdapterWithErrorHandler : CloudAdapter
    {
        private Templates _templates;
        private IBotTelemetryClient _adapterBotTelemetryClient;

        public AdapterWithErrorHandler
        (
            IConfiguration configuration, 
            IHttpClientFactory httpClientFactory, 
            ILogger<IBotFrameworkHttpAdapter> logger, 
            IStorage storage,
            UserState userState,
            TelemetryInitializerMiddleware telemetryInitializerMiddleware,
            IBotTelemetryClient botTelemetryClient,
            ConversationState conversationState
        ) : base(configuration, httpClientFactory, logger)
        {
            _adapterBotTelemetryClient = botTelemetryClient;

            Use(telemetryInitializerMiddleware);

            this.UseStorage(storage);
            this.UseBotState(userState);
            this.UseBotState(conversationState);

            string[] paths = { ".", $"{nameof(AdapterWithErrorHandler)}.lg" };
            string fullPath = Path.Combine(paths);
            _templates = Templates.ParseFile(fullPath);

            OnTurnError = async (turnContext, exception) =>
            {
                logger.LogError($"Exception caught : {exception.Message}");

                await turnContext.SendActivityAsync(ActivityFactory.FromObject(_templates.Evaluate("SomethingWentWrong", exception)));

                if (conversationState != null)
                {
                    try
                    {
                        await conversationState.DeleteAsync(turnContext);
                    }
                    catch (Exception e)
                    {
                        logger.LogError($"Exception caught on attempting to Delete ConversationState : {e.Message}");
                    }
                }
                //await turnContext.TraceActivityAsync("OnTurnError Trace", exception.Message, "https://www.botframework.com/schemas/error", "TurnError");
            };
        }
    }
}
