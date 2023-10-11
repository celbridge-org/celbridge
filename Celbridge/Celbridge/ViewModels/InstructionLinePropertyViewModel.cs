using Celbridge.Services;
using Celbridge.Tasks;
using Celbridge.Utils;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI;
using Microsoft.UI.Xaml.Documents;
using Newtonsoft.Json;
using Windows.UI;

namespace Celbridge.ViewModels
{
    public partial class InstructionLinePropertyViewModel : RecordSummaryPropertyViewModel
    {
        const double IndentDistance = 20;

        private ICelScriptService _celScriptService;

        private InstructionLine? _instructionLine;
        private InstructionLine InstructionLine
        {
            get
            {
                if (_instructionLine != null)
                {
                    return _instructionLine;
                }

                Guard.IsNotNull(Property);
                var propertyInfo = Property.PropertyInfo;

                if (Property.CollectionType != null)
                {
                    var list = propertyInfo.GetValue(Property.Object) as List<InstructionLine>;
                    Guard.IsNotNull(list);
                    Guard.IsTrue(ItemIndex < list.Count);
                    _instructionLine = list[ItemIndex];
                }
                else
                {
                    _instructionLine = propertyInfo.GetValue(Property.Object) as InstructionLine;
                }
                Guard.IsNotNull(_instructionLine);

                return _instructionLine;
            }
        }

        public string Keyword
        {
            get
            {
                return InstructionLine.Keyword;
            }

            set
            {
                if (value.Equals(Keyword))
                {
                    return;
                }

                InstructionLine.Keyword = value;

                OnPropertyChanged(nameof(Keyword));
                OnPropertyChanged(nameof(Description));
            }
        }

        [ObservableProperty]
        private double _indentWidth;

        [ObservableProperty]
        private SolidColorBrush _keywordColor = new SolidColorBrush(Colors.White);

        [ObservableProperty]
        private string _tooltip = string.Empty;

        private int _cachedinstructionHash;

        private string? _previousInstructionJson;

        public InstructionLinePropertyViewModel(IMessenger messengerService, ICelScriptService celScriptService) : base(messengerService)
        {
            _celScriptService = celScriptService;

            PropertyChanged += ViewModel_PropertyChanged;
        }

        private void OnSyntaxFormatUpdated(object recipient, CelSyntaxFormatUpdatedMessage message)
        {
            var celSyntaxFormat = message.CelSyntaxFormat;

            // Use the appropriate syntax format list for InputValues, OutputValues, Instructions, etc.
            List<SyntaxFormat> syntaxFormatList;

            Guard.IsNotNull(Property);
            if (Property.PropertyInfo.Name == nameof(ICel.Instructions))
            {
                syntaxFormatList = celSyntaxFormat.InstructionSyntaxFormat;
            }
            else if (Property.PropertyInfo.Name == nameof(ICel.Input))
            {
                syntaxFormatList = celSyntaxFormat.InputSyntaxFormat;
            }
            else if (Property.PropertyInfo.Name == nameof(ICel.Output))
            {
                syntaxFormatList = celSyntaxFormat.OutputSyntaxFormat;
            }
            else
            {
                throw new ArgumentException();
            }

            if (ItemIndex >= syntaxFormatList.Count)
            {
                // Reset to a sensible default until we get another syntax format update
                IndentWidth = 0;
                return;
            }

            // Lookup the syntax info for this line number
            var syntaxFormat = syntaxFormatList[ItemIndex];

            // Apply syntax formatting
            IndentWidth = syntaxFormat.IndentLevel * IndentDistance;
            if (syntaxFormat.PipeState == PipeState.PipeProducer)
            {
                IndentWidth += IndentDistance;
            }

            // Apply keyword color
            Color syntaxColor = Colors.White;
            var keywordColorResult = _celScriptService.SyntaxColors.GetColor(syntaxFormat.Category);
            syntaxColor = keywordColorResult.Success ? keywordColorResult.Data : Colors.White;

            // Only update the keyword color if the color has actually changed
            if (KeywordColor == null || KeywordColor.Color != syntaxColor)
            {
                KeywordColor = new SolidColorBrush(syntaxColor);
            }

            Tooltip = syntaxFormat.State == SyntaxState.Error ? $"{syntaxFormat.ErrorMessage}\n{syntaxFormat.Tooltip}" : syntaxFormat.Tooltip;

            var instructionHash = syntaxFormat.HashCode;

            if (_cachedinstructionHash != instructionHash)
            {
                // Only regenerate the description text Run objects if the instruction has changed
                _cachedinstructionHash = instructionHash;

                Guard.IsNotNull(DescriptionTextBlock);
                DescriptionTextBlock.Inlines.Clear();

                List<SyntaxToken> syntaxTokens;
                if (syntaxFormat.State == SyntaxState.Error)
                {
                    // Show the full summary using a single token with the error color
                    syntaxTokens = new ()
                    {
                        new SyntaxToken()
                        {
                            Category = InstructionCategory.Error,
                            Text = syntaxFormat.Summary
                        }
                    };
                }
                else
                {
                    // Show all the parsed tokens using the appropriate color for each token
                    syntaxTokens = syntaxFormat.Tokens;
                }

                foreach (var token in syntaxTokens)
                {
                    var run = new Run();
                    run.Text = token.Text;

                    var syntaxCategory = syntaxFormat.State == SyntaxState.Valid ? token.Category : InstructionCategory.Error;

                    // Apply description colors
                    var descColorResult = _celScriptService.SyntaxColors.GetColor(syntaxCategory);
                    if (descColorResult.Success)
                    {
                        var descSyntaxColor = descColorResult.Data!;
                        run.Foreground = new SolidColorBrush(descSyntaxColor);
                    }
                    else
                    {
                        run.Foreground = new SolidColorBrush(Colors.White);
                    }

                    DescriptionTextBlock.Inlines.Add(run);
                }
            }
        }

