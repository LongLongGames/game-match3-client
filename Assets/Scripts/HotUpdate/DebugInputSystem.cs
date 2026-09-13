using HotUpdate.Auth;
using UnityEngine;
using VContainer.Unity;

namespace HotUpdate
{
    public class DebugInputSystem : ITickable
    {
        // 可以通过构造函数注入你想要调试的其他服务
        private readonly IAuthService _authService;

        public DebugInputSystem(IAuthService authService)
        {
            _authService = authService;
        }

        public void Tick()
        {
            // 只有在编辑器或开发版中才运行调试快捷键
            if (!Debug.isDebugBuild && !Application.isEditor) return;

            if (Input.GetKeyDown(KeyCode.F1))
            {
                SetToken();
            }
            else if (Input.GetKeyDown(KeyCode.F2))
            {
                GetToken();
            }
        }

        public void SetToken()
        {
            PlayerPrefs.SetString("access_token", "test_token");
            Debug.Log("Token set to: " + PlayerPrefs.GetString("access_token"));

            _authService.TryRestoreToken();
            Debug.Log("IsLoggedIn: " + _authService.IsLoggedIn); // 输出
        }

        public void GetToken()
        {
            string token = PlayerPrefs.GetString("access_token", "No token found");
            Debug.Log("Current token: " + token);
        }
    }
}
