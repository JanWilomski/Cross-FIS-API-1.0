using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Cross_FIS_API_1._0.Models;
using Cross_FIS_API_1._0.Services;

namespace Cross_FIS_API_1._0
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private FisClient _fisClient;
        private ObservableCollection<FinancialInstrument> _instruments;
        private string _statusMessage = "Ready to connect to FIS server";
        private bool _isConnected = false;

        public ObservableCollection<FinancialInstrument> Instruments
        {
            get => _instruments;
            set
            {
                _instruments = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
                
                // Aktualizuj UI w głównym wątku
                Dispatcher.Invoke(() =>
                {
                    StatusMessageTextBlock.Text = value;
                });
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged();
                
                // Aktualizuj przyciski w głównym wątku
                Dispatcher.Invoke(() =>
                {
                    ConnectButton.IsEnabled = !value;
                    DisconnectButton.IsEnabled = value;
                    RefreshButton.IsEnabled = value;
                });
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            InitializeApplication();
        }

        private void InitializeApplication()
        {
            // Inicjalizuj kolekcję instrumentów
            Instruments = new ObservableCollection<FinancialInstrument>();
            InstrumentsDataGrid.ItemsSource = Instruments;
            
            // Ustaw początkowe wartości
            UpdateInstrumentCount();
            
            // Ustaw DataContext dla bindingu
            DataContext = this;
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectToFisServer();
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            DisconnectFromFisServer();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshInstruments();
        }

        private async void ConnectToFisServer()
        {
            try
            {
                // Utwórz konfigurację na podstawie wartości z UI
                var config = new FisConnectionConfig
                {
                    ServerAddress = ServerAddressTextBox.Text.Trim(),
                    ServerPort = int.Parse(PortTextBox.Text.Trim()),
                    UserNumber = UserTextBox.Text.Trim(),
                    Password = PasswordBox.Password,
                    DestinationServer = "SLC01", // Market Data Server
                    CallingId = "API01",
                    TimeoutMs = 30000
                };

                // Walidacja
                if (string.IsNullOrEmpty(config.ServerAddress) || config.ServerPort <= 0)
                {
                    MessageBox.Show("Please provide valid server address and port.", "Configuration Error", 
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusMessage = "Connecting to FIS server...";
                
                // Utwórz klienta FIS
                _fisClient = new FisClient(config);
                
                // Podłącz eventy
                _fisClient.ConnectionStatusChanged += OnConnectionStatusChanged;
                _fisClient.MessageReceived += OnMessageReceived;
                _fisClient.InstrumentsReceived += OnInstrumentsReceived;
                _fisClient.ErrorOccurred += OnErrorOccurred;
                
                // Połącz się z serwerem
                bool connected = await _fisClient.ConnectAsync();
                
                if (!connected)
                {
                    StatusMessage = "Failed to connect to FIS server";
                    MessageBox.Show("Failed to connect to FIS server. Please check your configuration.", 
                                    "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Connection error: {ex.Message}";
                MessageBox.Show($"Connection error: {ex.Message}", "Error", 
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DisconnectFromFisServer()
        {
            try
            {
                StatusMessage = "Disconnecting from FIS server...";
                
                if (_fisClient != null)
                {
                    await _fisClient.DisconnectAsync();
                    _fisClient.Dispose();
                    _fisClient = null;
                }
                
                IsConnected = false;
                StatusMessage = "Disconnected from FIS server";
                
                // Wyczyść instrumenty
                Instruments.Clear();
                UpdateInstrumentCount();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Disconnect error: {ex.Message}";
                MessageBox.Show($"Disconnect error: {ex.Message}", "Error", 
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RefreshInstruments()
        {
            try
            {
                if (_fisClient != null && _fisClient.ConnectionStatus == ConnectionStatus.Ready)
                {
                    StatusMessage = "Refreshing instruments...";
                    await _fisClient.RequestInstrumentsAsync();
                }
                else
                {
                    StatusMessage = "Not connected to FIS server";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Refresh error: {ex.Message}";
                MessageBox.Show($"Refresh error: {ex.Message}", "Error", 
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnConnectionStatusChanged(object sender, ConnectionStatus status)
        {
            Dispatcher.Invoke(() =>
            {
                switch (status)
                {
                    case ConnectionStatus.Disconnected:
                        StatusTextBlock.Text = "Disconnected";
                        StatusTextBlock.Foreground = Brushes.Red;
                        IsConnected = false;
                        StatusMessage = "Disconnected from FIS server";
                        break;
                        
                    case ConnectionStatus.Connecting:
                        StatusTextBlock.Text = "Connecting...";
                        StatusTextBlock.Foreground = Brushes.Orange;
                        StatusMessage = "Connecting to FIS server...";
                        break;
                        
                    case ConnectionStatus.PhysicallyConnected:
                        StatusTextBlock.Text = "TCP Connected";
                        StatusTextBlock.Foreground = Brushes.Blue;
                        StatusMessage = "TCP connection established";
                        break;
                        
                    case ConnectionStatus.LogicallyConnected:
                        StatusTextBlock.Text = "Logged In";
                        StatusTextBlock.Foreground = Brushes.Green;
                        StatusMessage = "Logical connection established";
                        break;
                        
                    case ConnectionStatus.Ready:
                        StatusTextBlock.Text = "Ready";
                        StatusTextBlock.Foreground = Brushes.DarkGreen;
                        IsConnected = true;
                        StatusMessage = "Connected and ready to receive data";
                        break;
                        
                    case ConnectionStatus.Error:
                        StatusTextBlock.Text = "Error";
                        StatusTextBlock.Foreground = Brushes.Red;
                        IsConnected = false;
                        StatusMessage = "Connection error occurred";
                        break;
                }
            });
        }

        private void OnMessageReceived(object sender, string message)
        {
            StatusMessage = $"Message: {message}";
        }

        private void OnInstrumentsReceived(object sender, List<FinancialInstrument> instruments)
        {
            Dispatcher.Invoke(() =>
            {
                // Wyczyść istniejące instrumenty
                Instruments.Clear();
                
                // Dodaj nowe instrumenty
                foreach (var instrument in instruments)
                {
                    Instruments.Add(instrument);
                }
                
                UpdateInstrumentCount();
                StatusMessage = $"Received {instruments.Count} instruments from FIS server";
            });
        }

        private void OnErrorOccurred(object sender, string error)
        {
            StatusMessage = $"Error: {error}";
            
            Dispatcher.Invoke(() =>
            {
                // Pokaż błąd tylko jeśli jest to poważny problem
                if (error.Contains("Connection") || error.Contains("failed"))
                {
                    MessageBox.Show(error, "FIS API Error", 
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            });
        }

        private void UpdateInstrumentCount()
        {
            Dispatcher.Invoke(() =>
            {
                InstrumentCountTextBlock.Text = Instruments.Count.ToString();
            });
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Rozłącz się przy zamykaniu aplikacji
            if (_fisClient != null)
            {
                try
                {
                    _fisClient.DisconnectAsync().Wait(5000); // Maksymalnie 5 sekund
                    _fisClient.Dispose();
                }
                catch (Exception ex)
                {
                    // Loguj błąd ale nie blokuj zamykania
                    Console.WriteLine($"Error during cleanup: {ex.Message}");
                }
            }
            
            base.OnClosing(e);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}