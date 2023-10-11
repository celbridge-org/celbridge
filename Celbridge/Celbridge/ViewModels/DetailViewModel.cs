using Celbridge.Models;
using Celbridge.Services;
using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Scripting.Utils;
using CommunityToolkit.Mvvm.Messaging;

namespace Celbridge.ViewModels
{
    public partial class DetailViewModel : ObservableObject
    {
        private readonly IMessenger _messengerService;
        private readonly IInspectorService _inspectorService;

        public ItemCollection? ItemCollection { get; set; }

        [ObservableProperty]
        private string _labelText = string.Empty;

        public DetailViewModel(IMessenger messengerService,
            IInspectorService inspectorService)
        {
            _messengerService = messengerService;
            _inspectorService = inspectorService;

            _messengerService.Register<SelectedEntityChangedMessage>(this, OnSelectedEntityChanged);
            _messengerService.Register<SelectedCollectionChangedMessage>(this, OnSelectedCollectionChanged);
            _messengerService.Register<InstructionDetailsChangedMessage>(this, OnInstructionKeywordChanged);
        }

        private void OnSelectedEntityChanged(object r, SelectedEntityChangedMessage m)
        {
            PopulateUIElements();
        }

        private void OnSelectedCollectionChanged(object recipient, SelectedCollectionChangedMessage message)
        {
            PopulateUIElements();
        }

        private void OnInstructionKeywordChanged(object recipient, InstructionDetailsChangedMessage message)
        {
            PopulateUIElements();
        }

        private void PopulateUIElements()
        {
            var selectedCollection = _inspectorService.SelectedCollection as Property;
            var selectedCollectionIndex = _inspectorService.SelectedCollectionIndex;

            LabelText = string.Empty;

            Guard.IsNotNull(ItemCollection);
            ItemCollection.Clear();

            if (selectedCollection == null || selectedCollectionIndex == -1)
            {
                return;
            }

            // Todo: Make this work generically
            // Requires the IRecord to provide the record object for the ItemCollection - defaults to "this" record.

            var collection = selectedCollection.PropertyInfo.GetValue(selectedCollection.Object) as IList;
            Guard.IsNotNull(collection);

            var instructionLine = collection[selectedCollectionIndex] as InstructionLine;
            Guard.IsNotNull(instructionLine);

            var instruction = instructionLine.Instruction;
            Guard.IsNotNull(instruction);

            /*
            // Log the items and show the selected index
            var sb = new System.Text.StringBuilder();
            var i = 0;
            foreach (var o in collection)
            {
                var ins = o as InstructionLine;
                if (i == selectedCollectionIndex)
                {
                    sb.Append("> ");
                }
                sb.AppendLine($"{i},{ins.Description}");
                i++;
            }
            Log.Information(sb.ToString());
            */

            var result = PropertyViewUtils.CreatePropertyViews(instruction, selectedCollection.Context, OnDetailPropertyChanged);
            if (result is ErrorResult<List<UIElement>> error)
            {
                Log.Error(error.Message);
                return;
            }

            var views = result.Data!;
            ItemCollection.AddRange(views);

            LabelText = instruction.GetType() == typeof(EmptyInstruction) ? string.Empty : instructionLine.Keyword;
        }

        private void OnDetailPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var selectedCollection = _inspectorService.SelectedCollection as Property;
            Guard.IsNotNull(selectedCollection);

            var selectedCollectionIndex = _inspectorService.SelectedCollectionIndex;
            var propertyName = e.PropertyName;
            Guard.IsNotNull(propertyName);

            var message = new DetailPropertyChangedMessage(selectedCollection, selectedCollectionIndex, propertyName);
            _messengerService.Send(message);
        }
    }
}