        private Dictionary<string, Type>? _cachedInstructionTypes { get; set; }

        public TextBox? KeywordTextBox { get; set; }
        public TextBlock? DescriptionTextBlock { get; set; }

        private void PopulateInstructionTypes()
        {
            Guard.IsNotNull(Property);
            bool isInstructionsProperty = Property.PropertyInfo.Name == nameof(ICel.Instructions);

            _cachedInstructionTypes = new Dictionary<string, Type>();

            // Lookup the supported instructions from the CelType mixins.
            var cel = Property.Object as ICel;
            Guard.IsNotNull(cel);

            var celType = cel.CelType;
            Guard.IsNotNull(celType);

            foreach (var celMixin in celType.CelMixins)
            {
                foreach (var kv in celMixin.InstructionTypes)
                {
                    var instructionName = kv.Key;
                    var instructionType = kv.Value;

                    if (isInstructionsProperty)
                    {
                        // The Instructions list property should contain all IInstruction nested types
                        if (!typeof(IInstruction).IsAssignableFrom(instructionType))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // The Input and Output list properties should only contain ITypeInstruction nested types
                        if (!typeof(ITypeInstruction).IsAssignableFrom(instructionType))
                        {
                            continue;
                        }
                    }

                    _cachedInstructionTypes.Add(instructionName, instructionType);
                }
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Keyword))
            {
                var keyword = Keyword;

                // Lookup the instruction types for the Cel's CelType
                Guard.IsNotNull(Property);
                var cel = Property.Object as Cel;
                Guard.IsNotNull(cel);

                if (_cachedInstructionTypes is null)
                {
                    // This is populated as late as possible to ensure we're accessing the authoritative
                    // list of Instructions for this CelType.
                    PopulateInstructionTypes();
                }

                Guard.IsNotNull(_cachedInstructionTypes);

                string? newInstructionKeyword = null;
                Type? newInstructionType = null;
                foreach (var kv in _cachedInstructionTypes)
                {
                    var instructionKeyword = kv.Key;
                    var instructionType = kv.Value;

                    if (keyword.Equals(instructionKeyword, StringComparison.OrdinalIgnoreCase))
                    {
                        newInstructionKeyword = instructionKeyword;
                        newInstructionType = instructionType;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(newInstructionKeyword) && 
                    keyword != newInstructionKeyword)
                {
                    Guard.IsNotNull(KeywordTextBox);

                    int caretPos = KeywordTextBox.SelectionStart;
                    InstructionLine.Keyword = newInstructionKeyword;

                    // Setting the keyword here causes the caret to asynchronously move to the start of the line.
                    // To work around this we listen for the selection change event when this happens and then
                    // move it back to the original position.
                    void OnSelectionChanged(object s, RoutedEventArgs e)
                    {
                        KeywordTextBox.SelectionChanged -= OnSelectionChanged;
                        KeywordTextBox.Select(caretPos, 0);
                    }
                    KeywordTextBox.SelectionChanged += OnSelectionChanged;
                }

                bool changed = false;
                if (newInstructionType == null)
                {
                    // No matching instruction found
                    if (InstructionLine.Instruction == null ||
                        InstructionLine.Instruction.GetType() != typeof(EmptyInstruction))
                    {
                        if (InstructionLine.Instruction != null)
                        {
                            // Cache a json representation of the previous instruction in case we can recycle any of its
                            // property values when we next create a new Instruction.
                            _previousInstructionJson = JsonConvert.SerializeObject(InstructionLine.Instruction);
                        }

                        // Create an empty instruction
                        InstructionLine.Instruction = new EmptyInstruction();
                        changed = true;
                    }
                }
                else
                {
                    // Matching instruction found
                    if (InstructionLine.Instruction == null ||
                        newInstructionType != InstructionLine.Instruction.GetType())
                    {
                        // Create a new instruction of the matched type
                        var instruction = Activator.CreateInstance(newInstructionType) as IInstruction;
                        Guard.IsNotNull(instruction);

                        InstructionLine.Instruction = instruction;

                        // Try to recycle any matching properties from the previous instruction
                        if (!string.IsNullOrEmpty(_previousInstructionJson))
                        {
                            var settings = new JsonSerializerSettings()
                            {
                                MissingMemberHandling = MissingMemberHandling.Ignore,
                            };
                            JsonConvert.PopulateObject(_previousInstructionJson, InstructionLine.Instruction, settings);
                            _previousInstructionJson = null;
                        }

                        if (instruction is ITreeNode childNode)
                        {
                            // Set the InstructionLine as the parent of the new Instruction
                            ParentNodeRef.SetParent(childNode, InstructionLine);
                        }

                        changed = true;
                    }
                }

                if (changed)
                {
                    // Notify the detail panel that its label needs to be updated
                    var detailsChangedMessage = new InstructionDetailsChangedMessage(InstructionLine);
                    _messengerService.Send(detailsChangedMessage);

                    if (Property.Context == PropertyContext.CelInput ||
                        Property.Context == PropertyContext.CelOutput)
                    {
                        NotifySignatureChanged();
                    }
                }
            }
        }

