namespace Cross_FIS_API_1._0.Models
{
    /// <summary>
    /// Konfiguracja połączenia z serwerem FIS
    /// </summary>
    public class FisConnectionConfig
    {
        public string ServerAddress { get; set; } = "localhost";
        public int ServerPort { get; set; } = 12345;
        public string UserNumber { get; set; } = "001";
        public string Password { get; set; } = "password";
        public string DestinationServer { get; set; } = "SLC01";
        public string CallingId { get; set; } = "API01";
        public int TimeoutMs { get; set; } = 30000;
    }
}