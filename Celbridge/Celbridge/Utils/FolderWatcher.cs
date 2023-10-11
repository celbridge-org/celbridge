using CommunityToolkit.Mvvm.Messaging;

namespace Celbridge.Utils
{
    public class FileChangedMessage
    {
        public string Path { get; set; } = string.Empty;
    }

    public class FolderChangedMessage
    {}

    class FolderWatcher : IDisposable
    {
        private bool _changeInProgress;
        private DateTime _modifiedTime;
        private readonly TimeSpan _cooldownTime;
        private readonly DispatcherTimer _updateTimer;
        private readonly IMessenger _messengerService;
        private FileSystemWatcher? _watcher;

        public List<string> _changedFiles  = new ();

        public FolderWatcher(IMessenger messengerService, string path, float cooldownTime)
        {
            _messengerService = messengerService;

            _cooldownTime = TimeSpan.FromSeconds(cooldownTime);

            _watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.Attributes
                | NotifyFilters.CreationTime
                | NotifyFilters.DirectoryName
                | NotifyFilters.FileName
                | NotifyFilters.LastAccess
                | NotifyFilters.LastWrite
                | NotifyFilters.Security
                | NotifyFilters.Size
            };

            _watcher.Changed += (s, e) =>
            {
                var extension = Path.GetExtension(e.Name);
                if (extension == ".celbridge")
                {
                    // Ignore project file changes
                    return;
                }

                // Todo: Check extension against list of supported extensions

                // Todo: Check why this is being called twice for each file change
                if (!_changedFiles.Contains(e.FullPath))
                {
                    _changedFiles.Add(e.FullPath);
                }

                OnModified();
            };

            _watcher.Created += (s,e) => OnModified();
            _watcher.Deleted += (s,e) => OnModified();
            _watcher.Renamed += (s,e) => OnModified();
            _watcher.Error += (s,e) => OnModified();
            _watcher.Filter = "*";
            _watcher.IncludeSubdirectories = true;
            _watcher.EnableRaisingEvents = true;

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(0.1)
            };
            _updateTimer.Tick += Update;
            _updateTimer.Start();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose of any managed objects here.
            }

            // Dispose of any unmanaged objects here.
            if (_watcher != null)
            {
                _watcher.Dispose();
                _watcher = null;
            }
        }

        ~FolderWatcher()
        {
            Dispose(false);
        }

        private void OnModified()
        {
            if (!_changeInProgress)
            {
                _changeInProgress = true;
            }
            _modifiedTime = DateTime.Now;
        }

        private void Update(object? sender, object e)
        {
            if (_changeInProgress)
            {
                var delta = DateTime.Now - _modifiedTime;
                if (delta > _cooldownTime)
                {
                    _changeInProgress = false;
                    _messengerService.Send(new FolderChangedMessage());

                    foreach (var changedFile in _changedFiles)
                    {
                        var message = new FileChangedMessage
                        {
                            Path = changedFile
                        };
                        _messengerService.Send(message);
                    }
                    _changedFiles.Clear();
                }
            }
        }
    }
}