        protected override void PropertyViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IndentWidth) ||
                e.PropertyName == nameof(KeywordColor) ||
                e.PropertyName == nameof(Tooltip))
            {
                // These properties are only used for display purposes, they should not trigger a save
                return;
            }

            // Changes to the Keyword property are handled by the ViewModel_PropertyChanged method above.
            // This check handles changes to any other property in the Input and Output contexts that
            // might affect the signature.
            bool isSignatureContext = IsSignatureContext();
            bool isKeywordOrDescription = e.PropertyName == nameof(Keyword) || e.PropertyName == nameof(Description);
            if (isSignatureContext && !isKeywordOrDescription)
            {
                NotifySignatureChanged();
            }

            // Notify the wrapped property that it has changed
            base.PropertyViewModel_PropertyChanged(sender, e);
        }

        private bool IsSignatureContext()
        {
            Guard.IsNotNull(Property);
            return Property.Context == PropertyContext.CelInput || Property.Context == PropertyContext.CelOutput;
        }

        public void NotifyWillDelete()
        {
            if (IsSignatureContext())
            {
                NotifySignatureChanged();
            }    
        }

        public void NotifyIndexChanged(int newIndex)
        {
            if (IsSignatureContext())
            {
                NotifySignatureChanged();
            }
        }

        private void NotifySignatureChanged()
        {
            Guard.IsNotNull(Property);
            var cel = Property.Object as ICel;
            Guard.IsNotNull(cel);

            var signatureMessage = new CelSignatureChangedMessage(cel);
            _messengerService.Send(signatureMessage);
        }

        public void OnViewLoaded()
        {
            if (!_messengerService.IsRegistered<CelSyntaxFormatUpdatedMessage>(this))
            {
                _messengerService.Register<CelSyntaxFormatUpdatedMessage>(this, OnSyntaxFormatUpdated);
            }
        }

        public void OnViewUnloaded()
        {
            if (_messengerService.IsRegistered<CelSyntaxFormatUpdatedMessage>(this))
            {
                _messengerService.Unregister<CelSyntaxFormatUpdatedMessage>(this);
            }
        }
    }
}
