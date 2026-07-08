    /// <summary>
    /// Настройки подключения к Финам API.
    /// Значения читаются из settings.local.json
    /// </summary>
    public class Connection
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
    }

