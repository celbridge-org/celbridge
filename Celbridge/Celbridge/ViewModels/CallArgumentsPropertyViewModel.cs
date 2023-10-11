using Celbridge.Models;
using Celbridge.Services;
using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Celbridge.ViewModels
{
    public partial class CallArgumentsPropertyViewModel : ObservableObject
    {
        private IMessenger _messengerService;
        private IProjectService _projectService;
        private ICelScriptService _celScriptService;
        private IInspectorService _inspectorService;

        public Property Property { get; private set; }
        public int ItemIndex { get; set; }

        public CallArgumentsPropertyViewModel(IMessenger messengerService, IProjectService projectService, ICelScriptService celScriptService, IInspectorService inspectorService)
        {
            _messengerService = messengerService;
            _projectService = projectService;
            _celScriptService = celScriptService;
            _inspectorService = inspectorService;
        }

        public void SetProperty(Property property, string labelText)
        {
            // Ignore label text for CallArguments, just display the property user controls
            // Todo: Fix the indenting for the CallArgument properties

            Property = property;
            PropertyChanged += CallArgumentsPropertyViewModel_PropertyChanged;
            PopulateCelScriptOptions();
        }

        ////// Cel Script combobox

        private List<ICelScript> _celScripts = new List<ICelScript>();
        public List<string> CelScriptNames { get; } = new ();

        private int _selectedCelScriptIndex;
        public int SelectedCelScriptIndex
        {
            get => _selectedCelScriptIndex;
            set
            {
                SetProperty(ref _selectedCelScriptIndex, value);
            }
        }

        // The CelScriptOptions are only populated once, when the ViewModel is created.
        private Result PopulateCelScriptOptions()
        {
            // Get the list of CelScripts and sort by name
            _celScripts = _celScriptService.CelScripts.Values.OrderBy(c => c.Entity.Name).ToList();

            // Add CelScript names to display in combobox
            CelScriptNames.Clear();
            foreach (var celScript in _celScripts)
            {
                var name = celScript.Entity.Name;
                name = Path.GetFileNameWithoutExtension(name);
                Guard.IsFalse(string.IsNullOrEmpty(name));

                CelScriptNames.Add(name);
            }

            // Get the record object that this property is referencing and create a view for each of its properties.
            ICallArguments callArguments = Property.PropertyInfo.GetValue(Property.Object) as ICallArguments;
            Guard.IsNotNull(callArguments);

            // Select the CelScript in the combobox that matches the CelScriptName property
            string celScriptName = callArguments.CelScriptName;
            var celScriptIndex = CelScriptNames.IndexOf(celScriptName);
            if (celScriptIndex == -1)
            {
                // Use the name of the parent CelScript
                var treeNode = callArguments as ITreeNode;
                Guard.IsNotNull(treeNode);

                var parentCelScript = ParentNodeRef.FindParent<ICelScript>(treeNode) as ICelScript;
                Guard.IsNotNull(parentCelScript);

                celScriptIndex = _celScripts.IndexOf(parentCelScript);
            }

            SelectedCelScriptIndex = celScriptIndex;

            UpdateCelOptions();

            // Select the index of the Cel option that matches the callArgument's CelName property.
            // This only needs to be done when the ViewModel is created, the user manually sets the
            // SelectedCelIndex when they use the combobox.
            var celName = callArguments.CelName;
            if (string.IsNullOrEmpty(celName))
            {
                SelectedCelIndex = -1;
            }
            else
            {
                SelectedCelIndex = CelNames.IndexOf(celName);
            }

            UpdateSelectedCelConnection();

            return new SuccessResult();
        }

        ////// Cel combobox

        private List<ICel> _cels = new ();
        public List<string> CelNames { get; } = new();

        private int _selectedCelIndex;
        public int SelectedCelIndex
        {
            get => _selectedCelIndex;
            set
            {
                SetProperty(ref _selectedCelIndex, value);
            }
        }

        private Result UpdateCelOptions()
        {
            if (SelectedCelScriptIndex == -1)
            {
                _cels.Clear();
                SelectedCelIndex = -1;
                if (CelNames.Count > 0)
                {
                    CelNames.Clear();
                    OnPropertyChanged(nameof(CelNames));
                }

                return new SuccessResult();
            }

            var celScript = _celScripts[SelectedCelScriptIndex];
            Guard.IsNotNull(celScript);

            _cels.Clear();
            CelNames.Clear();
            foreach (var celScriptNode in celScript.Cels)
            {
                var cel = celScriptNode as ICel;
                Guard.IsNotNull(cel);

                _cels.Add(cel);
            }

            _cels.Sort((a, b) => a.Name.CompareTo(b.Name));
            foreach (var cel in _cels)
            {
                CelNames.Add(cel.Name);
            }
            OnPropertyChanged(nameof(CelNames));

            return new SuccessResult();
        }

        ////// Cel Signature property 

        public Result<List<UIElement>> CreateChildViews()
        {
            try
            {
                // Get the record object that this property is referencing and create a view for each of its properties.
                ICallArguments callArguments = Property.PropertyInfo.GetValue(Property.Object) as ICallArguments;
                Guard.IsNotNull(callArguments);

                var result = PropertyViewUtils.CreatePropertyViews(callArguments, PropertyContext.Record, (s, e) =>
                {
                    // Notify observers of the View Model that a property has changed
                    OnPropertyChanged(e.PropertyName);

                    // Notify the record property itself that it has changed.
                    Property.NotifyPropertyChanged();
                });

                var list = result.Data;

                return result;
            }
            catch (Exception ex)
            {
                return new ErrorResult<List<UIElement>>($"Failed to create child views. {ex.Message}");
            }
        }

        private void CallArgumentsPropertyViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectedCelScriptIndex))
            {
                // Get the record object that this property is referencing and create a view for each of its properties.
                ICallArguments callArguments = Property.PropertyInfo.GetValue(Property.Object) as ICallArguments;
                Guard.IsNotNull(callArguments);

                string celScriptName = string.Empty;
                if (SelectedCelScriptIndex >= 0)
                {
                    celScriptName = CelScriptNames[SelectedCelScriptIndex];
                }

                UpdateCelOptions();

                if (celScriptName != callArguments.CelScriptName)
                {
                    callArguments.CelScriptName = celScriptName;
                    OnPropertyChanged(nameof(CallArguments.CelScriptName));
                }
            }

            if (e.PropertyName == nameof(SelectedCelIndex))
            {
                // Get the record object that this property is referencing and create a view for each of its properties.
                ICallArguments callArguments = Property.PropertyInfo.GetValue(Property.Object) as ICallArguments;
                Guard.IsNotNull(callArguments);

                var prevName = callArguments.CelName;
                if (SelectedCelIndex >= 0)
                {
                    callArguments.CelName = CelNames[SelectedCelIndex];
                }
                else
                {
                    callArguments.CelName = string.Empty;
                }

                if (prevName != callArguments.CelName)
                {
                    OnPropertyChanged(nameof(CallArguments.CelName));
                }
            }

            if (e.PropertyName == nameof(CallArguments.CelScriptName) ||
                e.PropertyName == nameof(CallArguments.CelName))
            {
                UpdateCallArguments();
            }
        }

        private Result UpdateCallArguments()
        {
            var callArguments = Property.PropertyInfo.GetValue(Property.Object) as ICallArguments;

            // Determine which CelScript and Cel this text is referencing
            ICelScript targetCelScript = null;
            if (string.IsNullOrEmpty(callArguments.CelScriptName))
            {
                // An empty "Cel Script Name" property indicates a Cel in the parent Cel Script

                var treeNode = callArguments as ITreeNode;
                Guard.IsNotNull(treeNode);

                var parentCelScript = ParentNodeRef.FindParent<ICelScript>(treeNode);
                Guard.IsNotNull(parentCelScript);

                targetCelScript = parentCelScript as ICelScript;
            }
            else
            {
                var activeProject = _projectService.ActiveProject;
                Guard.IsNotNull(activeProject);

                var celScriptName = callArguments.CelScriptName.Trim();
                var getResult = _celScriptService.GetCelScriptByName(activeProject, celScriptName);
                if (getResult.Success)
                {
                    targetCelScript = getResult.Data;
                }
            }

            ICel targetCel = null;
            if (targetCelScript != null)
            {
                var celName = callArguments.CelName;
                if (!string.IsNullOrEmpty(celName))
                {
                    celName = celName.Trim();
                    foreach (var cel in targetCelScript.Cels)
                    {
                        if (cel.Name.Equals(celName, StringComparison.OrdinalIgnoreCase))
                        {
                            targetCel = cel as ICel;
                            break;
                        }
                    }
                }
            }

            if (targetCel == null)
            {
                if (callArguments.CelSignature != null &&
                    callArguments.CelSignature.GetType() != typeof(CelSignature)) // Check if an empty CelSignature
                {
                    // Remove the existing signature, replace it with an empty one.
                    callArguments.CelId = Guid.Empty;
                    callArguments.CelSignature = new CelSignature();
                    NotifyCelSignatureChanged();
                }

                // This is fine, just means the names don't match any known CelScript / Cel
                // Todo: This should be an error once we support picking CelScript & Cel via a dropdown.
                return new SuccessResult();
            }

            // Create and apply the specified ICelSignature instance
            var targetCelScriptName = Path.GetFileNameWithoutExtension(targetCelScript.Entity.Name);
            var createResult = _celScriptService.CreateCelSignature(targetCelScriptName, targetCel.Name);
            if (createResult is ErrorResult<ICelSignature> createError)
            {
                return new ErrorResult($"Failed to create Cel Signature. {createError.Message}");
            }

            callArguments.CelId = (targetCel as ICelScriptNode).Id;
            callArguments.CelSignature = createResult.Data;

            NotifyCelSignatureChanged();

            UpdateSelectedCelConnection();

            return new SuccessResult();
        }

        // Trigger a Detail panel refresh to display the updated CelSignature property
        private void NotifyCelSignatureChanged()
        {
            ITreeNode callArguments = Property.PropertyInfo.GetValue(Property.Object) as ITreeNode;
            Guard.IsNotNull(callArguments);

            var instructionLine = ParentNodeRef.FindParent<InstructionLine>(callArguments) as InstructionLine;
            Guard.IsNotNull(instructionLine);

            var detailsChangedMessage = new InstructionDetailsChangedMessage(instructionLine);
            _messengerService.Send(detailsChangedMessage);

            var celConnectionsChangedMessage = new CelConnectionsChangedMessage();
            _messengerService.Send(celConnectionsChangedMessage);

            // Trigger a save
            Property.NotifyPropertyChanged();
        }

        private void UpdateSelectedCelConnection()
        {
            var callArgumentsNode = Property.PropertyInfo.GetValue(Property.Object) as ITreeNode;
            Guard.IsNotNull(callArgumentsNode);

            var callArguments = callArgumentsNode as ICallArguments;
            Guard.IsNotNull(callArguments);

            var parentCel = ParentNodeRef.FindParent<ICel>(callArgumentsNode) as ICelScriptNode;
            Guard.IsNotNull(parentCel);

            var celA = parentCel.Id;
            var celB = callArguments.CelId;

            var celConnectionId = CelConnection.CreateCelConnectionId(celA, celB);

            // Set this cel connection as the User Data on the Inspector Service.
            // Clients can listen for this property being set and cleared as the user
            // selects instructions and other entities in the inspector.
            _inspectorService.SelectedItemUserData = celConnectionId;
        }
    }
}
