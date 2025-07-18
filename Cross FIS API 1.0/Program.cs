using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FISApiClient
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

    public class FISApiClient
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

        // Stałe protokołu
        private const byte STX = 2;
        private const byte ETX = 3;
        private const int HEADER_LENGTH = 32;
        private const int FOOTER_LENGTH = 3;
        private const int LG_LENGTH = 2;
        private const int CLIENT_ID_LENGTH = 16;

        // Dostępne konfiguracje giełd
        private static readonly List<ExchangeConfig> AvailableExchanges = new List<ExchangeConfig>
        {
            new ExchangeConfig { Exchange = Exchange.WSE, Market = Market.Cash, Name = "WSE Cash", Description = "Warsaw Stock Exchange - Cash Market" },
            new ExchangeConfig { Exchange = Exchange.WSE, Market = Market.Options, Name = "WSE Options", Description = "Warsaw Stock Exchange - Options Market" },
            new ExchangeConfig { Exchange = Exchange.WSE, Market = Market.Future, Name = "WSE Future", Description = "Warsaw Stock Exchange - Future Market" },
            new ExchangeConfig { Exchange = Exchange.SMTF, Market = Market.Cash, Name = "SMTF Cash", Description = "SMTF - Cash Market" },
            new ExchangeConfig { Exchange = Exchange.BSRM, Market = Market.Cash, Name = "BSRM Cash", Description = "BSRM - Cash Market" },
            new ExchangeConfig { Exchange = Exchange.BSMTF, Market = Market.Cash, Name = "BSMTF Cash", Description = "BSMTF - Cash Market" }
        };

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
                Console.WriteLine($"Łączenie z {host}:{port}...");
                
                tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(host, port);
                stream = tcpClient.GetStream();
                
                Console.WriteLine("Połączenie TCP nawiązane.");

                await SendClientIdentification();
                await SendLogicalConnection();

                var response = await ReceiveResponse();
                return ProcessConnectionResponse(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd połączenia: {ex.Message}");
                return false;
            }
        }

        private async Task SendClientIdentification()
        {
            var clientId = Encoding.ASCII.GetBytes("FISAPICLIENT     ");
            await stream.WriteAsync(clientId, 0, CLIENT_ID_LENGTH);
            Console.WriteLine("Identyfikator klienta wysłany.");
        }

        private async Task SendLogicalConnection()
        {
            var message = BuildLogicalConnectionMessage();
            await stream.WriteAsync(message, 0, message.Length);
            Console.WriteLine($"Żądanie logicznego połączenia (1100) wysłane. Rozmiar: {message.Length} bajtów");
        }

        private byte[] BuildLogicalConnectionMessage()
        {
            var dataBuilder = new List<byte>();

            // User Number (3 bajty)
            var userBytes = Encoding.ASCII.GetBytes(user.PadLeft(3, '0'));
            dataBuilder.AddRange(userBytes);

            // Password (16 bajtów)
            var passwordBytes = Encoding.ASCII.GetBytes(password.PadRight(16, ' '));
            dataBuilder.AddRange(passwordBytes);

            // Filler (7 bajtów)
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(new string(' ', 7)));

            // Key/Value pary
            dataBuilder.AddRange(EncodeField("15"));
            dataBuilder.AddRange(EncodeField("V5"));
            dataBuilder.AddRange(EncodeField("26"));
            dataBuilder.AddRange(EncodeField(user));

            var data = dataBuilder.ToArray();
            var totalLength = LG_LENGTH + HEADER_LENGTH + data.Length + FOOTER_LENGTH;
            var message = new byte[totalLength];
            var offset = 0;

            // Długość wiadomości
            message[offset++] = (byte)(totalLength % 256);
            message[offset++] = (byte)(totalLength / 256);

            // Nagłówek
            BuildHeader(message, ref offset, data.Length, 1100);

            // Dane
            Array.Copy(data, 0, message, offset, data.Length);
            offset += data.Length;

            // Stopka
            message[offset++] = (byte)' ';
            message[offset++] = (byte)' ';
            message[offset++] = ETX;

            return message;
        }

        private void BuildHeader(byte[] message, ref int offset, int dataLength, int requestNumber)
        {
            // STX
            message[offset++] = STX;

            // API version
            message[offset++] = (byte)'0';

            // Request size
            var requestSize = (HEADER_LENGTH + dataLength + FOOTER_LENGTH).ToString("D5");
            Array.Copy(Encoding.ASCII.GetBytes(requestSize), 0, message, offset, 5);
            offset += 5;

            // Called logical identifier
            var calledId = subnode.PadLeft(5, '0');
            Array.Copy(Encoding.ASCII.GetBytes(calledId), 0, message, offset, 5);
            offset += 5;

            // Filler
            Array.Copy(Encoding.ASCII.GetBytes("     "), 0, message, offset, 5);
            offset += 5;

            // Calling logical identifier
            Array.Copy(Encoding.ASCII.GetBytes(callingId), 0, message, offset, 5);
            offset += 5;

            // Filler
            Array.Copy(Encoding.ASCII.GetBytes("  "), 0, message, offset, 2);
            offset += 2;

            // Request number
            var reqNum = requestNumber.ToString("D5");
            Array.Copy(Encoding.ASCII.GetBytes(reqNum), 0, message, offset, 5);
            offset += 5;

            // Filler
            Array.Copy(Encoding.ASCII.GetBytes("   "), 0, message, offset, 3);
            offset += 3;
        }

        private byte[] EncodeField(string value)
        {
            var valueBytes = Encoding.ASCII.GetBytes(value);
            var encoded = new byte[valueBytes.Length + 1];
            encoded[0] = (byte)(valueBytes.Length + 32);
            Array.Copy(valueBytes, 0, encoded, 1, valueBytes.Length);
            return encoded;
        }

        private async Task<byte[]> ReceiveResponse()
        {
            var buffer = new byte[32000];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            
            var response = new byte[bytesRead];
            Array.Copy(buffer, response, bytesRead);
            
            Console.WriteLine($"Odebrano odpowiedź: {bytesRead} bajtów");
            
            return response;
        }

        private bool ProcessConnectionResponse(byte[] response)
        {
            if (response.Length < LG_LENGTH + HEADER_LENGTH)
            {
                Console.WriteLine("Odpowiedź za krótka");
                return false;
            }

            // Znajdowanie STX
            var stxPos = -1;
            for (int i = 0; i < response.Length; i++)
            {
                if (response[i] == STX)
                {
                    stxPos = i;
                    break;
                }
            }

            if (stxPos == -1)
            {
                Console.WriteLine("Nie znaleziono STX w odpowiedzi");
                return false;
            }

            Console.WriteLine($"STX znaleziony na pozycji: {stxPos}");

            // Odczytanie nagłówka
            if (stxPos + 32 <= response.Length)
            {
                var calledIdStr = Encoding.ASCII.GetString(response, stxPos + 7, 5);
                var requestNumberStr = Encoding.ASCII.GetString(response, stxPos + 24, 5);
                
                callingId = calledIdStr.Trim();

                if (int.TryParse(requestNumberStr, out int requestNumber))
                {
                    Console.WriteLine($"Numer żądania odpowiedzi: {requestNumber}");

                    if (requestNumber == 1100)
                    {
                        Console.WriteLine("Połączenie logiczne nawiązane pomyślnie!");
                        Console.WriteLine($"Używany Calling ID: '{callingId}'");
                        return true;
                    }
                    else if (requestNumber == 1102)
                    {
                        Console.WriteLine("Połączenie logiczne odrzucone.");
                        ProcessConnectionError(response);
                        return false;
                    }
                }
            }

            Console.WriteLine("Nieznany format odpowiedzi");
            return false;
        }

        private void ProcessConnectionError(byte[] response)
        {
            Console.WriteLine("Szczegóły błędu połączenia:");
            
            var stxPos = -1;
            for (int i = 0; i < response.Length; i++)
            {
                if (response[i] == STX)
                {
                    stxPos = i;
                    break;
                }
            }

            if (stxPos != -1)
            {
                var dataStart = stxPos + 32;
                var dataEnd = response.Length - FOOTER_LENGTH;
                
                if (dataStart < dataEnd)
                {
                    try
                    {
                        var userNumber = Encoding.ASCII.GetString(response, dataStart, 3).Trim();
                        Console.WriteLine($"User Number: {userNumber}");
                        
                        var password = Encoding.ASCII.GetString(response, dataStart + 3, 16).Trim();
                        Console.WriteLine($"Password: {password}");
                        
                        var reasonPos = dataStart + 3 + 16;
                        if (reasonPos < dataEnd)
                        {
                            var reason = response[reasonPos] - 32;
                            Console.WriteLine($"Reason code: {reason}");
                            
                            switch (reason)
                            {
                                case 0: Console.WriteLine("Nieprawidłowe hasło"); break;
                                case 1: Console.WriteLine("Brak miejsca w bazie połączeń logicznych"); break;
                                case 2: Console.WriteLine("Nieprawidłowy format żądania połączenia"); break;
                                case 3: Console.WriteLine("Zabroniony numer użytkownika"); break;
                                case 4: Console.WriteLine("Nieznany numer użytkownika"); break;
                                case 7: Console.WriteLine("Użytkownik już połączony"); break;
                                case 52: Console.WriteLine("Złe hasło"); break;
                                case 59: Console.WriteLine("Już połączony"); break;
                                default: Console.WriteLine($"Nieznany kod błędu: {reason}"); break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Błąd podczas przetwarzania odpowiedzi błędu: {ex.Message}");
                    }
                }
            }
        }

        public void ShowAvailableExchanges()
        {
            Console.WriteLine("\n=== DOSTĘPNE GIEŁDY ===");
            for (int i = 0; i < AvailableExchanges.Count; i++)
            {
                var exchange = AvailableExchanges[i];
                Console.WriteLine($"{i + 1}. {exchange.Name} - {exchange.Description}");
                Console.WriteLine($"   GLID: {exchange.GetGLID()}");
            }
        }

        public async Task SendDictionaryRequest(int exchangeIndex = 0)
        {
            if (stream == null)
            {
                Console.WriteLine("Brak połączenia!");
                return;
            }

            if (exchangeIndex < 0 || exchangeIndex >= AvailableExchanges.Count)
            {
                Console.WriteLine("Nieprawidłowy indeks giełdy!");
                return;
            }

            var selectedExchange = AvailableExchanges[exchangeIndex];
            Console.WriteLine($"Wysyłanie żądania Dictionary dla: {selectedExchange.Name}");
            Console.WriteLine($"Calling ID: '{callingId}', GLID: {selectedExchange.GetGLID()}");
            
            var message = BuildDictionaryMessage(selectedExchange.GetGLID());
            await stream.WriteAsync(message, 0, message.Length);
            
            Console.WriteLine($"Żądanie Dictionary wysłane. Rozmiar: {message.Length} bajtów");
        }

        private bool lastResponseHadInstruments = false;

        public async Task SendDictionaryRequestAll()
        {
            if (stream == null)
            {
                Console.WriteLine("Brak połączenia!");
                return;
            }

            var exchanges = new[] { 40, 330, 331, 332 };
            var totalRequests = 0;
            var successfulRequests = 0;
            var marketsWithInstruments = new List<string>();

            Console.WriteLine("=== POBIERANIE DICTIONARY DLA WSZYSTKICH GIEŁD I RYNKÓW ===");
            
            foreach (var exchange in exchanges)
            {
                var exchangeName = GetExchangeName(exchange);
                Console.WriteLine($"\n--- {exchangeName} (Exchange {exchange}) ---");
                
                for (int market = 1; market <= 20; market++)
                {
                    var glid = $"{exchange:D4}00{market:D3}000";
                    
                    Console.Write($"Sprawdzanie {exchangeName} rynek {market:D2} (GLID: {glid})... ");
                    
                    try
                    {
                        lastResponseHadInstruments = false;
                        
                        var message = BuildDictionaryMessage(glid);
                        await stream.WriteAsync(message, 0, message.Length);
                        totalRequests++;
                        
                        // Czekamy na odpowiedź z krótszym timeoutem
                        if (await WaitForResponse(2000))
                        {
                            successfulRequests++;
                            if (lastResponseHadInstruments)
                            {
                                marketsWithInstruments.Add($"{exchangeName} rynek {market}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Timeout");
                        }
                        
                        // Małe opóźnienie między żądaniami
                        await Task.Delay(50);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Błąd: {ex.Message}");
                    }
                }
            }
            
            Console.WriteLine($"\n=== PODSUMOWANIE ===");
            Console.WriteLine($"Wysłano żądań: {totalRequests}");
            Console.WriteLine($"Otrzymano odpowiedzi: {successfulRequests}");
            Console.WriteLine($"Nie odpowiedziały: {totalRequests - successfulRequests}");
            
            if (marketsWithInstruments.Count > 0)
            {
                Console.WriteLine($"\nRynki z instrumentami ({marketsWithInstruments.Count}):");
                foreach (var market in marketsWithInstruments)
                {
                    Console.WriteLine($"  - {market}");
                }
            }
            else
            {
                Console.WriteLine("\nBrak rynków z instrumentami");
            }
        }

        private string GetExchangeName(int exchange)
        {
            return exchange switch
            {
                40 => "WSE",
                330 => "SMTF",
                331 => "BSRM",
                332 => "BSMTF",
                _ => $"Exchange_{exchange}"
            };
        }

        private byte[] BuildDictionaryMessage(string glid)
        {
            var dataBuilder = new List<byte>();

            // H0 - Number of GLID
            dataBuilder.AddRange(Encoding.ASCII.GetBytes("00001"));

            // H1 - GLID
            dataBuilder.AddRange(EncodeField(glid));

            var data = dataBuilder.ToArray();
            
            var totalLength = LG_LENGTH + HEADER_LENGTH + data.Length + FOOTER_LENGTH;
            var message = new byte[totalLength];
            var offset = 0;

            // Długość wiadomości
            message[offset++] = (byte)(totalLength % 256);
            message[offset++] = (byte)(totalLength / 256);

            // Nagłówek
            BuildHeader(message, ref offset, data.Length, 5108);

            // Dane
            Array.Copy(data, 0, message, offset, data.Length);
            offset += data.Length;

            // Stopka
            message[offset++] = (byte)' ';
            message[offset++] = (byte)' ';
            message[offset++] = ETX;

            return message;
        }

        public async Task<bool> WaitForResponse(int timeoutMs = 5000)
        {
            if (stream == null) return false;

            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (stream.DataAvailable)
                {
                    var response = await ReceiveResponse();
                    await ProcessIncomingMessage(response);
                    return true;
                }
                await Task.Delay(50);
            }
            
            return false; // Usunięto komunikat timeout dla dict all
        }

        public async Task<bool> WaitForResponseWithTimeout(int timeoutMs = 5000)
        {
            if (stream == null) return false;

            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (stream.DataAvailable)
                {
                    var response = await ReceiveResponse();
                    await ProcessIncomingMessage(response);
                    return true;
                }
                await Task.Delay(50);
            }
            
            Console.WriteLine($"Timeout: Brak odpowiedzi po {timeoutMs}ms");
            return false;
        }

        private async Task ProcessIncomingMessage(byte[] response)
        {
            if (response.Length < LG_LENGTH + HEADER_LENGTH)
            {
                Console.WriteLine("Otrzymano zbyt krótką wiadomość");
                return;
            }

            var stxPos = -1;
            for (int i = 0; i < response.Length; i++)
            {
                if (response[i] == STX)
                {
                    stxPos = i;
                    break;
                }
            }

            if (stxPos == -1)
            {
                Console.WriteLine("Nie znaleziono STX w wiadomości");
                return;
            }

            var requestNumberStr = Encoding.ASCII.GetString(response, stxPos + 24, 5);
            if (int.TryParse(requestNumberStr, out int requestNumber))
            {
                switch (requestNumber)
                {
                    case 1044:
                        Console.WriteLine("Brak instrumentów");
                        lastResponseHadInstruments = false;
                        break;
                    case 5108:
                        ProcessDictionaryResponse(response, stxPos);
                        break;
                    default:
                        Console.WriteLine($"Nieobsługiwany typ wiadomości: {requestNumber}");
                        break;
                }
            }
        }

        private void ProcessDictionaryResponse(byte[] response, int stxPos)
        {
            try
            {
                var dataStart = stxPos + 32;
                var dataEnd = response.Length - FOOTER_LENGTH;
                
                if (dataStart < dataEnd)
                {
                    var responseData = new byte[dataEnd - dataStart];
                    Array.Copy(response, dataStart, responseData, 0, responseData.Length);
                    
                    var position = 0;
                    
                    // H0 - Chaining
                    var chaining = responseData[position];
                    position++;
                    
                    // H1 - Number of GLID
                    var numberOfGlidStr = Encoding.ASCII.GetString(responseData, position, 5);
                    var numberOfGlid = int.Parse(numberOfGlidStr);
                    position += 5;
                    
                    // Jeśli brak instrumentów, wyświetl krótką informację
                    if (numberOfGlid == 0)
                    {
                        Console.WriteLine("Brak instrumentów");
                        lastResponseHadInstruments = false;
                        return;
                    }
                    
                    Console.WriteLine($"✓ Znaleziono {numberOfGlid} instrumentów");
                    lastResponseHadInstruments = true;
                    
                    // Przetwarzanie każdego GLID - pokazuj szczegóły tylko dla pierwszych 3
                    for (int glidIndex = 0; glidIndex < Math.Min(numberOfGlid, 3); glidIndex++)
                    {
                        Console.WriteLine($"  Instrument {glidIndex + 1}:");
                        
                        for (int fieldIndex = 0; fieldIndex < 5; fieldIndex++)
                        {
                            if (position >= responseData.Length)
                                break;
                                
                            var fieldLength = responseData[position] - 32;
                            if (fieldLength > 0 && position + 1 + fieldLength <= responseData.Length)
                            {
                                var fieldValue = Encoding.ASCII.GetString(responseData, position + 1, fieldLength);
                                
                                switch (fieldIndex)
                                {
                                    case 0:
                                        if (fieldValue.Length > 12)
                                        {
                                            var symbol = fieldValue.Substring(12);
                                            Console.WriteLine($"    Symbol: {symbol}");
                                        }
                                        break;
                                    case 1:
                                        Console.WriteLine($"    Nazwa: {fieldValue}");
                                        break;
                                    case 3:
                                        Console.WriteLine($"    ISIN: {fieldValue}");
                                        break;
                                }
                                
                                position += 1 + fieldLength;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    
                    // Przeskocz pozostałe instrumenty jeśli ich więcej niż 3
                    if (numberOfGlid > 3)
                    {
                        Console.WriteLine($"  ... i {numberOfGlid - 3} więcej instrumentów");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd: {ex.Message}");
                lastResponseHadInstruments = false;
            }
        }

        private void ProcessUnknownStockCode(byte[] response, int stxPos)
        {
            Console.WriteLine("=== NIEZNANY KOD AKCJI ===");
            
            var dataStart = stxPos + 32;
            var dataEnd = response.Length - FOOTER_LENGTH;
            
            if (dataStart < dataEnd)
            {
                var responseData = new byte[dataEnd - dataStart];
                Array.Copy(response, dataStart, responseData, 0, responseData.Length);
                
                if (responseData.Length > 0)
                {
                    var fieldLength = responseData[0] - 32;
                    if (fieldLength > 0 && 1 + fieldLength <= responseData.Length)
                    {
                        var unknownStock = Encoding.ASCII.GetString(responseData, 1, fieldLength);
                        Console.WriteLine($"Nieznany kod akcji: {unknownStock}");
                    }
                }
            }
        }

        public bool IsConnected()
        {
            return tcpClient != null && tcpClient.Connected && stream != null;
        }

        public void Disconnect()
        {
            stream?.Close();
            tcpClient?.Close();
            Console.WriteLine("Rozłączono.");
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            var client = new FISApiClient(
                host: "172.31.136.4",
                port: 25003,
                user: "401",
                password: "glglgl",
                node: "5000",
                subnode: "4000"
            );

            try
            {
                var connected = await client.ConnectAsync();
                
                if (connected)
                {
                    Console.WriteLine("Połączenie nawiązane pomyślnie!");
                    
                    await Task.Delay(1000);
                    
                    // Pokaż dostępne giełdy
                    client.ShowAvailableExchanges();
                    
                    Console.WriteLine("\nDostępne komendy:");
                    Console.WriteLine("  exchanges - pokaż dostępne giełdy");
                    Console.WriteLine("  dict <numer> - żądanie Dictionary dla wybranej giełdy (np. dict 1)");
                    Console.WriteLine("  dict all - żądanie Dictionary dla wszystkich giełd i rynków (1-20)");
                    Console.WriteLine("  test - test połączenia");
                    Console.WriteLine("  quit - zakończenie");
                    
                    while (true)
                    {
                        Console.Write("\n> ");
                        var input = Console.ReadLine();
                        
                        if (input?.ToLower() == "quit")
                        {
                            break;
                        }
                        else if (input?.ToLower() == "exchanges")
                        {
                            client.ShowAvailableExchanges();
                        }
                        else if (input?.ToLower().StartsWith("dict") == true)
                        {
                            var parts = input.Split(' ');
                            if (parts.Length > 1)
                            {
                                if (parts[1].ToLower() == "all")
                                {
                                    await client.SendDictionaryRequestAll();
                                }
                                else if (int.TryParse(parts[1], out int exchangeIndex))
                                {
                                    await client.SendDictionaryRequest(exchangeIndex - 1); // -1 bo indeks od 0
                                    await client.WaitForResponseWithTimeout(10000);
                                }
                                else
                                {
                                    Console.WriteLine("Użyj: dict <numer giełdy> lub dict all");
                                    client.ShowAvailableExchanges();
                                }
                            }
                            else
                            {
                                Console.WriteLine("Użyj: dict <numer giełdy> lub dict all");
                                client.ShowAvailableExchanges();
                            }
                        }
                        else if (input?.ToLower() == "test")
                        {
                            Console.WriteLine($"Połączenie aktywne: {client.IsConnected()}");
                        }
                        else if (!string.IsNullOrEmpty(input))
                        {
                            Console.WriteLine("Nieznana komenda.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd: {ex.Message}");
            }
            finally
            {
                client.Disconnect();
            }
        }
    }
}