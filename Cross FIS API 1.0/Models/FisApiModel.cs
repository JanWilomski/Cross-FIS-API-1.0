
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Cross_FIS_API_1._0.Models
{
    public enum Exchange
    {
        WSE = 40,    // Warsaw Stock Exchange
        SMTF = 330,  // SMTF
        BSRM = 331,  // BSRM
        BSMTF = 332  // BSMTF
    }

    public enum Market
    {
        Bonds = 1,
        Cash = 2,
        Options = 3,
        Future = 4,
        Index = 5,
        OPCVM = 9,
        Growth = 16,
        FutureIndices = 17,
        Warrants = 20
    }

    public class ExchangeConfig
    {
        public Exchange Exchange { get; set; }
        public Market Market { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public string GetGLID()
        {
            return $"{(int)Exchange:D4}00{(int)Market:D3}000";
        }
    }

    public class Instrument
    {
        public string Symbol { get; set; }
        public string Name { get; set; }
        public string ISIN { get; set; }
        public string GLID { get; set; }
    }

    public partial class FISApiClient
    {
        private TcpClient tcpClient;
        private NetworkStream stream;
        private readonly string host;
        private readonly int port;
        private readonly string user;
        private readonly string password;
        private readonly string node;
        private readonly string subnode;
        private string callingId;

        private const byte STX = 2;
        private const byte ETX = 3;
        private const int HEADER_LENGTH = 32;
        private const int FOOTER_LENGTH = 3;
        private const int LG_LENGTH = 2;
        private const int CLIENT_ID_LENGTH = 16;

        public static List<ExchangeConfig> AvailableExchanges { get; } = new List<ExchangeConfig>
        {
            new ExchangeConfig { Exchange = Exchange.WSE, Market = Market.Cash, Name = "WSE Cash", Description = "Warsaw Stock Exchange - Cash Market" },
            new ExchangeConfig { Exchange = Exchange.WSE, Market = Market.Options, Name = "WSE Options", Description = "Warsaw Stock Exchange - Options Market" },
            new ExchangeConfig { Exchange = Exchange.WSE, Market = Market.Future, Name = "WSE Future", Description = "Warsaw Stock Exchange - Future Market" },
            new ExchangeConfig { Exchange = Exchange.SMTF, Market = Market.Cash, Name = "SMTF Cash", Description = "SMTF - Cash Market" },
            new ExchangeConfig { Exchange = Exchange.BSRM, Market = Market.Cash, Name = "BSRM Cash", Description = "BSRM - Cash Market" },
            new ExchangeConfig { Exchange = Exchange.BSMTF, Market = Market.Cash, Name = "BSMTF Cash", Description = "BSMTF - Cash Market" }
        };

        public event Action<string> LogMessage;
        public event Action<List<Instrument>> InstrumentsReceived;
        public event Action<string> ConnectionStatusChanged;
        public event Action<string> OrderConfirmed;
        public event Action<string> OrderRejected;

        public FISApiClient(string host, int port, string user, string password, string node, string subnode)
        {
            this.host = host;
            this.port = port;
            this.user = user;
            this.password = password;
            this.node = node;
            this.subnode = subnode;
            this.callingId = "00000";
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                LogMessage?.Invoke($"Łączenie z {host}:{port}...");
                tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(host, port);
                stream = tcpClient.GetStream();
                LogMessage?.Invoke("Połączenie TCP nawiązane.");

                await SendClientIdentification();
                await SendLogicalConnection();

                var response = await ReceiveResponse();
                bool connected = ProcessConnectionResponse(response);
                if (connected)
                {
                    ConnectionStatusChanged?.Invoke("Połączono");
                    _ = Task.Run(ListenForMessages); // Start listening in the background
                }
                else
                {
                    ConnectionStatusChanged?.Invoke("Błąd połączenia");
                }
                return connected;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Błąd połączenia: {ex.Message}");
                ConnectionStatusChanged?.Invoke("Błąd połączenia");
                return false;
            }
        }

        private async Task ListenForMessages()
        {
            while (IsConnected())
            {
                try
                {
                    if (stream.DataAvailable)
                    {
                        var response = await ReceiveResponse();
                        ProcessIncomingMessage(response);
                    }
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Błąd podczas nasłuchiwania: {ex.Message}");
                    Disconnect();
                }
            }
        }

        private async Task SendClientIdentification()
        {
            var clientId = Encoding.ASCII.GetBytes("FISAPICLIENT     ");
            await stream.WriteAsync(clientId, 0, CLIENT_ID_LENGTH);
            LogMessage?.Invoke("Identyfikator klienta wysłany.");
        }

        private async Task SendLogicalConnection()
        {
            var message = BuildLogicalConnectionMessage();
            await stream.WriteAsync(message, 0, message.Length);
            LogMessage?.Invoke($"Żądanie logicznego połączenia (1100) wysłane.");
        }

        private byte[] BuildLogicalConnectionMessage()
        {
            var dataBuilder = new List<byte>();
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(user.PadLeft(3, '0')));
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(password.PadRight(16, ' ')));
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(new string(' ', 7)));
            dataBuilder.AddRange(EncodeField("15"));
            dataBuilder.AddRange(EncodeField("V5"));
            dataBuilder.AddRange(EncodeField("26"));
            dataBuilder.AddRange(EncodeField(user));
            var data = dataBuilder.ToArray();
            return BuildMessage(data, 1100);
        }

        private void BuildHeader(byte[] message, ref int offset, int dataLength, int requestNumber)
        {
            message[offset++] = STX;
            message[offset++] = (byte)'0';
            var requestSize = (HEADER_LENGTH + dataLength + FOOTER_LENGTH).ToString("D5");
            Array.Copy(Encoding.ASCII.GetBytes(requestSize), 0, message, offset, 5);
            offset += 5;
            var calledId = subnode.PadLeft(5, '0');
            Array.Copy(Encoding.ASCII.GetBytes(calledId), 0, message, offset, 5);
            offset += 5;
            Array.Copy(Encoding.ASCII.GetBytes("     "), 0, message, offset, 5);
            offset += 5;
            Array.Copy(Encoding.ASCII.GetBytes(callingId), 0, message, offset, 5);
            offset += 5;
            Array.Copy(Encoding.ASCII.GetBytes("  "), 0, message, offset, 2);
            offset += 2;
            var reqNum = requestNumber.ToString("D5");
            Array.Copy(Encoding.ASCII.GetBytes(reqNum), 0, message, offset, 5);
            offset += 5;
            Array.Copy(Encoding.ASCII.GetBytes("   "), 0, message, offset, 3);
            offset += 3;
        }
        
        private byte[] BuildMessage(byte[] data, int requestNumber)
        {
            var totalLength = LG_LENGTH + HEADER_LENGTH + data.Length + FOOTER_LENGTH;
            var message = new byte[totalLength];
            var offset = 0;
            message[offset++] = (byte)(totalLength % 256);
            message[offset++] = (byte)(totalLength / 256);
            BuildHeader(message, ref offset, data.Length, requestNumber);
            Array.Copy(data, 0, message, offset, data.Length);
            offset += data.Length;
            message[offset++] = (byte)' ';
            message[offset++] = (byte)' ';
            message[offset++] = ETX;
            return message;
        }

        private byte[] EncodeField(string value)
        {
            var valueBytes = Encoding.ASCII.GetBytes(value);
            var encoded = new byte[valueBytes.Length + 1];
            encoded[0] = (byte)(valueBytes.Length + 32);
            Array.Copy(valueBytes, 0, encoded, 1, valueBytes.Length);
            return encoded;
        }

        private string DecodeField(byte[] data, ref int position)
        {
            if (position >= data.Length)
            {
                return string.Empty;
            }
            var fieldLength = data[position] - 32;
            if (position + 1 + fieldLength > data.Length)
            {
                return string.Empty;
            }
            var value = Encoding.ASCII.GetString(data, position + 1, fieldLength);
            position += 1 + fieldLength;
            return value;
        }

        private async Task<byte[]> ReceiveResponse()
        {
            var buffer = new byte[32000];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            var response = new byte[bytesRead];
            Array.Copy(buffer, response, bytesRead);
            LogMessage?.Invoke($"Odebrano odpowiedź: {bytesRead} bajtów");
            return response;
        }

        private bool ProcessConnectionResponse(byte[] response)
        {
            var stxPos = Array.IndexOf(response, STX);
            if (stxPos == -1)
            {
                LogMessage?.Invoke("Nie znaleziono STX w odpowiedzi");
                return false;
            }

            if (stxPos + 32 > response.Length)
            {
                 LogMessage?.Invoke("Odpowiedź za krótka");
                return false;
            }

            var calledIdStr = Encoding.ASCII.GetString(response, stxPos + 7, 5);
            var requestNumberStr = Encoding.ASCII.GetString(response, stxPos + 24, 5);
            callingId = calledIdStr.Trim();

            if (int.TryParse(requestNumberStr, out int requestNumber))
            {
                LogMessage?.Invoke($"Numer żądania odpowiedzi: {requestNumber}");
                if (requestNumber == 1100)
                {
                    LogMessage?.Invoke("Połączenie logiczne nawiązane pomyślnie!");
                    return true;
                }
                if (requestNumber == 1102)
                {
                    LogMessage?.Invoke("Połączenie logiczne odrzucone.");
                    ProcessConnectionError(response, stxPos);
                    return false;
                }
            }
            LogMessage?.Invoke("Nieznany format odpowiedzi");
            return false;
        }

        private void ProcessConnectionError(byte[] response, int stxPos)
        {
            var dataStart = stxPos + 32;
            var dataEnd = response.Length - FOOTER_LENGTH;
            if (dataStart >= dataEnd) return;

            try
            {
                var reasonPos = dataStart + 3 + 16;
                if (reasonPos < dataEnd)
                {
                    var reason = response[reasonPos] - 32;
                    string error = reason switch
                    {
                        0 => "Nieprawidłowe hasło",
                        1 => "Brak miejsca w bazie połączeń logicznych",
                        2 => "Nieprawidłowy format żądania połączenia",
                        3 => "Zabroniony numer użytkownika",
                        4 => "Nieznany numer użytkownika",
                        7 => "Użytkownik już połączony",
                        52 => "Złe hasło",
                        59 => "Już połączony",
                        _ => $"Nieznany kod błędu: {reason}"
                    };
                    LogMessage?.Invoke($"Błąd połączenia: {error}");
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Błąd podczas przetwarzania odpowiedzi błędu: {ex.Message}");
            }
        }

        public async Task SendDictionaryRequest(ExchangeConfig exchange)
        {
            if (!IsConnected())
            {
                LogMessage?.Invoke("Brak połączenia!");
                return;
            }
            LogMessage?.Invoke($"Wysyłanie żądania Dictionary dla: {exchange.Name}");
            var message = BuildDictionaryMessage(exchange.GetGLID());
            await stream.WriteAsync(message, 0, message.Length);
        }

        public async Task SendDictionaryRequestAllAsync()
        {
            if (!IsConnected())
            {
                LogMessage?.Invoke("Brak połączenia!");
                return;
            }

            LogMessage?.Invoke("Rozpoczynam pobieranie instrumentów dla wszystkich rynków...");
            var exchanges = new[] { 40, 330, 331, 332 };

            foreach (var exchange in exchanges)
            {
                var exchangeName = Enum.GetName(typeof(Exchange), exchange) ?? $"Exchange_{exchange}";
                LogMessage?.Invoke($"--- Przetwarzanie giełdy: {exchangeName} ---");
                for (int market = 1; market <= 20; market++)
                {
                    var glid = $"{exchange:D4}00{market:D3}000";
                    LogMessage?.Invoke($"Wysyłanie żądania dla rynku {market} (GLID: {glid})");
                    var message = BuildDictionaryMessage(glid);
                    await stream.WriteAsync(message, 0, message.Length);
                    await Task.Delay(100); // Małe opóźnienie między żądaniami
                }
            }
            LogMessage?.Invoke("Zakończono wysyłanie żądań.");
        }

        private byte[] BuildDictionaryMessage(string glid)
        {
            var dataBuilder = new List<byte>();
            dataBuilder.AddRange(Encoding.ASCII.GetBytes("00001"));
            dataBuilder.AddRange(EncodeField(glid));
            var data = dataBuilder.ToArray();
            return BuildMessage(data, 5108);
        }

        private void ProcessIncomingMessage(byte[] response)
        {
            var stxPos = Array.IndexOf(response, STX);
            if (stxPos == -1) return;

            var requestNumberStr = Encoding.ASCII.GetString(response, stxPos + 24, 5);
            if (int.TryParse(requestNumberStr, out int requestNumber))
            {
                switch (requestNumber)
                {
                    case 1044:
                        LogMessage?.Invoke("Brak instrumentów dla danego rynku.");
                        InstrumentsReceived?.Invoke(new List<Instrument>());
                        break;
                    case 5108:
                        ProcessDictionaryResponse(response, stxPos);
                        break;
                    case 4102: // Potwierdzenie zlecenia
                        ProcessOrderConfirmation(response, stxPos);
                        break;
                    case 4103: // Odrzucenie zlecenia
                        ProcessOrderReject(response, stxPos);
                        break;
                    default:
                        LogMessage?.Invoke($"Nieobsługiwany typ wiadomości: {requestNumber}");
                        break;
                }
            }
        }

        private void ProcessOrderConfirmation(byte[] response, int stxPos)
        {
            var dataStart = stxPos + 32;
            var dataEnd = response.Length - FOOTER_LENGTH;
            if (dataStart >= dataEnd) return;

            var responseData = new byte[dataEnd - dataStart];
            Array.Copy(response, dataStart, responseData, 0, responseData.Length);

            var position = 0;
            var orderId = DecodeField(responseData, ref position);
            var instrumentId = DecodeField(responseData, ref position);

            OrderConfirmed?.Invoke($"Zlecenie {orderId} dla instrumentu {instrumentId} zostało potwierdzone.");
        }

        private void ProcessOrderReject(byte[] response, int stxPos)
        {
            var dataStart = stxPos + 32;
            var dataEnd = response.Length - FOOTER_LENGTH;
            if (dataStart >= dataEnd) return;

            var responseData = new byte[dataEnd - dataStart];
            Array.Copy(response, dataStart, responseData, 0, responseData.Length);

            var position = 0;
            var reason = DecodeField(responseData, ref position);

            OrderRejected?.Invoke($"Zlecenie zostało odrzucone. Powód: {reason}");
        }

        private void ProcessDictionaryResponse(byte[] response, int stxPos)
        {
            try
            {
                var dataStart = stxPos + 32;
                var dataEnd = response.Length - FOOTER_LENGTH;
                if (dataStart >= dataEnd) return;

                var responseData = new byte[dataEnd - dataStart];
                Array.Copy(response, dataStart, responseData, 0, responseData.Length);

                var position = 0;
                position++; // Chaining

                var numberOfGlidStr = Encoding.ASCII.GetString(responseData, position, 5);
                var numberOfGlid = int.Parse(numberOfGlidStr);
                position += 5;

                if (numberOfGlid == 0)
                {
                    LogMessage?.Invoke("Brak instrumentów.");
                    InstrumentsReceived?.Invoke(new List<Instrument>());
                    return;
                }

                LogMessage?.Invoke($"Znaleziono {numberOfGlid} instrumentów.");
                var instruments = new List<Instrument>();

                for (int i = 0; i < numberOfGlid; i++)
                {
                    var instrument = new Instrument();
                    // Tutaj trzeba będzie zaimplementować logikę parsowania pól instrumentu
                    // Na podstawie analizy kodu, pola to: GLID, Nazwa, ?, ISIN, ?
                    // To jest uproszczona wersja
                     if (position >= responseData.Length) break;
                    var fieldLength1 = responseData[position] - 32;
                    var fieldValue1 = Encoding.ASCII.GetString(responseData, position + 1, fieldLength1);
                    instrument.GLID = fieldValue1;
                    if(fieldValue1.Length > 12) instrument.Symbol = fieldValue1.Substring(12);
                    position += 1 + fieldLength1;

                    if (position >= responseData.Length) break;
                    var fieldLength2 = responseData[position] - 32;
                    instrument.Name = Encoding.ASCII.GetString(responseData, position + 1, fieldLength2);
                    position += 1 + fieldLength2;
                    
                    if (position >= responseData.Length) break;
                    var fieldLength3 = responseData[position] - 32;
                    position += 1 + fieldLength3; // Puste pole

                    if (position >= responseData.Length) break;
                    var fieldLength4 = responseData[position] - 32;
                    instrument.ISIN = Encoding.ASCII.GetString(responseData, position + 1, fieldLength4);
                    position += 1 + fieldLength4;

                    if (position >= responseData.Length) break;
                    var fieldLength5 = responseData[position] - 32;
                    position += 1 + fieldLength5; // Puste pole

                    instruments.Add(instrument);
                }
                InstrumentsReceived?.Invoke(instruments);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Błąd przetwarzania odpowiedzi Dictionary: {ex.Message}");
            }
        }

        public async Task SendCrossOrder(CrossOrder order)
        {
            if (!IsConnected())
            {
                LogMessage?.Invoke("Brak połączenia!");
                return;
            }
            LogMessage?.Invoke($"Wysyłanie zlecenia cross order dla: {order.InstrumentId}");
            var message = BuildCrossOrderMessage(order);
            await stream.WriteAsync(message, 0, message.Length);
            LogMessage?.Invoke($"Zlecenie cross order dla {order.InstrumentId} wysłane.");
        }

        private byte[] BuildCrossOrderMessage(CrossOrder order)
        {
            var dataBuilder = new List<byte>();
            dataBuilder.AddRange(EncodeField(order.InstrumentId));
            dataBuilder.AddRange(EncodeField("B")); // Strona kupna jako inicjator
            dataBuilder.AddRange(EncodeField(order.Quantity.ToString()));
            dataBuilder.AddRange(EncodeField(order.Price.ToString("F2")));
            dataBuilder.AddRange(EncodeField("L")); // Zlecenie typu Limit
            dataBuilder.AddRange(EncodeField("0")); // Ważne na dzień
            dataBuilder.AddRange(EncodeField(order.BuyerAccount));
            dataBuilder.AddRange(EncodeField("C")); // Typ Cross
            dataBuilder.AddRange(EncodeField("S")); // Strona przeciwna - sprzedaż
            dataBuilder.AddRange(EncodeField(order.SellerAccount));
            dataBuilder.AddRange(EncodeField(order.Price.ToString("F2")));

            var data = dataBuilder.ToArray();
            return BuildMessage(data, 2040); // Numer żądania dla zlecenia Cross Order
        }

        public bool IsConnected()
        {
            bool isTcpClientNull = tcpClient == null;
            bool isTcpClientConnected = tcpClient != null && tcpClient.Connected;
            bool isStreamNull = stream == null;
            bool result = !isTcpClientNull && isTcpClientConnected && !isStreamNull;
            //LogMessage?.Invoke($"IsConnected() check: tcpClient null={isTcpClientNull}, tcpClient.Connected={isTcpClientConnected}, stream null={isStreamNull}. Result: {result}");
            return result;
        }

        public void Disconnect()
        {
            if (IsConnected())
            {
                stream?.Close();
                tcpClient?.Close();
                LogMessage?.Invoke("Rozłączono.");
                ConnectionStatusChanged?.Invoke("Rozłączono");
            }
        }
    }
}
