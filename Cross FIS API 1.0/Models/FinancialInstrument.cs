using System;
using System.Collections.Generic;

namespace Cross_FIS_API_1._0.Models
{
    /// <summary>
    /// Instrument finansowy z WSE
    /// </summary>
    public class FinancialInstrument
    {
        public string Mnemonic { get; set; } = string.Empty;
        public string ISIN { get; set; } = string.Empty;
        public string StockName { get; set; } = string.Empty;
        public decimal BidPrice { get; set; }
        public decimal AskPrice { get; set; }
        public decimal LastPrice { get; set; }
        public decimal OpenPrice { get; set; }
        public decimal HighPrice { get; set; }
        public decimal LowPrice { get; set; }
        public decimal ClosePrice { get; set; }
        public long Volume { get; set; }
        public decimal PercentageChange { get; set; }
        public string Currency { get; set; } = "PLN";
        public int Market { get; set; }
        public string TradingPhase { get; set; } = string.Empty;
        public DateTime LastUpdateTime { get; set; }
        public string SuspensionIndicator { get; set; } = string.Empty;
        public int NumberOfTrades { get; set; }
        public decimal AmountExchanged { get; set; }
    }

    /// <summary>
    /// Struktura wiadomości FIS
    /// </summary>
    public class FisMessage
    {
        public byte[] Length { get; set; } = new byte[2];
        public byte[] Header { get; set; } = new byte[32];
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public byte[] Footer { get; set; } = new byte[3];

        public int TotalLength => Length[0] + Length[1] * 256;
        public int RequestNumber { get; set; }
        public string CallingId { get; set; } = string.Empty;
        public string CalledId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Typy requestów FIS
    /// </summary>
    public enum FisRequestType
    {
        Connection = 1100,
        Disconnection = 1102,
        StockWatch = 1000,
        StockWatchSubscription = 1001,
        StockWatchReply = 1003,
        RealTimeSubscription = 2017,
        RealTimeUnsubscription = 2018,
        RealTimeMessage = 2019,
        OrderManagement = 2000,
        OrderBookConsultation = 2004,
        RepliesConsultation = 2008
    }

    /// <summary>
    /// Status połączenia z serwerem FIS
    /// </summary>
    public enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        PhysicallyConnected,
        LogicallyConnected,
        Ready,
        Error
    }

    /// <summary>
    /// Dostępne rynki na WSE
    /// </summary>
    public enum WseMarket
    {
        Bonds = 1,
        Cash = 2,
        Options = 3,
        Futures = 4,
        Index = 5,
        OPCVM = 9,
        GrowthMarket = 16,
        FutureIndices = 17,
        Warrants = 20
    }
}