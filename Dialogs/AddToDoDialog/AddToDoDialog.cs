using System.Collections.Generic;
using System.IO;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Adaptive;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Input;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Actions;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Conditions;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Templates;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Generators;
using Microsoft.Bot.Builder.LanguageGeneration;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Extensions.Configuration;
using System;
using AdaptiveExpressions.Properties;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Recognizers;
using Microsoft.Bot.Builder.AI.QnA.Recognizers;
using System.Threading.Tasks;
using AdaptiveExpressions;
using Microsoft.Bot.Builder;

namespace JDBots.Dialogs.AddToDoDialog
{
    public class AddToDoDialog : ComponentDialog
    {
        private static IConfiguration Configuration;
        private AdaptiveDialog _addToDoDialog;
        public AddToDoDialog(IConfiguration configuration)
            : base(nameof(AddToDoDialog))
        {
            Configuration = configuration;
            string[] paths = { ".", "Dialogs", "AddToDoDialog", "AddToDoDialog.lg" };
            string fullPath = Path.Combine(paths);
            _addToDoDialog = new AdaptiveDialog(nameof(AddToDoDialog))
            {
                Generator = new TemplateEngineLanguageGenerator(Templates.ParseFile(fullPath)),
                Recognizer = CreateCrossTrainedRecognizer(configuration),
                Triggers = new List<OnCondition>()
                {
                    new OnBeginDialog() 
                    {
                        Actions = new List<Dialog>()
                        {
                            new SetProperties()
                            {
                                Assignments = new List<PropertyAssignment>()
                                {
                                    new PropertyAssignment()
                                    {
                                        Property = "dialog.itemTitle",
                                        Value = "=@itemTitle"
                                    },
                                    new PropertyAssignment()
                                    {
                                        Property = "dialog.listType",
                                        Value = "=@listType"
                                    }
                                }
                            },
                            new TextInput()
                            {
                                Property = "dialog.itemTitle",
                                Prompt = new ActivityTemplate("${GetItemTitle()}"),
                                Value = "=@itemTitle",
                                AllowInterruptions = "!@itemTitle && turn.recognized.score >= 0.7"
                            },
                            new TextInput()
                            {
                                Property = "dialog.listType",
                                Prompt = new ActivityTemplate("${GetListType()}"),
                                Value = "=@listType",
                                AllowInterruptions = "!@listType && turn.recognized.score >= 0.7",
                                Validations = new List<BoolExpression>()
                                {
                                    "contains(createArray('todo', 'shopping', 'grocery'), toLower(this.value))",
                                },
                                OutputFormat = "=toLower(this.value)",
                                InvalidPrompt = new ActivityTemplate("${GetListType.Invalid()}"),
                                MaxTurnCount = 2,
                                DefaultValue = "todo",
                                DefaultValueResponse = new ActivityTemplate("${GetListType.DefaultValueResponse()}")
                            },
                            new EditArray()
                            {
                                ItemsProperty = "user.lists[dialog.listType]",
                                ChangeType = EditArray.ArrayChangeType.Push,
                                Value = "=dialog.itemTitle"
                            },
                            new SendActivity("${AddItemReadBack()}")
                        }
                    },
                    new OnIntent("Help")
                    {
                        Actions = new List<Dialog>()
                        {
                            new SendActivity("${HelpAddItem()}")
                        }
                    },
                    new OnDialogEvent()
                    {
                        Event = AdaptiveEvents.RecognizedIntent,
                        Condition = "#GetItemTitle || #GetListType",
                        Actions = new List<Dialog>()
                        {
                            new SetProperties()
                            {
                                Assignments = new List<PropertyAssignment>()
                                {
                                    new PropertyAssignment()
                                    {
                                        Property = "dialog.itemTitle",
                                        Value = "=@itemTitle"
                                    },
                                    new PropertyAssignment()
                                    {
                                        Property = "dialog.listType",
                                        Value = "=@listType"
                                    }
                                }
                            }
                        }
                    },
                    new OnQnAMatch
                    {
                        Actions = new List<Dialog>()
                        {
                            new CodeAction(ResolveAndSendQnAAnswer)
                        }
                    }
                }
            };

            AddDialog(_addToDoDialog);

            InitialDialogId = nameof(AddToDoDialog);
        }

        private async Task<DialogTurnResult> ResolveAndSendQnAAnswer(DialogContext dc, System.Object options)
        {
            var exp1 = Expression.Parse("@answer").TryEvaluate(dc.State).value;
            var resVal = await this._addToDoDialog.Generator.GenerateAsync(dc, exp1.ToString(), dc.State);
            await dc.Context.SendActivityAsync(ActivityFactory.FromObject(resVal));
            return await dc.EndDialogAsync(options);
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
            if (string.IsNullOrEmpty(configuration["qna:AddToDoDialog_en_us_qna"]) || string.IsNullOrEmpty(configuration["QnAHostName"]) || string.IsNullOrEmpty(configuration["QnAEndpointKey"]))
            {
                throw new Exception("NOTE: QnA Maker is not configured for AddToDoDialog. Please follow instructions in README.md to add 'qnamaker:AddToDoDialog_en_us_qna', 'QnAHostName' and 'QnAEndpointKey' to the appsettings.json file.");
            }

            return new QnAMakerRecognizer()
            {
                HostName = configuration["QnAHostName"],
                EndpointKey = configuration["QnAEndpointKey"],
                KnowledgeBaseId = configuration["qna:AddToDoDialog_en_us_qna"],

                Context = "dialog.qnaContext",

                QnAId = "turn.qnaIdFromPrompt",

                LogPersonalInformation = false,

                IncludeDialogNameInMetadata = true,

                Id = $"QnA_{nameof(AddToDoDialog)}"
            };
        }

        private static Recognizer CreateLuisRecognizer(IConfiguration Configuration)
        {
            if (string.IsNullOrEmpty(Configuration["luis:AddToDoDialog_en_us_lu"]) || string.IsNullOrEmpty(Configuration["LuisAPIKey"]) || string.IsNullOrEmpty(Configuration["LuisAPIHostName"]))
            {
                throw new Exception("Your AddToDoDialog's LUIS application is not configured for AddToDoDialog. Please see README.MD to set up a LUIS application.");
            }
            return new LuisAdaptiveRecognizer()
            {
                Endpoint = Configuration["LuisAPIHostName"],
                EndpointKey = Configuration["LuisAPIKey"],
                ApplicationId = Configuration["luis:AddToDoDialog_en_us_lu"],

                // Id needs to be LUIS_<dialogName> for cross-trained recognizer to work.
                Id = $"LUIS_{nameof(AddToDoDialog)}"
            };
        }
    }
}
