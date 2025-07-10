using System;
using System.Text;
using System.Collections.Generic;
using Cross_FIS_API_1._0.Models;

namespace Cross_FIS_API_1._0.Services
{
    /// <summary>
    /// Kodowanie i dekodowanie wiadomości FIS według protokołu API
    /// </summary>
    public class FisMessageEncoder
    {
        private const byte STX = 2;
        private const byte ETX = 3;
        private const int HEADER_LENGTH = 32;
        private const int FOOTER_LENGTH = 3;
        private const int LG_LENGTH = 2;

        /// <summary>
        /// Koduje wiadomość logicznego połączenia (1100)
        /// </summary>
        public byte[] EncodeLogicalConnection(FisConnectionConfig config)
        {
            var data = new List<byte>();
            
            // User Number (3 bajty, dopełnione zerami)
            var userBytes = Encoding.ASCII.GetBytes(config.UserNumber.PadLeft(3, '0'));
            data.AddRange(userBytes);
            
            // Password (16 bajtów, dopełnione spacjami)
            var passwordBytes = Encoding.ASCII.GetBytes(config.Password.PadRight(16, ' '));
            if (passwordBytes.Length > 16)
                Array.Resize(ref passwordBytes, 16);
            data.AddRange(passwordBytes);
            
            // Filler (7 bajtów)
            data.AddRange(new byte[7] { 32, 32, 32, 32, 32, 32, 32 });
            
            return EncodeMessage(data.ToArray(), FisRequestType.Connection, config);
        }

        /// <summary>
        /// Koduje subskrypcję real-time (2017)
        /// </summary>
        public byte[] EncodeRealTimeSubscription()
        {
            var data = new List<byte>();
            
            // E1-E7 (wszystkie ustawione na '1' dla pełnej subskrypcji)
            data.AddRange(Encoding.ASCII.GetBytes("1111111"));
            
            // Filler (11 bajtów)
            data.AddRange(new byte[11] { 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32 });
            
            return EncodeMessage(data.ToArray(), FisRequestType.RealTimeSubscription, null);
        }

        /// <summary>
        /// Koduje request Stock Watch (1000)
        /// </summary>
        public byte[] EncodeStockWatch(string stockCode = "")
        {
            var data = new List<byte>();
            
            // Stock code (jeśli pusty, pobieramy wszystkie instrumenty)
            if (!string.IsNullOrEmpty(stockCode))
            {
                var lengthByte = (byte)(stockCode.Length + 32);
                data.Add(lengthByte);
                data.AddRange(Encoding.ASCII.GetBytes(stockCode));
            }
            
            return EncodeMessage(data.ToArray(), FisRequestType.StockWatch, null);
        }

        /// <summary>
        /// Koduje kompletną wiadomość FIS
        /// </summary>
        private byte[] EncodeMessage(byte[] messageData, FisRequestType requestType, FisConnectionConfig config)
        {
            var totalLength = LG_LENGTH + HEADER_LENGTH + messageData.Length + FOOTER_LENGTH;
            var message = new byte[totalLength];
            var offset = 0;
            
            // Długość wiadomości (2 bajty)
            message[offset++] = (byte)(totalLength % 256);
            message[offset++] = (byte)(totalLength / 256);
            
            // Header (32 bajty)
            var header = EncodeHeader(messageData.Length + HEADER_LENGTH + FOOTER_LENGTH, 
                                    (int)requestType, config);
            Array.Copy(header, 0, message, offset, HEADER_LENGTH);
            offset += HEADER_LENGTH;
            
            // Data
            Array.Copy(messageData, 0, message, offset, messageData.Length);
            offset += messageData.Length;
            
            // Footer (3 bajty)
            message[offset++] = 32; // Spacja
            message[offset++] = 32; // Spacja
            message[offset++] = ETX;
            
            return message;
        }

        /// <summary>
        /// Koduje header wiadomości FIS
        /// </summary>
        private byte[] EncodeHeader(int requestSize, int requestNumber, FisConnectionConfig config)
        {
            var header = new byte[HEADER_LENGTH];
            var offset = 0;
            
            // STX
            header[offset++] = STX;
            
            // API version (V4 = spacja)
            header[offset++] = 32;
            
            // Request size (5 bajtów)
            var requestSizeStr = requestSize.ToString().PadLeft(5, '0');
            Array.Copy(Encoding.ASCII.GetBytes(requestSizeStr), 0, header, offset, 5);
            offset += 5;
            
            // Called logical identifier (5 bajtów)
            string calledId = config?.DestinationServer ?? "SLC01";
            calledId = calledId.PadLeft(5, '0');
            Array.Copy(Encoding.ASCII.GetBytes(calledId), 0, header, offset, 5);
            offset += 5;
            
            // Filler (5 bajtów)
            for (int i = 0; i < 5; i++)
                header[offset++] = 32;
            
            // Calling logical identifier (5 bajtów)
            string callingId = config?.CallingId ?? "API01";
            Array.Copy(Encoding.ASCII.GetBytes(callingId), 0, header, offset, 5);
            offset += 5;
            
            // Filler (2 bajty)
            header[offset++] = 32;
            header[offset++] = 32;
            
            // Request number (5 bajtów)
            var requestNumberStr = requestNumber.ToString().PadLeft(5, '0');
            Array.Copy(Encoding.ASCII.GetBytes(requestNumberStr), 0, header, offset, 5);
            offset += 5;
            
            // Filler (3 bajty)
            for (int i = 0; i < 3; i++)
                header[offset++] = 32;
            
            return header;
        }

