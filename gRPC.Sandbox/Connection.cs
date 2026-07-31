    /// <summary>
    /// Настройки подключения к Финам API.
    /// Значения читаются из settings.local.json
    /// </summary>
    public class Connection
    {
        public string TargetUrl { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string SourceAppId { get; set; } = string.Empty;
    }

