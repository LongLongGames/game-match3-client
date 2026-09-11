namespace HotUpdate.Network
{
    /// <summary>
    /// API 配置。可在 Inspector / 远程配置覆盖。
    /// </summary>
    public class ApiConfig
    {
        public string MpBaseUrl { get; set; } = "http://localhost:11080"; // 中台固定
        public string GameBaseUrl { get; set; } = "http://localhost:13180"; //# 严格按 G=1 公式：13000 + 100 + 80 = 13180
        public string Channel { get; set; } = "official";
        public string Region { get; set; } = "cn";
        public string GameId { get; set; } = "match3";
        public string BoardId { get; set; } = "default";
        public string AppId { get; set; } = "test_app";
    }
}
