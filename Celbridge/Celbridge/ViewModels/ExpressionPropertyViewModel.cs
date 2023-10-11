using Celbridge.Models;

namespace Celbridge.ViewModels
{
    public partial class ExpressionPropertyViewModel : ClassPropertyViewModel<ExpressionBase>
    {
        public string ExpressionValue
        {
            get => Value.Expression;
            set
            {
                Value.Expression = value;
                OnPropertyChanged(nameof(Value));
            }
        }
    }
}
