using AdaptiveExpressions;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Bot.Builder.AI.QnA.Recognizers;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Adaptive;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Actions;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Conditions;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Generators;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Input;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Recognizers;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Templates;
using Microsoft.Bot.Builder.LanguageGeneration;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace JDBots.Dialogs.RootDialog
{
    public class RootDialog : ComponentDialog
    {
        private static IConfiguration Configuration;
        private AdaptiveDialog _rootDialog;

        public RootDialog(IConfiguration configuration, IBotTelemetryClient telemetryClient) : base(nameof(RootDialog))
        {
            Configuration = configuration;
            TelemetryClient = telemetryClient; 

            string[] paths = { ".", "Dialogs", "RootDialog", "RootDialog.lg" };
            string fullPath = Path.Combine(paths);

            _rootDialog = new AdaptiveDialog(nameof(RootDialog))
            {
                // Add a generator. This is how all Language Generation constructs specified for this dialog are resolved.
                Generator = new TemplateEngineLanguageGenerator(Templates.ParseFile(fullPath)),
                // Create a LUIS recognizer.
                // The recognizer is built using the intents, utterances, patterns and entities defined in ./RootDialog.lu file
                Recognizer = CreateCrossTrainedRecognizer(configuration),
                Triggers = new List<OnCondition>()
                {
                    // Add a rule to welcome user
                    new OnConversationUpdateActivity()
                    {
                        Actions = WelcomeUserSteps(),
                    },
                    new OnMessageActivity()
                    {
                         Actions = new List<Dialog>() {
                             new SendActivity("${turn.activity.text}")
                         }
                    },

                    // Intent rules for the LUIS model. Each intent here corresponds to an intent defined in ./Dialogs/Resources/ToDoBot.lu file
                    new OnIntent("Greeting")
                    {
                        Actions = new List<Dialog>()
                        {
                            new SendActivity("${HelpRootDialog()}"),
                        },
                    },
                    new OnIntent("AddItem")
                    {
                        Condition = "#AddItem.Score >= 0.5",
                        Actions = new List<Dialog>() 
                        {
                            new BeginDialog(nameof(AddToDoDialog)),
                        },
                    },
                    new OnIntent("DeleteItem") 
                    {
                        Condition = "#DeleteItem.Score >= 0.5",
                        Actions = new List<Dialog>() 
                        {
                            new BeginDialog(nameof(DeleteToDoDialog)),
                        },
                    },
                    new OnIntent("ViewItem")
                    {
                        Condition = "#ViewItem.Score >= 0.5",
                        Actions = new List<Dialog>() 
                        {
                            new BeginDialog(nameof(ViewToDoDialog)),
                        },
                    },
                    new OnIntent("GetUserProfile")
                    {
                        Condition = "#GetUserProfile.Score >= 0.5",
                        Actions = new List<Dialog>()
                        {
                             new BeginDialog(nameof(GetUserProfileDialog)),
                        },
                    },
                    new OnIntent("Cancel")
                    {
                        Condition = "#Cancel.Score >= 0.8",
                        Actions = new List<Dialog>() 
                        {
                            new ConfirmInput()
                            {
                                Prompt = new ActivityTemplate("${Cancel.prompt()}"),
                                Property = "turn.confirm",
                                Value = "=@confirmation",
                                AllowInterruptions = "!@confirmation",
                            },
                            new IfCondition()
                            {
                                Condition = "turn.confirm == true",
                                Actions = new List<Dialog>()
                                {
                                    new SendActivity("Cancelling all dialogs.."),
                                    new SendActivity("${WelcomeActions()}"),
                                    new CancelAllDialogs(),
                                },
                                ElseActions = new List<Dialog>()
                                {
                                    new SendActivity("${CancelCancelled()}"),
                                    new SendActivity("${WelcomeActions()}"),
                                },
                            },
                        },
                    },
                    new OnQnAMatch
                    {
                        Actions = new List<Dialog>()
                        {
                            new CodeAction(this.ResolveAndSendQnAAnswer)
                        },
                    },
                    new OnChooseIntent()
                    {
                        Actions = new List<Dialog>()
                        {
                            new SetProperties()
                            {
                                Assignments = new List<PropertyAssignment>()
                                {
                                    new PropertyAssignment()
                                    {
                                        Property = "dialog.luisResult",
                                        Value = $"=jPath(turn.recognized, \"$.candidates[?(@.id == 'LUIS_{nameof(RootDialog)}')]\")"
                                    },
                                    new PropertyAssignment()
                                    {
                                        Property = "dialog.qnaResult",
                                        Value = $"=jPath(turn.recognized, \"$.candidates[?(@.id == 'QnA_{nameof(RootDialog)}')]\")"
                                    },
                                }
                            },

                            new IfCondition()
                            {
                                Condition = "dialog.luisResult.score >= 0.9 && dialog.qnaResult.score <= 0.5",
                                Actions = new List<Dialog>()
                                {
                                    new EmitEvent()
                                    {
                                        EventName = AdaptiveEvents.RecognizedIntent,
                                        EventValue = "=dialog.luisResult.result"
                                    },
                                    new BreakLoop()
                                }
                            },

                            new IfCondition()
                            {
                                Condition = "dialog.luisResult.score <= 0.5 && dialog.qnaResult.score >= 0.9",
                                Actions = new List<Dialog>()
                                {
                                    new EmitEvent()
                                    {
                                        EventName = AdaptiveEvents.RecognizedIntent,
                                        EventValue = "=dialog.qnaResult.result"
                                    },
                                    new BreakLoop()
                                }
                            },

                            new IfCondition()
                            {
                                Condition = "dialog.qnaResult.score >= 0.95",
                                Actions = new List<Dialog>()
                                {
                                    new EmitEvent()
                                    {
                                        EventName = AdaptiveEvents.RecognizedIntent,
                                        EventValue = "=dialog.qnaResult.result"
                                    },
                                    new BreakLoop()
                                }
                            },

                            new IfCondition()
                            {
                                Condition = "dialog.qnaResult.score <= 0.05",
                                Actions = new List<Dialog>()
                                {
                                    new EmitEvent()
                                    {
                                        EventName = AdaptiveEvents.RecognizedIntent,
                                        EventValue = "=dialog.luisResult.result"
                                    },
                                    new BreakLoop()
                                }
                            },

                            new TextInput()
                            {
                                Property = "turn.intentChoice",
                                Prompt = new ActivityTemplate("${chooseIntentResponseWithCard()}"),
                                Value = "=@userChosenIntent",
                                AlwaysPrompt = true,
                                AllowInterruptions = false
                            },

                            new IfCondition()
                            {
                                Condition = "turn.intentChoice != 'none'",
                                Actions = new List<Dialog>()
                                {
                                    new EmitEvent()
                                    {
                                        EventName = AdaptiveEvents.RecognizedIntent,
                                        EventValue = "=dialog[turn.intentChoice].result"
                                    }
                                },
                                ElseActions = new List<Dialog>()
                                {
                                    new SendActivity()
                                    {
                                        Activity = new ActivityTemplate("Sure, no worries.")
                                    }
                                },
                            },
                        },
                    },
                },
            };

            AddDialog(_rootDialog);

            AddDialog(new AddToDoDialog.AddToDoDialog(configuration));
            AddDialog(new DeleteToDoDialog.DeleteToDoDialog(configuration));
            AddDialog(new ViewToDoDialog.ViewToDoDialog(configuration));
            AddDialog(new GetUserProfileDialog.GetUserProfileDialog(configuration));

            InitialDialogId = nameof(RootDialog);
        }

        private async Task<DialogTurnResult> ResolveAndSendQnAAnswer(DialogContext dc, System.Object options)
        {
            var exp1 = Expression.Parse("@answer").TryEvaluate(dc.State).value;
            var resVal = await this._rootDialog.Generator.GenerateAsync(dc, exp1.ToString(), dc.State);
            await dc.Context.SendActivityAsync(ActivityFactory.FromObject(resVal));
            return await dc.EndDialogAsync(options);
        }

        private static List<Dialog> WelcomeUserSteps()
        {
            return new List<Dialog>()
            {
                new Foreach()
                {
                    ItemsProperty = "turn.activity.membersAdded",
                    Actions = new List<Dialog>()
                    {
                        new IfCondition()
                        {
                            Condition = "$foreach.value.name != turn.activity.recipient.name",
                            Actions = new List<Dialog>()
                            {
                                new SendActivity("${IntroMessage()}"),
                                new SetProperty()
                                {
                                    Property = "user.lists",
                                    Value = "={todo : [], grocery : [], shopping : []}"
                                }
                            }
                        }
                    }
                }
            };
        }

        private static Recognizer CreateCrossTrainedRecognizer(IConfiguration configuration)
        {
            return new CrossTrainedRecognizerSet()
            {
                Recognizers = new List<Recognizer>()
                {
                    //CreateLuisRecognizer(configuration),
                    //CreateQnAMakerRecognizer(configuration)
                }
            };
        }

        private static Recognizer CreateQnAMakerRecognizer(IConfiguration configuration)
        {
            if (string.IsNullOrEmpty(configuration["qna:RootDialog_en_us_qna"]) || string.IsNullOrEmpty(configuration["QnAHostName"]) || string.IsNullOrEmpty(configuration["QnAEndpointKey"]))
            {
                throw new Exception("NOTE: QnA Maker is not configured for RootDialog. Please follow instructions in README.md to add 'qna:RootDialog_en_us_qna', 'QnAHostName' and 'QnAEndpointKey' to the appsettings.json file.");
            }

            return new QnAMakerRecognizer()
            {
                HostName = configuration["QnAHostName"],
                EndpointKey = configuration["QnAEndpointKey"],
                KnowledgeBaseId = configuration["qna:RootDialog_en_us_qna"],

                // property path that holds qna context
                Context = "dialog.qnaContext",

                // Property path where previous qna id is set. This is required to have multi-turn QnA working.
                QnAId = "turn.qnaIdFromPrompt",

                // Disable teletry logging
                LogPersonalInformation = false,

                // Enable to automatically including dialog name as meta data filter on calls to QnA Maker.
                IncludeDialogNameInMetadata = true,

                // Id needs to be QnA_<dialogName> for cross-trained recognizer to work.
                Id = $"QnA_{nameof(RootDialog)}"
            };
        }

        public static Recognizer CreateLuisRecognizer(IConfiguration Configuration)
        {
            if (string.IsNullOrEmpty(Configuration["luis:RootDialog_en_us_lu"]) || string.IsNullOrEmpty(Configuration["LuisAPIKey"]) || string.IsNullOrEmpty(Configuration["LuisAPIHostName"]))
            {
                throw new Exception("Your RootDialog LUIS application is not configured. Please see README.MD to set up a LUIS application.");
            }
            return new LuisAdaptiveRecognizer()
            {
                Endpoint = Configuration["LuisAPIHostName"],
                EndpointKey = Configuration["LuisAPIKey"],
                ApplicationId = Configuration["luis:RootDialog_en_us_lu"],

                Id = $"LUIS_{nameof(RootDialog)}"
            };
        }
    }
}
