using Cross_FIS_API_1._0.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System;

namespace Cross_FIS_API_1._0.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly FISApiClient _fisApiClient;
        private string _status;
        private ObservableCollection<string> _logs;
        private ExchangeConfig _selectedExchange;
        private ObservableCollection<Instrument> _instruments;

        public MainViewModel()
        {
            _fisApiClient = new FISApiClient("172.31.136.4", 25003, "401", "glglgl", "5000", "4000");
            _fisApiClient.LogMessage += (msg) => App.Current.Dispatcher.Invoke(() => Logs.Add(msg));
            _fisApiClient.ConnectionStatusChanged += (status) =>
            {
                Status = status;
                ((AsyncRelayCommand)ConnectCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)FetchInstrumentsCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)FetchAllCommand).RaiseCanExecuteChanged();
            };
            _fisApiClient.InstrumentsReceived += (instruments) =>
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var i in instruments)
                    {
                        Instruments.Add(i);
                    }
                });
            };

            Logs = new ObservableCollection<string>();
            Instruments = new ObservableCollection<Instrument>();
            Exchanges = new ObservableCollection<ExchangeConfig>(FISApiClient.AvailableExchanges);

            ConnectCommand = new AsyncRelayCommand(ConnectAsync);
            FetchInstrumentsCommand = new AsyncRelayCommand(FetchInstrumentsAsync, () => _fisApiClient.IsConnected() && SelectedExchange != null);
            FetchAllCommand = new AsyncRelayCommand(FetchAllInstrumentsAsync, () => _fisApiClient.IsConnected());
            ClearInstrumentsCommand = new RelayCommand(ClearInstruments);
        }

        public ObservableCollection<ExchangeConfig> Exchanges { get; }
        public ICommand ConnectCommand { get; }
        public ICommand FetchInstrumentsCommand { get; }
        public ICommand FetchAllCommand { get; }
        public ICommand ClearInstrumentsCommand { get; }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<string> Logs
        {
            get => _logs;
            set
            {
                _logs = value;
                OnPropertyChanged();
            }
        }

        public ExchangeConfig SelectedExchange
        {
            get => _selectedExchange;
            set
            {
                _selectedExchange = value;
                OnPropertyChanged();
                ((AsyncRelayCommand)FetchInstrumentsCommand).RaiseCanExecuteChanged();
            }
        }

        public ObservableCollection<Instrument> Instruments
        {
            get => _instruments;
            set
            {
                _instruments = value;
                OnPropertyChanged();
            }
        }

        private async Task ConnectAsync()
        {
            await _fisApiClient.ConnectAsync();
        }

        private async Task FetchInstrumentsAsync()
        {
            if (SelectedExchange != null)
            {
                await _fisApiClient.SendDictionaryRequest(SelectedExchange);
            }
        }

        private async Task FetchAllInstrumentsAsync()
        {
            await _fisApiClient.SendDictionaryRequestAllAsync();
        }

        private void ClearInstruments()
        {
            Instruments.Clear();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Helper class for async commands
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
        { 
            _execute = execute;
            _canExecute = canExecute ?? (() => true);
        }

        public bool CanExecute(object parameter)
        {
            return !_isExecuting && _canExecute();
        }

        public async void Execute(object parameter)
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await _execute();
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public event EventHandler CanExecuteChanged;

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Helper class for simple commands
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute ?? (() => true);
        }

        public bool CanExecute(object parameter) => _canExecute();

        public void Execute(object parameter) => _execute();

        public event EventHandler CanExecuteChanged;

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}