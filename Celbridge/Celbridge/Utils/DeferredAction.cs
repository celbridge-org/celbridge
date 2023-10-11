using CommunityToolkit.Diagnostics;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

namespace Celbridge.Utils
{
    public class DeferredAction
    {
        private bool _isActionPending;
        private bool _isExecutingAction;
        private readonly DispatcherTimer _timer;
        private readonly Func<Task<Result>> _action;

        public bool Enabled 
        { 
            get => _timer.IsEnabled; 
            set
            {
                if (value)
                {
                    _timer.Start();
                }
                else
                {
                    _timer.Stop();
                }
            }
        }

        public DeferredAction(double interval, Func<Task<Result>> action)
        {
            Guard.IsTrue(interval > 0);
            Guard.IsNotNull(action);

            _action = action;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(interval)
            };

            _timer.Tick += OnSaveTimerTick;
            _timer.Start();
        }

        public async Task<Result> ExecuteNowAsync()
        {
            _isActionPending = false;
            _isExecutingAction = true;
            var result = await _action.Invoke();
            _isExecutingAction = false;
            return result;
        }

        public async Task<Result> ExecuteNowIfPendingAsync()
        {
            if (_isActionPending)
            {
                return await ExecuteNowAsync();
            }
            return new SuccessResult();
        }

        public void RequestExecute()
        {
            _isActionPending = true;
        }

        private void OnSaveTimerTick(object? sender, object e)
        {
            if (_isActionPending && !_isExecutingAction)
            {
                _isActionPending = false;
                _ = ExecuteDeferredAction();
            }
        }

        private async Task<Result> ExecuteDeferredAction()
        {
            try
            {
                return await ExecuteNowAsync();
            }
            catch (Exception ex)
            {
                _isExecutingAction = false;
                return new ErrorResult($"Failed to execute deferred action. {ex.Message}");
            }
        }
    }
}
