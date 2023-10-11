using Celbridge.Models;
using Celbridge.Utils;
using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Foundation;

namespace Celbridge.Views
{
    public partial class CelScriptDocumentView : TabViewItem, IDocumentView
    {
        public CelScriptDocumentViewModel ViewModel { get; }

        public CelScriptDocumentView()
        {
            this.InitializeComponent();

            ViewModel = (Application.Current as App).Host.Services.GetRequiredService<CelScriptDocumentViewModel>();
            ViewModel.CelCanvas = CelCanvas;

            ViewModel.NodePositionChanged += ViewModel_NodePositionChanged;
            ViewModel.CelConnectionSelected += ViewModel_CelConnectionSelected;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        public IDocument Document
        {
            get => ViewModel.Document;
            set => ViewModel.Document = value;
        }

        public void CloseDocument()
        {
            ViewModel.CloseDocumentCommand.ExecuteAsync(null);
        }

        public async Task<Result> LoadDocumentAsync()
        {
            return await ViewModel.LoadAsync();
        }

        private void CelCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var width = CelCanvas.ActualWidth;
            var height = CelCanvas.ActualHeight;

            CelCanvas.Clip = new RectangleGeometry()
            {
                Rect = new Rect(0, 0, width, height)
            };

            ViewModel.SpawnPosition = new Vector2((float)(width / 2), (float)(height / 2));
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.CelConnections))
            {
                UpdateCelConnections(ViewModel.CelConnections);
            }
        }

        private Dictionary<Guid, CelConnectionLine> _celConnectionLines = new();

        private void UpdateCelConnections(List<CelConnection> celConnections)
        {
            // Remove lines for connections that no longer exist
            {
                List<Guid> removeList = new();
                foreach (var kv in _celConnectionLines)
                {
                    var connectionId = kv.Key;
                    var line = kv.Value;

                    bool match = false;
                    foreach (var connection in celConnections)
                    {
                        if (connection.CelConnectionId == connectionId)
                        {
                            match = true;
                            break;
                        }
                    }

                    if (!match)
                    {
                        CelConnectionGroup.Children.Remove(line);
                        removeList.Add(connectionId);
                    }
                }

                foreach (var removeId in removeList)
                {
                    _celConnectionLines.Remove(removeId);
                }
            }

            // Add lines for newly created connections
            foreach (var celConnection in celConnections)
            {
                if (_celConnectionLines.ContainsKey(celConnection.CelConnectionId))
                {
                    continue;
                }

                // Todo: Offset using the actual width and height of the node

                var line = new CelConnectionLine();
                line.Start = new Point(celConnection.CelScriptNodeA.X + 40, celConnection.CelScriptNodeA.Y + 40);
                line.End = new Point(celConnection.CelScriptNodeB.X + 40, celConnection.CelScriptNodeB.Y + 40);
                line.Update();

                CelConnectionGroup.Children.Add(line);

                _celConnectionLines.Add(celConnection.CelConnectionId, line);
            }
        }

        private void ViewModel_NodePositionChanged()
        {
            // Update the position of each line to match it's connected cels

            var celConnections = ViewModel.CelConnections;

            foreach (var celConnectionLine in _celConnectionLines)
            {
                var celConnectionId = celConnectionLine.Key;
                var line = celConnectionLine.Value;

                var celConnection = celConnections.Find(c => c.CelConnectionId == celConnectionId);
                if (celConnection == null)
                {
                    // Todo: Remove this line from the group because it's no longer connected
                    continue;
                }

                // Todo: Offset using the actual width and height of the node

                line.Start = new Point(celConnection.CelScriptNodeA.X + 40, celConnection.CelScriptNodeA.Y + 40);
                line.End = new Point(celConnection.CelScriptNodeB.X + 40, celConnection.CelScriptNodeB.Y + 40);
                line.Update();
            }
        }

        private void ViewModel_CelConnectionSelected(Guid celConnectionId)
        {
            foreach (var celConnectionLine in _celConnectionLines)
            {
                var lineId = celConnectionLine.Key;
                var line = celConnectionLine.Value;

                if (lineId == celConnectionId)
                {
                    // Highlight exactly one connection if the id matches
                    // This means the call instruction corresponding to this line is selected.
                    line.ViewModel.StrokeThickness = 2;
                }
                else
                {
                    // All other lines are not highlighted
                    line.ViewModel.StrokeThickness = 0;
                }
            }
        }
    }
}