        /// <summary>
        /// Dekoduje wiadomość FIS
        /// </summary>
        public FisMessage DecodeMessage(byte[] data)
        {
            if (data.Length < LG_LENGTH + HEADER_LENGTH + FOOTER_LENGTH)
                throw new ArgumentException("Message too short");
            
            var message = new FisMessage();
            var offset = 0;
            
            // Długość
            message.Length[0] = data[offset++];
            message.Length[1] = data[offset++];
            
            // Header
            Array.Copy(data, offset, message.Header, 0, HEADER_LENGTH);
            offset += HEADER_LENGTH;
            
            // Data
            var dataLength = message.TotalLength - LG_LENGTH - HEADER_LENGTH - FOOTER_LENGTH;
            if (dataLength > 0)
            {
                message.Data = new byte[dataLength];
                Array.Copy(data, offset, message.Data, 0, dataLength);
                offset += dataLength;
            }
            
            // Footer
            Array.Copy(data, offset, message.Footer, 0, FOOTER_LENGTH);
            
            // Dekoduj header
            DecodeHeader(message);
            
            return message;
        }

        /// <summary>
        /// Dekoduje header wiadomości
        /// </summary>
        private void DecodeHeader(FisMessage message)
        {
            var offset = 1; // Skip STX
            
            // Skip API version
            offset++;
            
            // Skip request size
            offset += 5;
            
            // Called ID
            var calledIdBytes = new byte[5];
            Array.Copy(message.Header, offset, calledIdBytes, 0, 5);
            message.CalledId = Encoding.ASCII.GetString(calledIdBytes).Trim();
            offset += 5;
            
            // Skip filler
            offset += 5;
            
            // Calling ID
            var callingIdBytes = new byte[5];
            Array.Copy(message.Header, offset, callingIdBytes, 0, 5);
            message.CallingId = Encoding.ASCII.GetString(callingIdBytes).Trim();
            offset += 5;
            
            // Skip filler
            offset += 2;
            
            // Request number
            var requestNumberBytes = new byte[5];
            Array.Copy(message.Header, offset, requestNumberBytes, 0, 5);
            var requestNumberStr = Encoding.ASCII.GetString(requestNumberBytes).Trim();
            if (int.TryParse(requestNumberStr, out int requestNumber))
                message.RequestNumber = requestNumber;
        }

        /// <summary>
        /// Dekoduje dane Stock Watch
        /// </summary>
        public List<FinancialInstrument> DecodeStockWatch(byte[] data)
        {
            var instruments = new List<FinancialInstrument>();
            
            // Implementacja dekodowania danych Stock Watch
            // Zgodnie z dokumentacją, pozycje 0-236 zawierają różne pola
            
            try
            {
                var instrument = new FinancialInstrument();
                var offset = 0;
                
                // Dekoduj pola według dokumentacji FIS
                // H1 - Mnemo (pozycja 0)
                if (data.Length > offset)
                {
                    var mnemoLength = data[offset] - 32;
                    if (mnemoLength > 0 && data.Length > offset + 1 + mnemoLength)
                    {
                        var mnemoBytes = new byte[mnemoLength];
                        Array.Copy(data, offset + 1, mnemoBytes, 0, mnemoLength);
                        instrument.Mnemonic = Encoding.ASCII.GetString(mnemoBytes);
                        offset += 1 + mnemoLength;
                    }
                }
                
                // Pozostałe pola można dekodować podobnie
                // Na razie tworzymy przykładowy instrument
                if (string.IsNullOrEmpty(instrument.Mnemonic))
                    instrument.Mnemonic = "SAMPLE";
                
                instrument.ISIN = "PLSAMPLE001";
                instrument.StockName = "Sample Company";
                instrument.LastUpdateTime = DateTime.Now;
                
                instruments.Add(instrument);
            }
            catch (Exception ex)
            {
                // Log błędu
                Console.WriteLine($"Error decoding stock watch data: {ex.Message}");
            }
            
            return instruments;
        }

        /// <summary>
        /// Tworzy identyfikator klienta (16 bajtów)
        /// </summary>
        public byte[] CreateClientIdentifier(string clientName = "FISAPICLIENT")
        {
            var identifier = new byte[16];
            var nameBytes = Encoding.ASCII.GetBytes(clientName);
            Array.Copy(nameBytes, 0, identifier, 0, Math.Min(nameBytes.Length, 16));
            
            // Wypełnij resztę spacjami
            for (int i = nameBytes.Length; i < 16; i++)
                identifier[i] = 32;
            
            return identifier;
        }
    }
}