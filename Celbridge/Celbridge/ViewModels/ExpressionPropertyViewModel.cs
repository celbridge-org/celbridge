using CommunityToolkit.Diagnostics;

namespace Celbridge.ViewModels
{
    public partial class ExpressionPropertyViewModel : ClassPropertyViewModel<ExpressionBase>
    {
        public string ExpressionValue
        {
            get
            {
                Guard.IsNotNull(Value);
                return Value.Expression;
            }
            set
            {
                Guard.IsNotNull(value);
                Guard.IsNotNull(Value);
                Value.Expression = value;
                OnPropertyChanged(nameof(Value));
            }
        }
    }
}
