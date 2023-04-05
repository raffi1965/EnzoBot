using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Adaptive;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Input;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Actions;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Conditions;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Templates;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Generators;
using Microsoft.Bot.Builder.LanguageGeneration;
using Microsoft.Extensions.Configuration;
using Microsoft.Bot.Builder.AI.Luis;
using AdaptiveExpressions.Properties;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Builder.Dialogs.Adaptive.Recognizers;
using Microsoft.Bot.Builder.AI.QnA.Recognizers;
using AdaptiveExpressions;
using Microsoft.Bot.Builder;

namespace JDBots.Dialogs.DeleteToDoDialog
{
    public class DeleteToDoDialog : ComponentDialog
    {
        private AdaptiveDialog _deleteToDoDialog;
        public DeleteToDoDialog(IConfiguration configuration)
            : base(nameof(DeleteToDoDialog))
        {
            string[] paths = { ".", "Dialogs", "DeleteToDoDialog", "DeleteToDoDialog.lg" };
            string fullPath = Path.Combine(paths);
            
            _deleteToDoDialog = new AdaptiveDialog(nameof(DeleteToDoDialog))
            {
                Generator = new TemplateEngineLanguageGenerator(Templates.ParseFile(fullPath)),
                Recognizer = CreateCrossTrainedRecognizer(configuration),
                Triggers = new List<OnCondition>()
                {
                    new OnBeginDialog() 
                    {
                        Actions = new List<Dialog>() 
                        {
                            new IfCondition()
                            {
                                // All conditions are expressed using adaptive expressions.
                                // See https://aka.ms/adaptive-expressions to learn more
                                Condition = "count(user.lists.todo) == 0 && count(user.lists.grocery) == 0 && count(user.lists.shopping) == 0",
                                Actions = new List<Dialog>()
                                {
                                    new SendActivity("${DeleteEmptyList()}"),
                                    new EndDialog()
                                }
                            },

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

                            new IfCondition()
                            {
                                Condition = "count(user.lists[dialog.listType]) == 0",
                                Actions = new List<Dialog>()
                                {
                                    new SendActivity("${NoItemsInList()}"),
                                    new EndDialog()
                                }
                            },

                            new ChoiceInput()
                            {
                                Choices = "user.lists[dialog.listType]",
                                Property = "dialog.itemTitle",
                                OutputFormat = ChoiceOutputFormat.Value,
                                Style = ListStyle.List,
                                Prompt = new ActivityTemplate("${GetItemTitleToDelete()}")
                            },

                            new EditArray()
                            {
                                ItemsProperty = "user.lists[dialog.listType]",
                                Value = "=dialog.itemTitle",
                                ChangeType = EditArray.ArrayChangeType.Remove
                            },

                            new SendActivity("${DeleteConfirmationReadBack()}")
                        }
                    },
                    new OnDialogEvent()
                    {
                        Event = AdaptiveEvents.RecognizedIntent,
                        Condition = "#GetTitleToDelete || #GetListType",
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

            AddDialog(this._deleteToDoDialog);

            InitialDialogId = nameof(DeleteToDoDialog);
        }

        private async Task<DialogTurnResult> ResolveAndSendQnAAnswer(DialogContext dc, System.Object options)
        {
            var exp1 = Expression.Parse("@answer").TryEvaluate(dc.State).value;
            var resVal = await this._deleteToDoDialog.Generator.GenerateAsync(dc, exp1.ToString(), dc.State);
            await dc.Context.SendActivityAsync(ActivityFactory.FromObject(resVal));
            return await dc.EndDialogAsync(options);
        }

        private async Task<DialogTurnResult> GetToDoTitleToDelete(DialogContext dc, System.Object options)
        {
            var todoList = dc.State.GetValue<string[]>("user.todos");
            string todoTitleStr = null;
            string[] todoTitle;

            dc.State.TryGetValue("turn.entities.todoTitle", out todoTitle);
            if (todoTitle != null && todoTitle.Length != 0)
            {
                if (Array.Exists(todoList, e => e == todoTitle[0])) {
                    todoTitleStr = todoTitle[0];
                }
            }
            if (todoTitleStr != null)
            {
                dc.State.SetValue("turn.todoTitle", todoTitleStr);
            }
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
            if (string.IsNullOrEmpty(configuration["qna:DeleteToDoDialog_en_us_qna"]) || string.IsNullOrEmpty(configuration["QnAHostName"]) || string.IsNullOrEmpty(configuration["QnAEndpointKey"]))
            {
                throw new Exception("NOTE: QnA Maker is not configured for DeleteToDoDialog. Please follow instructions in README.md to add 'qna:DeleteToDoDialog_en_us_qna', 'QnAHostName' and 'QnAEndpointKey' to the appsettings.json file.");
            }

            return new QnAMakerRecognizer()
            {
                HostName = configuration["QnAHostName"],
                EndpointKey = configuration["QnAEndpointKey"],
                KnowledgeBaseId = configuration["qna:DeleteToDoDialog_en_us_qna"],

                Context = "dialog.qnaContext",

                QnAId = "turn.qnaIdFromPrompt",

                LogPersonalInformation = false,

                IncludeDialogNameInMetadata = true,

                Id = $"QnA_{nameof(DeleteToDoDialog)}"
            };
        }
        private static Recognizer CreateLuisRecognizer(IConfiguration Configuration)
        {
            if (string.IsNullOrEmpty(Configuration["luis:DeleteToDoDialog_en_us_lu"]) || string.IsNullOrEmpty(Configuration["LuisAPIKey"]) || string.IsNullOrEmpty(Configuration["LuisAPIHostName"]))
            {
                throw new Exception("Your AddToDoDialog's LUIS application is not configured for AddToDoDialog. Please see README.MD to set up a LUIS application.");
            }
            return new LuisAdaptiveRecognizer()
            {
                Endpoint = Configuration["LuisAPIHostName"],
                EndpointKey = Configuration["LuisAPIKey"],
                ApplicationId = Configuration["luis:DeleteToDoDialog_en_us_lu"],

                Id = $"LUIS_{nameof(DeleteToDoDialog)}"
            };
        }
    }
}
