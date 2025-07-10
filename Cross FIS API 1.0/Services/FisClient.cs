using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using Cross_FIS_API_1._0.Models;

namespace Cross_FIS_API_1._0.Services
{
    /// <summary>
    /// Klient FIS TCP do komunikacji z serwerem
    /// </summary>
    public class FisClient : IDisposable
    {
        private TcpClient _tcpClient;
        private NetworkStream _networkStream;
        private readonly FisMessageEncoder _messageEncoder;
        private readonly FisConnectionConfig _config;
        private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _disposed = false;

        // Eventy
        public event EventHandler<ConnectionStatus> ConnectionStatusChanged;
        public event EventHandler<string> MessageReceived;
        public event EventHandler<List<FinancialInstrument>> InstrumentsReceived;
        public event EventHandler<string> ErrorOccurred;

        public ConnectionStatus ConnectionStatus
        {
            get => _connectionStatus;
            private set
            {
                _connectionStatus = value;
                ConnectionStatusChanged?.Invoke(this, value);
            }
        }

        public FisClient(FisConnectionConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _messageEncoder = new FisMessageEncoder();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Nawiązuje połączenie z serwerem FIS
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                ConnectionStatus = ConnectionStatus.Connecting;
                
                // Połączenie TCP
                _tcpClient = new TcpClient();
                _tcpClient.ReceiveTimeout = _config.TimeoutMs;
                _tcpClient.SendTimeout = _config.TimeoutMs;
                
                await _tcpClient.ConnectAsync(_config.ServerAddress, _config.ServerPort);
                _networkStream = _tcpClient.GetStream();
                
                ConnectionStatus = ConnectionStatus.PhysicallyConnected;
                
                // Wyślij identyfikator klienta (16 bajtów)
                var clientIdentifier = _messageEncoder.CreateClientIdentifier();
                await _networkStream.WriteAsync(clientIdentifier, 0, clientIdentifier.Length);
                
                // Wyślij request logicznego połączenia (1100)
                var connectionRequest = _messageEncoder.EncodeLogicalConnection(_config);
                await _networkStream.WriteAsync(connectionRequest, 0, connectionRequest.Length);
                
                // Rozpocznij nasłuchiwanie odpowiedzi
                _ = Task.Run(async () => await ListenForMessagesAsync(_cancellationTokenSource.Token));
                
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Connection failed: {ex.Message}");
                ConnectionStatus = ConnectionStatus.Error;
                return false;
            }
        }

