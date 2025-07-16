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
        private string callingId; // Usunąłem readonly - będzie aktualizowane

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

            // Odczytanie nagłówka
            if (stxPos + 32 <= response.Length)
            {
                var apiVersion = (char)response[stxPos + 1];
                var requestSizeStr = Encoding.ASCII.GetString(response, stxPos + 2, 5);
                var calledIdStr = Encoding.ASCII.GetString(response, stxPos + 7, 5);
                
                // KLUCZOWE: Odczytanie Calling ID z nagłówka odpowiedzi
                var newCallingIdStr = Encoding.ASCII.GetString(response, stxPos + 17, 5);
                var requestNumberStr = Encoding.ASCII.GetString(response, stxPos + 24, 5);

                Console.WriteLine($"API Version: {apiVersion}");
                Console.WriteLine($"Request Size: {requestSizeStr}");
                Console.WriteLine($"Called ID: {calledIdStr}");
                Console.WriteLine($"Calling ID z serwera: {newCallingIdStr}");
                Console.WriteLine($"Request Number: {requestNumberStr}");

                // Aktualizacja Calling ID na podstawie odpowiedzi serwera
                if (!string.IsNullOrWhiteSpace(newCallingIdStr.Trim()))
                {
                    var trimmedCallingId = newCallingIdStr.Trim();
                    if (trimmedCallingId != "00000" && trimmedCallingId != "0")
                    {
                        callingId = newCallingIdStr; // Zachowaj oryginalny format (z paddingiem)
                        Console.WriteLine($"Zaktualizowano Calling ID na: '{callingId}'");
                    }
                }

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

        public async Task SendDictionaryRequest()
        {
            if (stream == null)
            {
                Console.WriteLine("Brak połączenia!");
                return;
            }

            Console.WriteLine($"Wysyłanie żądania Dictionary z Calling ID: '{callingId}'");
            
            var message = BuildDictionaryMessage();
            await stream.WriteAsync(message, 0, message.Length);
            
            Console.WriteLine($"Żądanie Dictionary wysłane. Rozmiar: {message.Length} bajtów");
            
            // Oczekiwanie na odpowiedź
            Console.WriteLine("Oczekiwanie na odpowiedź Dictionary...");
            await WaitForResponse(10000);
        }

        private byte[] BuildDictionaryMessage()
        {
            var dataBuilder = new List<byte>();

            // H0 - Number of GLID (1 GLID)
            dataBuilder.AddRange(Encoding.ASCII.GetBytes("00001"));

            // H1 - GLID dla WSE Cash market
            var wseCashGlid = "004000002000"; // WSE Exchange(40) + Source(00) + Market(002) + Sub-market(000)
            
            Console.WriteLine($"Używany GLID: {wseCashGlid}");
            Console.WriteLine($"Używany Calling ID: '{callingId}'");
            
            dataBuilder.AddRange(EncodeField(wseCashGlid));

            var data = dataBuilder.ToArray();
            
            var totalLength = LG_LENGTH + HEADER_LENGTH + data.Length + FOOTER_LENGTH;
            var message = new byte[totalLength];
            var offset = 0;

            // Długość wiadomości
            message[offset++] = (byte)(totalLength % 256);
            message[offset++] = (byte)(totalLength / 256);

            // Nagłówek - TERAZ Z PRAWIDŁOWYM CALLING ID
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
            // Implementacja podobna do ProcessDictionaryResponse
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
                var connected = await client.ConnectAsync();
                
                if (connected)
                {
                    Console.WriteLine("Połączenie nawiązane pomyślnie!");
                    
                    // Krótka pauza przed wysłaniem Dictionary
                    await Task.Delay(1000);
                    
                    // Automatyczne pobranie listy symboli
                    Console.WriteLine("Pobieranie listy dostępnych symboli...");
                    await client.SendDictionaryRequest();
                    
                    // Więcej czasu na odpowiedź
                    await client.WaitForResponse(15000);
                    
                    Console.WriteLine("\nDostępne komendy:");
                    Console.WriteLine("  dict - żądanie Dictionary");
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
                            await client.WaitForResponse(10000);
                        }
                        else if (input?.ToLower() == "test")
                        {
                            Console.WriteLine($"Połączenie aktywne: {client.IsConnected()}");
                        }
                        else if (!string.IsNullOrEmpty(input))
                        {
                            Console.WriteLine("Nieznana komenda. Dostępne komendy: dict, test, quit");
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