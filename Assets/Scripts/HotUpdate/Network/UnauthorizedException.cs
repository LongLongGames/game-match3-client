using System;

namespace HotUpdate.Network
{
    /// <summary>
    /// HTTP 401：Token 无效或过期。HTTP 层抛出，业务层应登出并回登录页。
    /// </summary>
    public sealed class UnauthorizedException : Exception
    {
        public long ResponseCode { get; }

        public UnauthorizedException(long responseCode, string message)
            : base(message)
        {
            ResponseCode = responseCode;
        }
    }
}
