using Celbridge.Models;
using Celbridge.Utils;
using System.Threading.Tasks;

namespace Celbridge.Views
{
    interface IDocumentView
    {
        IDocument Document { get; set; }

        Task<Result> LoadDocumentAsync();

        void CloseDocument();
    }
}
