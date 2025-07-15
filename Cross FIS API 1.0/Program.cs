using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FISApiClient
{
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
        private readonly string callingId;

        // Stałe protokołu
        private const byte STX = 2;
        private const byte ETX = 3;
        private const int HEADER_LENGTH = 32;
        private const int FOOTER_LENGTH = 3;
        private const int LG_LENGTH = 2;
        private const int CLIENT_ID_LENGTH = 16;

        public FISApiClient(string host, int port, string user, string password, string node, string subnode)
        {
            this.host = host;
            this.port = port;
            this.user = user;
            this.password = password;
            this.node = node;
            this.subnode = subnode;
            this.callingId = "00000"; // Domyślny calling ID
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                Console.WriteLine($"Łączenie z {host}:{port}...");
                
                // 1. Nawiązanie połączenia TCP
                tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(host, port);
                stream = tcpClient.GetStream();
                
                Console.WriteLine("Połączenie TCP nawiązane.");

                // 2. Wysłanie identyfikatora klienta (16 bajtów)
                await SendClientIdentification();

                // 3. Wysłanie żądania logicznego połączenia (1100)
                await SendLogicalConnection();

                // 4. Odebranie odpowiedzi
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
            // Wysłanie 16-bajtowego identyfikatora klienta
            var clientId = Encoding.ASCII.GetBytes("FISAPICLIENT     "); // 16 bajtów
            await stream.WriteAsync(clientId, 0, CLIENT_ID_LENGTH);
            Console.WriteLine("Identyfikator klienta wysłany.");
        }

        private async Task SendLogicalConnection()
        {
            var message = BuildLogicalConnectionMessage();
            await stream.WriteAsync(message, 0, message.Length);
            Console.WriteLine($"Żądanie logicznego połączenia (1100) wysłane. Rozmiar: {message.Length} bajtów");
            
            // Debug: wyświetl wiadomość w hex
            Console.WriteLine($"Wiadomość hex: {BitConverter.ToString(message)}");
        }

        private byte[] BuildLogicalConnectionMessage()
        {
            // Przygotowanie danych dla żądania 1100
            var dataBuilder = new List<byte>();

            // User Number (3 bajty) - dopełnione zerami z lewej strony
            var userBytes = Encoding.ASCII.GetBytes(user.PadLeft(3, '0'));
            dataBuilder.AddRange(userBytes);

            // Password (16 bajtów) - dopełnione spacjami z prawej strony
            var passwordBytes = Encoding.ASCII.GetBytes(password.PadRight(16, ' '));
            dataBuilder.AddRange(passwordBytes);

            // Filler (7 bajtów) - spacje
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(new string(' ', 7)));

            // Key/Value pary (opcjonalne)
            // Key 15 - Server version
            dataBuilder.AddRange(EncodeField("15"));
            dataBuilder.AddRange(EncodeField("V5"));

            // Key 26 - Username (Connection ID)
            dataBuilder.AddRange(EncodeField("26"));
            dataBuilder.AddRange(EncodeField(user));

            var data = dataBuilder.ToArray();

            // Budowanie kompletnej wiadomości
            var totalLength = LG_LENGTH + HEADER_LENGTH + data.Length + FOOTER_LENGTH;
            var message = new byte[totalLength];
            var offset = 0;

            // 1. Długość wiadomości (LG) - 2 bajty
            message[offset++] = (byte)(totalLength % 256);
            message[offset++] = (byte)(totalLength / 256);

            // 2. Nagłówek (32 bajty)
            BuildHeader(message, ref offset, data.Length, 1100);

            // 3. Dane
            Array.Copy(data, 0, message, offset, data.Length);
            offset += data.Length;

            // 4. Stopka (3 bajty)
            message[offset++] = (byte)' ';
            message[offset++] = (byte)' ';
            message[offset++] = ETX;

            return message;
        }

        private void BuildHeader(byte[] message, ref int offset, int dataLength, int requestNumber)
        {
            var headerStart = offset;

            // STX
            message[offset++] = STX;

            // API version - '0' dla SLC V5, ' ' dla V4
            message[offset++] = (byte)'0';

            // Request size (5 bajtów)
            var requestSize = (HEADER_LENGTH + dataLength + FOOTER_LENGTH).ToString("D5");
            Array.Copy(Encoding.ASCII.GetBytes(requestSize), 0, message, offset, 5);
            offset += 5;

            // Called logical identifier (5 bajtów) - identyfikator serwera docelowego
            var calledId = subnode.PadLeft(5, '0');
            Array.Copy(Encoding.ASCII.GetBytes(calledId), 0, message, offset, 5);
            offset += 5;

            // Filler (5 bajtów)
            Array.Copy(Encoding.ASCII.GetBytes("     "), 0, message, offset, 5);
            offset += 5;

            // Calling logical identifier (5 bajtów)
            Array.Copy(Encoding.ASCII.GetBytes(callingId), 0, message, offset, 5);
            offset += 5;

            // Filler (2 bajty)
            Array.Copy(Encoding.ASCII.GetBytes("  "), 0, message, offset, 2);
            offset += 2;

            // Request number (5 bajtów)
            var reqNum = requestNumber.ToString("D5");
            Array.Copy(Encoding.ASCII.GetBytes(reqNum), 0, message, offset, 5);
            offset += 5;

            // Filler (3 bajty)
            Array.Copy(Encoding.ASCII.GetBytes("   "), 0, message, offset, 3);
            offset += 3;
        }

        private byte[] EncodeField(string value)
        {
            // Kodowanie FIS: pierwszy bajt = długość + 32, potem dane
            var valueBytes = Encoding.ASCII.GetBytes(value);
            var encoded = new byte[valueBytes.Length + 1];
            encoded[0] = (byte)(valueBytes.Length + 32);
            Array.Copy(valueBytes, 0, encoded, 1, valueBytes.Length);
            return encoded;
        }

        private (string key, string value) DecodeField(byte[] data, ref int position)
        {
            if (position >= data.Length)
                return (null, null);

            try
            {
                // Pierwszy bajt to długość + 32
                var keyLength = data[position] - 32;
                if (keyLength <= 0 || position + 1 + keyLength > data.Length)
                    return (null, null);

                var key = Encoding.ASCII.GetString(data, position + 1, keyLength);
                position += 1 + keyLength;

                if (position >= data.Length)
                    return (key, null);

                // Drugi bajt to długość wartości + 32
                var valueLength = data[position] - 32;
                if (valueLength <= 0 || position + 1 + valueLength > data.Length)
                    return (key, null);

                var value = Encoding.ASCII.GetString(data, position + 1, valueLength);
                position += 1 + valueLength;

                return (key, value);
            }
            catch
            {
                return (null, null);
            }
        }

        private async Task<byte[]> ReceiveResponse()
        {
            var buffer = new byte[32000]; // Maksymalny rozmiar bufora
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            
            var response = new byte[bytesRead];
            Array.Copy(buffer, response, bytesRead);
            
            Console.WriteLine($"Odebrano odpowiedź: {bytesRead} bajtów");
            Console.WriteLine($"Odpowiedź hex: {BitConverter.ToString(response)}");
            
            return response;
        }

        private bool ProcessConnectionResponse(byte[] response)
        {
            if (response.Length < LG_LENGTH + HEADER_LENGTH)
            {
                Console.WriteLine("Odpowiedź za krótka");
                return false;
            }

            // Odczytanie długości wiadomości
            var messageLength = response[0] + (response[1] * 256);
            Console.WriteLine($"Długość wiadomości: {messageLength}");

            // Znajdowanie STX w nagłówku
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

            // Szczegółowa analiza nagłówka
            if (stxPos + 32 <= response.Length)
            {
                var apiVersion = (char)response[stxPos + 1];
                var requestSizeStr = Encoding.ASCII.GetString(response, stxPos + 2, 5);
                var calledIdStr = Encoding.ASCII.GetString(response, stxPos + 7, 5);
                var callingIdStr = Encoding.ASCII.GetString(response, stxPos + 17, 5);
                var requestNumberStr = Encoding.ASCII.GetString(response, stxPos + 24, 5);

                Console.WriteLine($"API Version: {apiVersion}");
                Console.WriteLine($"Request Size: {requestSizeStr}");
                Console.WriteLine($"Called ID: {calledIdStr}");
                Console.WriteLine($"Calling ID: {callingIdStr}");
                Console.WriteLine($"Request Number: {requestNumberStr}");

                if (int.TryParse(requestNumberStr, out int requestNumber))
                {
                    Console.WriteLine($"Numer żądania odpowiedzi: {requestNumber}");

                    if (requestNumber == 1100)
                    {
                        Console.WriteLine("Połączenie logiczne nawiązane pomyślnie!");
                        
                        // Analiza dodatkowych danych w odpowiedzi
                        var dataStart = stxPos + 32;
                        var dataEnd = response.Length - FOOTER_LENGTH;
                        if (dataStart < dataEnd)
                        {
                            var responseData = new byte[dataEnd - dataStart];
                            Array.Copy(response, dataStart, responseData, 0, responseData.Length);
                            
                            Console.WriteLine($"Dane odpowiedzi (raw): {BitConverter.ToString(responseData)}");
                            
                            // Przetwarzanie Key/Value pary
                            Console.WriteLine("Przetwarzanie Key/Value par:");
                            var position = 0;
                            
                            // Pomijamy początkowe spacje/filler
                            while (position < responseData.Length && responseData[position] == 0x20)
                            {
                                position++;
                            }
                            
                            while (position < responseData.Length)
                            {
                                var (key, value) = DecodeField(responseData, ref position);
                                if (key != null && value != null)
                                {
                                    Console.WriteLine($"  Key '{key}' = '{value}'");
                                    
                                    // Interpretacja znanych kluczy
                                    switch (key)
                                    {
                                        case "15":
                                            Console.WriteLine($"    Server version: {value}");
                                            break;
                                        case "26":
                                            Console.WriteLine($"    Username: {value}");
                                            break;
                                        default:
                                            Console.WriteLine($"    Nieznany klucz: {key}");
                                            break;
                                    }
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                        
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
            // Przetwarzanie odpowiedzi 1102 (połączenie odrzucone)
            Console.WriteLine("Szczegóły błędu połączenia:");
            
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

            if (stxPos != -1)
            {
                // Dane znajdują się po nagłówku (32 bajty od STX)
                var dataStart = stxPos + 32;
                var dataEnd = response.Length - FOOTER_LENGTH;
                
                if (dataStart < dataEnd)
                {
                    try
                    {
                        // Pierwsze 3 bajty to User Number
                        var userNumber = Encoding.ASCII.GetString(response, dataStart, 3).Trim();
                        Console.WriteLine($"User Number: {userNumber}");
                        
                        // Następne 16 bajtów to Password
                        var password = Encoding.ASCII.GetString(response, dataStart + 3, 16).Trim();
                        Console.WriteLine($"Password: {password}");
                        
                        // Następny bajt to Reason (kod błędu)
                        var reasonPos = dataStart + 3 + 16;
                        if (reasonPos < dataEnd)
                        {
                            var reason = response[reasonPos] - 32; // Odejmujemy 32 (kodowanie GL_C)
                            Console.WriteLine($"Reason code: {reason}");
                            
                            // Interpretacja kodu błędu
                            switch (reason)
                            {
                                case 0: Console.WriteLine("Nieprawidłowe hasło"); break;
                                case 1: Console.WriteLine("Brak miejsca w bazie połączeń logicznych"); break;
                                case 2: Console.WriteLine("Nieprawidłowy format żądania połączenia"); break;
                                case 3: Console.WriteLine("Zabroniony numer użytkownika"); break;
                                case 4: Console.WriteLine("Nieznany numer użytkownika"); break;
                                case 5: Console.WriteLine("Zabroniony numer brokera"); break;
                                case 6: Console.WriteLine("Nieznany numer brokera"); break;
                                case 7: Console.WriteLine("Użytkownik już połączony"); break;
                                case 8: Console.WriteLine("Problem z połączeniem"); break;
                                case 9: Console.WriteLine("Nieznany odbiorca"); break;
                                case 52: Console.WriteLine("Złe hasło"); break;
                                case 53: Console.WriteLine("Osiągnięto maksymalną liczbę ID"); break;
                                case 55: Console.WriteLine("Oczekiwanie na rozłączenie"); break;
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
            
            Console.WriteLine($"Timeout: Brak odpowiedzi po {timeoutMs}ms");
            return false;
        }

        public async Task SendDictionaryRequest()
        {
            if (stream == null)
            {
                Console.WriteLine("Brak połączenia!");
                return;
            }

            Console.WriteLine("Wysyłanie żądania Dictionary (lista wszystkich symboli)...");
            
            var message = BuildDictionaryMessage();
            await stream.WriteAsync(message, 0, message.Length);
            
            Console.WriteLine($"Żądanie Dictionary wysłane. Rozmiar: {message.Length} bajtów");
            Console.WriteLine($"Wiadomość hex: {BitConverter.ToString(message)}");
            
            // Oczekiwanie na odpowiedź
            Console.WriteLine("Oczekiwanie na odpowiedź Dictionary...");
            await WaitForResponse(10000); // 10 sekund timeout
        }

        private byte[] BuildDictionaryMessage()
        {
            var dataBuilder = new List<byte>();

            // H0 - Number of GLID (1 GLID)
            dataBuilder.AddRange(Encoding.ASCII.GetBytes("00001"));

            // H1 - GLID dla WSE Cash market - spróbujmy różnych formatów
            // Format 1: Pełny GLID
            var wseCashGlid = "00400000"; // WSE Exchange(40) + Source(00) + Market(002) + Sub-market(000)
            
            // Możemy też spróbować:
            // var wseCashGlid = "0040"; // Tylko Exchange
            // var wseCashGlid = "00400000"; // Exchange + Source
            // var wseCashGlid = "004000000002"; // Exchange + Source + Market
            
            Console.WriteLine($"Używany GLID: {wseCashGlid}");
            dataBuilder.AddRange(EncodeField(wseCashGlid));

            var data = dataBuilder.ToArray();
            Console.WriteLine($"Dane Dictionary: {BitConverter.ToString(data)}");
            
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

        public async Task SendDictionaryRequestRefresh()
        {
            if (stream == null)
            {
                Console.WriteLine("Brak połączenia!");
                return;
            }

            Console.WriteLine("Wysyłanie żądania Dictionary Refresh (5109)...");
            
            var message = BuildDictionaryRefreshMessage();
            await stream.WriteAsync(message, 0, message.Length);
            
            Console.WriteLine($"Żądanie Dictionary Refresh wysłane. Rozmiar: {message.Length} bajtów");
            Console.WriteLine($"Wiadomość hex: {BitConverter.ToString(message)}");
            
            // Oczekiwanie na odpowiedź
            Console.WriteLine("Oczekiwanie na odpowiedź Dictionary Refresh...");
            await WaitForResponse(10000); // 10 sekund timeout
        }

        private byte[] BuildDictionaryRefreshMessage()
        {
            var dataBuilder = new List<byte>();

            // H0 - Number of GLID (1 GLID)
            dataBuilder.AddRange(Encoding.ASCII.GetBytes("00001"));

            // H1 - GLID dla WSE Cash market
            var wseCashGlid = "004000002000";
            Console.WriteLine($"Używany GLID dla refresh: {wseCashGlid}");
            dataBuilder.AddRange(EncodeField(wseCashGlid));

            var data = dataBuilder.ToArray();
            var totalLength = LG_LENGTH + HEADER_LENGTH + data.Length + FOOTER_LENGTH;
            var message = new byte[totalLength];
            var offset = 0;

            // Długość wiadomości
            message[offset++] = (byte)(totalLength % 256);
            message[offset++] = (byte)(totalLength / 256);

            // Nagłówek - używamy 5109 (refresh) zamiast 5108
            BuildHeader(message, ref offset, data.Length, 5109);

            // Dane
            Array.Copy(data, 0, message, offset, data.Length);
            offset += data.Length;

            // Stopka
            message[offset++] = (byte)' ';
            message[offset++] = (byte)' ';
            message[offset++] = ETX;

            return message;
        }

        public async Task SendDictionaryRequestAllMarkets()
        {
            if (stream == null)
            {
                Console.WriteLine("Brak połączenia!");
                return;
            }

            Console.WriteLine("Wysyłanie żądania Dictionary dla wszystkich rynków WSE...");
            
            var message = BuildDictionaryMessageAllMarkets();
            await stream.WriteAsync(message, 0, message.Length);
            
            Console.WriteLine($"Żądanie Dictionary (wszystkie rynki) wysłane. Rozmiar: {message.Length} bajtów");
            Console.WriteLine($"Wiadomość hex: {BitConverter.ToString(message)}");
            
            // Oczekiwanie na odpowiedź
            Console.WriteLine("Oczekiwanie na odpowiedź Dictionary All Markets...");
            await WaitForResponse(15000); // 15 sekund timeout dla wszystkich rynków
        }

        public async Task SendSimpleTest()
        {
            if (stream == null)
            {
                Console.WriteLine("Brak połączenia!");
                return;
            }

            Console.WriteLine("Wysyłanie prostego testu Dictionary z minimalnym GLID...");
            
            var message = BuildSimpleTestMessage();
            await stream.WriteAsync(message, 0, message.Length);
            
            Console.WriteLine($"Prosty test wysłany. Rozmiar: {message.Length} bajtów");
            Console.WriteLine($"Wiadomość hex: {BitConverter.ToString(message)}");
            
            await WaitForResponse(5000);
        }

        private byte[] BuildSimpleTestMessage()
        {
            var dataBuilder = new List<byte>();

            // H0 - Number of GLID (1 GLID)
            dataBuilder.AddRange(Encoding.ASCII.GetBytes("00001"));

            // H1 - Prosty GLID - tylko numer exchange
            var simpleGlid = "0040"; // Tylko WSE exchange number
            Console.WriteLine($"Używany prosty GLID: {simpleGlid}");
            dataBuilder.AddRange(EncodeField(simpleGlid));

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

        private byte[] BuildDictionaryMessageAllMarkets()
        {
            var dataBuilder = new List<byte>();

            // Wszystkie dostępne rynki WSE według dokumentacji
            var wseMarkets = new string[]
            {
                "004000001000", // Bonds
                "004000002000", // Cash
                "004000003000", // Options
                "004000004000", // Future
                "004000005000", // Index
                "004000009000", // OPCVM
                "004000016000", // Growth market (EURNM)
                "004000017000", // Future Indices
                "004000020000"  // Warrants
            };

            // H0 - Number of GLID
            dataBuilder.AddRange(Encoding.ASCII.GetBytes($"{wseMarkets.Length:D5}"));

            // H1 - GLIDs dla wszystkich rynków
            foreach (var glid in wseMarkets)
            {
                dataBuilder.AddRange(EncodeField(glid));
            }

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

        public async Task StartListening()
        {
            if (stream == null)
            {
                Console.WriteLine("Brak połączenia!");
                return;
            }

            Console.WriteLine("Rozpoczynanie nasłuchiwania wiadomości...");
            
            try
            {
                while (tcpClient.Connected)
                {
                    try
                    {
                        // Sprawdzenie czy dane są dostępne
                        if (stream.DataAvailable)
                        {
                            Console.WriteLine("Dane dostępne do odczytu...");
                            var response = await ReceiveResponse();
                            await ProcessIncomingMessage(response);
                        }
                        
                        // Małe opóźnienie aby nie obciążać CPU
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Błąd podczas przetwarzania wiadomości: {ex.Message}");
                        await Task.Delay(1000);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas nasłuchiwania: {ex.Message}");
            }
        }

        private async Task ProcessIncomingMessage(byte[] response)
        {
            if (response.Length < LG_LENGTH + HEADER_LENGTH)
            {
                Console.WriteLine("Otrzymano zbyt krótką wiadomość");
                return;
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
                Console.WriteLine("Nie znaleziono STX w wiadomości");
                return;
            }

            // Odczytanie numeru żądania
            var requestNumberStr = Encoding.ASCII.GetString(response, stxPos + 24, 5);
            if (int.TryParse(requestNumberStr, out int requestNumber))
            {
                Console.WriteLine($"Otrzymano wiadomość typu: {requestNumber}");
                
                switch (requestNumber)
                {
                    case 1000:
                        ProcessStockWatchResponse(response, stxPos);
                        break;
                    case 1003:
                        ProcessStockWatchUpdate(response, stxPos);
                        break;
                    case 1044:
                        ProcessUnknownStockCode(response, stxPos);
                        break;
                    case 5108:
                        ProcessDictionaryResponse(response, stxPos);
                        break;
                    case 5109:
                        ProcessDictionaryResponse(response, stxPos);
                        break;
                    case 5111:
                        ProcessDictionaryUpdateResponse(response, stxPos);
                        break;
                    default:
                        Console.WriteLine($"Nieobsługiwany typ wiadomości: {requestNumber}");
                        // Wyświetl surowe dane dla debugowania
                        Console.WriteLine($"Surowe dane wiadomości: {BitConverter.ToString(response)}");
                        break;
                }
            }
        }

        private void ProcessDictionaryResponse(byte[] response, int stxPos)
        {
            Console.WriteLine("=== ODPOWIEDŹ DICTIONARY ===");
            
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
                    Console.WriteLine($"Chaining: {chaining} ({(chaining == '0' ? "Ostatnia" : "Dalsze dane")})");
                    position++;
                    
                    // H1 - Number of GLID
                    var numberOfGlidStr = Encoding.ASCII.GetString(responseData, position, 5);
                    var numberOfGlid = int.Parse(numberOfGlidStr);
                    Console.WriteLine($"Liczba GLID: {numberOfGlid}");
                    position += 5;
                    
                    // Przetwarzanie każdego GLID
                    for (int glidIndex = 0; glidIndex < numberOfGlid; glidIndex++)
                    {
                        Console.WriteLine($"\n--- GLID {glidIndex + 1} ---");
                        
                        // Przetwarzanie 5 pól dla każdego GLID
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
                                        Console.WriteLine($"  GLID + Stockcode: {fieldValue}");
                                        // Parsowanie symbolu z GLID+Stockcode
                                        if (fieldValue.Length > 12)
                                        {
                                            var symbol = fieldValue.Substring(12);
                                            Console.WriteLine($"  Symbol: {symbol}");
                                        }
                                        break;
                                    case 1:
                                        Console.WriteLine($"  Stock name: {fieldValue}");
                                        break;
                                    case 2:
                                        Console.WriteLine($"  Local code: {fieldValue}");
                                        break;
                                    case 3:
                                        Console.WriteLine($"  ISIN code: {fieldValue}");
                                        break;
                                    case 4:
                                        Console.WriteLine($"  Quotation group: {fieldValue}");
                                        break;
                                }
                                
                                position += 1 + fieldLength;
                            }
                            else
                            {
                                Console.WriteLine($"  Pole {fieldIndex}: Błąd dekodowania");
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas przetwarzania Dictionary: {ex.Message}");
            }
        }

        private void ProcessDictionaryUpdateResponse(byte[] response, int stxPos)
        {
            Console.WriteLine("=== AKTUALIZACJA DICTIONARY ===");
            
            try
            {
                var dataStart = stxPos + 32;
                var dataEnd = response.Length - FOOTER_LENGTH;
                
                if (dataStart < dataEnd)
                {
                    var responseData = new byte[dataEnd - dataStart];
                    Array.Copy(response, dataStart, responseData, 0, responseData.Length);
                    
                    var position = 0;
                    
                    // H0 - Number of GLID
                    var numberOfGlidStr = Encoding.ASCII.GetString(responseData, position, 5);
                    var numberOfGlid = int.Parse(numberOfGlidStr);
                    Console.WriteLine($"Liczba GLID: {numberOfGlid}");
                    position += 5;
                    
                    // Przetwarzanie każdego GLID
                    for (int glidIndex = 0; glidIndex < numberOfGlid; glidIndex++)
                    {
                        Console.WriteLine($"\n--- GLID {glidIndex + 1} ---");
                        
                        // Przetwarzanie 6 pól dla aktualizacji (5111)
                        for (int fieldIndex = 0; fieldIndex < 6; fieldIndex++)
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
                                        Console.WriteLine($"  Operation type: {fieldValue}");
                                        break;
                                    case 1:
                                        Console.WriteLine($"  GLID + Stockcode: {fieldValue}");
                                        if (fieldValue.Length > 12)
                                        {
                                            var symbol = fieldValue.Substring(12);
                                            Console.WriteLine($"  Symbol: {symbol}");
                                        }
                                        break;
                                    case 2:
                                        Console.WriteLine($"  Stock name: {fieldValue}");
                                        break;
                                    case 3:
                                        Console.WriteLine($"  Local code: {fieldValue}");
                                        break;
                                    case 4:
                                        Console.WriteLine($"  ISIN code: {fieldValue}");
                                        break;
                                    case 5:
                                        Console.WriteLine($"  Quotation group: {fieldValue}");
                                        break;
                                }
                                
                                position += 1 + fieldLength;
                            }
                            else
                            {
                                Console.WriteLine($"  Pole {fieldIndex}: Błąd dekodowania");
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas przetwarzania Dictionary Update: {ex.Message}");
            }
        }

        public async Task SendStockWatchRequest(string symbol)
        {
            if (stream == null)
            {
                Console.WriteLine("Brak połączenia!");
                return;
            }

            Console.WriteLine($"Wysyłanie żądania Stock Watch dla symbolu: {symbol}");
            
            var message = BuildStockWatchMessage(symbol);
            await stream.WriteAsync(message, 0, message.Length);
            
            Console.WriteLine($"Żądanie Stock Watch wysłane. Rozmiar: {message.Length} bajtów");
        }

        private byte[] BuildStockWatchMessage(string symbol)
        {
            var dataBuilder = new List<byte>();

            // Filler (7 bajtów)
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(new string(' ', 7)));

            // GLID + Stock code - poprawiony format dla WSE
            // GLID format: [EEEE][SS][MMM][SSS] = Exchange(40) + Source(00) + Market(002) + Sub-market(000)
            var glidStock = $"004000002000{symbol}"; // WSE Cash market + symbol
            dataBuilder.AddRange(EncodeField(glidStock));

            var data = dataBuilder.ToArray();
            var totalLength = LG_LENGTH + HEADER_LENGTH + data.Length + FOOTER_LENGTH;
            var message = new byte[totalLength];
            var offset = 0;

            // Długość wiadomości
            message[offset++] = (byte)(totalLength % 256);
            message[offset++] = (byte)(totalLength / 256);

            // Nagłówek
            BuildHeader(message, ref offset, data.Length, 1000);

            // Dane
            Array.Copy(data, 0, message, offset, data.Length);
            offset += data.Length;

            // Stopka
            message[offset++] = (byte)' ';
            message[offset++] = (byte)' ';
            message[offset++] = ETX;

            return message;
        }

        private void ProcessStockWatchResponse(byte[] response, int stxPos)
        {
            Console.WriteLine("=== ODPOWIEDŹ STOCK WATCH ===");
            
            try
            {
                var dataStart = stxPos + 32;
                var dataEnd = response.Length - FOOTER_LENGTH;
                
                if (dataStart < dataEnd)
                {
                    var responseData = new byte[dataEnd - dataStart];
                    Array.Copy(response, dataStart, responseData, 0, responseData.Length);
                    
                    // Dekodowanie podstawowych pól Stock Watch
                    var position = 0;
                    
                    // H0 - Chaining
                    if (position < responseData.Length)
                    {
                        var chaining = responseData[position];
                        Console.WriteLine($"Chaining: {chaining}");
                        position++;
                    }
                    
                    // H1 - GLID + Stockcode
                    if (position < responseData.Length)
                    {
                        var fieldLength = responseData[position] - 32;
                        if (fieldLength > 0 && position + 1 + fieldLength <= responseData.Length)
                        {
                            var glidStock = Encoding.ASCII.GetString(responseData, position + 1, fieldLength);
                            Console.WriteLine($"GLID + Stock: {glidStock}");
                            position += 1 + fieldLength;
                        }
                    }
                    
                    // H2 - Filler (7 bajtów)
                    position += 7;
                    
                    // Dekodowanie pól danych
                    DecodeStockWatchFields(responseData, position);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas przetwarzania Stock Watch: {ex.Message}");
            }
        }

        private void DecodeStockWatchFields(byte[] data, int startPosition)
        {
            Console.WriteLine("--- Pola Stock Watch ---");
            
            var position = startPosition;
            var fieldNumber = 0;
            
            while (position < data.Length - 3) // -3 dla footer
            {
                try
                {
                    if (data[position] == 0x20) // Spacja - przeskocz
                    {
                        position++;
                        continue;
                    }
                    
                    var fieldLength = data[position] - 32;
                    if (fieldLength > 0 && position + 1 + fieldLength <= data.Length)
                    {
                        var fieldValue = Encoding.ASCII.GetString(data, position + 1, fieldLength);
                        
                        // Interpretacja znanych pól
                        var fieldName = GetStockWatchFieldName(fieldNumber);
                        Console.WriteLine($"  Pole {fieldNumber} ({fieldName}): {fieldValue}");
                        
                        position += 1 + fieldLength;
                        fieldNumber++;
                    }
                    else
                    {
                        position++;
                    }
                }
                catch
                {
                    position++;
                }
            }
        }

        private string GetStockWatchFieldName(int fieldNumber)
        {
            switch (fieldNumber)
            {
                case 0: return "Bid quantity";
                case 1: return "Bid price";
                case 2: return "Ask price";
                case 3: return "Ask quantity";
                case 4: return "Last traded price";
                case 5: return "Last traded quantity";
                case 6: return "Last trade time";
                case 8: return "Percentage variation";
                case 9: return "Total quantity exchanged";
                case 10: return "Opening price";
                case 11: return "High";
                case 12: return "Low";
                case 13: return "Suspension indicator";
                case 14: return "Variation sign";
                case 16: return "Closing price";
                case 34: return "Stock name";
                case 70: return "Currency";
                case 88: return "ISIN code";
                case 140: return "Trading phase";
                default: return "Unknown field";
            }
        }

        private void ProcessStockWatchUpdate(byte[] response, int stxPos)
        {
            Console.WriteLine("=== AKTUALIZACJA STOCK WATCH ===");
            // Podobne przetwarzanie jak dla 1000, ale dla aktualizacji real-time
            ProcessStockWatchResponse(response, stxPos);
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
                
                // H0 - GLID + Stockcode
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

    // Program główny
    class Program
    {
        static async Task Main(string[] args)
        {
            var client = new FISApiClient(
                host: "10.251.224.201",
                port: 29593,
                user: "401",
                password: "glglgl",
                node: "9592",
                subnode: "19592"
            );

            try
            {
                // Połączenie z serwerem
                var connected = await client.ConnectAsync();
                
                if (connected)
                {
                    Console.WriteLine("Połączenie nawiązane pomyślnie!");
                    
                    // Uruchomienie nasłuchiwania w osobnym zadaniu
                    var listeningTask = Task.Run(async () => await client.StartListening());
                    
                    // Automatyczne pobranie listy symboli
                    Console.WriteLine("Pobieranie listy dostępnych symboli...");
                    await client.SendDictionaryRequest();
                    
                    // Możliwość wysyłania dodatkowych żądań
                    Console.WriteLine("\nDostępne komendy:");
                    Console.WriteLine("  dict - żądanie Dictionary snapshot (5108)");
                    Console.WriteLine("  dictref - żądanie Dictionary refresh (5109)");
                    Console.WriteLine("  dictall - żądanie Dictionary wszystkie rynki");
                    Console.WriteLine("  simple - prosty test Dictionary z minimalnym GLID");
                    Console.WriteLine("  stock <symbol> - żądanie Stock Watch dla symbolu");
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
                        else if (input?.ToLower() == "dict")
                        {
                            await client.SendDictionaryRequest();
                        }
                        else if (input?.ToLower() == "dictref")
                        {
                            await client.SendDictionaryRequestRefresh();
                        }
                        else if (input?.ToLower() == "dictall")
                        {
                            await client.SendDictionaryRequestAllMarkets();
                        }
                        else if (input?.ToLower() == "simple")
                        {
                            await client.SendSimpleTest();
                        }
                        else if (input?.ToLower() == "test")
                        {
                            Console.WriteLine($"Połączenie aktywne: {client.IsConnected()}");
                        }
                        else if (input?.StartsWith("stock ") == true)
                        {
                            var symbol = input.Substring(6).Trim().ToUpper();
                            await client.SendStockWatchRequest(symbol);
                            await client.WaitForResponse(5000);
                        }
                        else if (!string.IsNullOrEmpty(input))
                        {
                            Console.WriteLine("Nieznana komenda. Dostępne komendy: dict, dictref, dictall, simple, stock <symbol>, test, quit");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Nie udało się nawiązać połączenia.");
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