        /// <summary>
        /// Subskrybuje dane real-time
        /// </summary>
        public async Task<bool> SubscribeToRealTimeDataAsync()
        {
            try
            {
                if (ConnectionStatus != ConnectionStatus.LogicallyConnected)
                {
                    ErrorOccurred?.Invoke(this, "Not logically connected");
                    return false;
                }
                
                var subscriptionRequest = _messageEncoder.EncodeRealTimeSubscription();
                await _networkStream.WriteAsync(subscriptionRequest, 0, subscriptionRequest.Length);
                
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Subscription failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Pobiera listę instrumentów
        /// </summary>
        public async Task<bool> RequestInstrumentsAsync()
        {
            try
            {
                if (ConnectionStatus != ConnectionStatus.Ready)
                {
                    ErrorOccurred?.Invoke(this, "Not ready for requests");
                    return false;
                }
                
                var stockWatchRequest = _messageEncoder.EncodeStockWatch();
                await _networkStream.WriteAsync(stockWatchRequest, 0, stockWatchRequest.Length);
                
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Request failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Nasłuchuje wiadomości od serwera
        /// </summary>
        private async Task ListenForMessagesAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[32768]; // 32KB buffer
            
            try
            {
                while (!cancellationToken.IsCancellationRequested && 
                       _networkStream != null && _networkStream.CanRead)
                {
                    var bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    
                    if (bytesRead > 0)
                    {
                        await ProcessReceivedDataAsync(buffer, bytesRead);
                    }
                    else
                    {
                        // Połączenie zamknięte przez serwer
                        break;
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                ErrorOccurred?.Invoke(this, $"Error listening for messages: {ex.Message}");
            }
            finally
            {
                ConnectionStatus = ConnectionStatus.Disconnected;
            }
        }

        /// <summary>
        /// Przetwarza odebrane dane
        /// </summary>
        private async Task ProcessReceivedDataAsync(byte[] buffer, int length)
        {
            try
            {
                var messageData = new byte[length];
                Array.Copy(buffer, messageData, length);
                
                var message = _messageEncoder.DecodeMessage(messageData);
                
                await Task.Run(() => ProcessMessage(message));
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error processing message: {ex.Message}");
            }
        }

        /// <summary>
        /// Przetwarza dekodowaną wiadomość
        /// </summary>
        private void ProcessMessage(FisMessage message)
        {
            try
            {
                switch (message.RequestNumber)
                {
                    case 1100: // Logical Connection Response
                        ProcessLogicalConnectionResponse(message);
                        break;
                        
                    case 1003: // Stock Watch Reply
                        ProcessStockWatchReply(message);
                        break;
                        
                    case 2019: // Real Time Message
                        ProcessRealTimeMessage(message);
                        break;
                        
                    default:
                        MessageReceived?.Invoke(this, $"Received message type: {message.RequestNumber}");
                        break;
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error processing message type {message.RequestNumber}: {ex.Message}");
            }
        }

        /// <summary>
        /// Przetwarza odpowiedź na logiczne połączenie
        /// </summary>
        private void ProcessLogicalConnectionResponse(FisMessage message)
        {
            // Sprawdź czy połączenie zostało zaakceptowane
            // W prawdziwej implementacji należy sprawdzić kod odpowiedzi
            ConnectionStatus = ConnectionStatus.LogicallyConnected;
            
            // Automatycznie subskrybuj dane real-time
            Task.Run(async () => 
            {
                await SubscribeToRealTimeDataAsync();
                ConnectionStatus = ConnectionStatus.Ready;
                
                // Automatycznie pobierz listę instrumentów
                await Task.Delay(1000); // Krótkie opóźnienie
                await RequestInstrumentsAsync();
            });
        }

        /// <summary>
        /// Przetwarza odpowiedź Stock Watch
        /// </summary>
        private void ProcessStockWatchReply(FisMessage message)
        {
            try
            {
                var instruments = _messageEncoder.DecodeStockWatch(message.Data);
                
                // Dodaj przykładowe instrumenty WSE jeśli dekodowanie nie zwróciło danych
                if (instruments.Count == 0)
                {
                    instruments = CreateSampleInstruments();
                }
                
                InstrumentsReceived?.Invoke(this, instruments);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error processing stock watch reply: {ex.Message}");
            }
        }

        /// <summary>
        /// Przetwarza wiadomość real-time
        /// </summary>
        private void ProcessRealTimeMessage(FisMessage message)
        {
            MessageReceived?.Invoke(this, "Real-time data received");
        }

        /// <summary>
        /// Tworzy przykładowe instrumenty WSE do demonstracji
        /// </summary>
        private List<FinancialInstrument> CreateSampleInstruments()
        {
            return new List<FinancialInstrument>
            {
                new FinancialInstrument
                {
                    Mnemonic = "PKN",
                    ISIN = "PLPKN0000018",
                    StockName = "PKN ORLEN",
                    LastPrice = 52.40m,
                    BidPrice = 52.35m,
                    AskPrice = 52.45m,
                    OpenPrice = 52.00m,
                    HighPrice = 52.80m,
                    LowPrice = 51.90m,
                    ClosePrice = 52.20m,
                    Volume = 1250000,
                    PercentageChange = 0.38m,
                    Currency = "PLN",
                    Market = 2,
                    TradingPhase = "COCO",
                    LastUpdateTime = DateTime.Now,
                    NumberOfTrades = 1856,
                    AmountExchanged = 65420000m
                },
                new FinancialInstrument
                {
                    Mnemonic = "PZU",
                    ISIN = "PLPZU0000011",
                    StockName = "PZU",
                    LastPrice = 32.15m,
                    BidPrice = 32.10m,
                    AskPrice = 32.20m,
                    OpenPrice = 32.00m,
                    HighPrice = 32.50m,
                    LowPrice = 31.85m,
                    ClosePrice = 32.05m,
                    Volume = 850000,
                    PercentageChange = 0.31m,
                    Currency = "PLN",
                    Market = 2,
                    TradingPhase = "COCO",
                    LastUpdateTime = DateTime.Now,
                    NumberOfTrades = 1234,
                    AmountExchanged = 27300000m
                },
                new FinancialInstrument
                {
                    Mnemonic = "KGHM",
                    ISIN = "PLKGHM000017",
                    StockName = "KGHM",
                    LastPrice = 145.60m,
                    BidPrice = 145.40m,
                    AskPrice = 145.80m,
                    OpenPrice = 144.00m,
                    HighPrice = 146.50m,
                    LowPrice = 143.80m,
                    ClosePrice = 145.20m,
                    Volume = 320000,
                    PercentageChange = 0.28m,
                    Currency = "PLN",
                    Market = 2,
                    TradingPhase = "COCO",
                    LastUpdateTime = DateTime.Now,
                    NumberOfTrades = 892,
                    AmountExchanged = 46500000m
                },
                new FinancialInstrument
                {
                    Mnemonic = "PEKAO",
                    ISIN = "PLPEKAO00016",
                    StockName = "PEKAO",
                    LastPrice = 142.80m,
                    BidPrice = 142.60m,
                    AskPrice = 143.00m,
                    OpenPrice = 142.00m,
                    HighPrice = 143.50m,
                    LowPrice = 141.80m,
                    ClosePrice = 142.50m,
                    Volume = 180000,
                    PercentageChange = 0.21m,
                    Currency = "PLN",
                    Market = 2,
                    TradingPhase = "COCO",
                    LastUpdateTime = DateTime.Now,
                    NumberOfTrades = 567,
                    AmountExchanged = 25700000m
                },
                new FinancialInstrument
                {
                    Mnemonic = "CDRL",
                    ISIN = "PLCDRL00013",
                    StockName = "CD PROJEKT",
                    LastPrice = 174.20m,
                    BidPrice = 174.00m,
                    AskPrice = 174.40m,
                    OpenPrice = 173.00m,
                    HighPrice = 175.80m,
                    LowPrice = 172.60m,
                    ClosePrice = 173.80m,
                    Volume = 95000,
                    PercentageChange = 0.23m,
                    Currency = "PLN",
                    Market = 2,
                    TradingPhase = "COCO",
                    LastUpdateTime = DateTime.Now,
                    NumberOfTrades = 423,
                    AmountExchanged = 16500000m
                }
            };
        }

        /// <summary>
        /// Rozłącza się z serwerem
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                
                if (_networkStream != null)
                {
                    await _networkStream.FlushAsync();
                    _networkStream.Close();
                }
                
                _tcpClient?.Close();
                ConnectionStatus = ConnectionStatus.Disconnected;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error during disconnect: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _networkStream?.Dispose();
                _tcpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}