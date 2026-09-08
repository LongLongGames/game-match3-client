namespace HotUpdate.Network
{
    /// <summary>
    /// API 配置。可在 Inspector / 远程配置覆盖。
    /// </summary>
    public class ApiConfig
    {
        public string MpBaseUrl { get; set; } = "http://localhost:8080";
        public string GameBaseUrl { get; set; } = "http://localhost:8081";
        public string Channel { get; set; } = "official";
        public string Region { get; set; } = "cn";
        public string GameId { get; set; } = "match3";
        public string BoardId { get; set; } = "default";
        public string AppId { get; set; } = "test_app";
    }
}
