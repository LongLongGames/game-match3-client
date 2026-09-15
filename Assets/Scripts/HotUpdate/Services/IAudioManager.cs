namespace HotUpdate.Services
{
    /// <summary>
    /// 全局音频：BGM / UI 音效。开关持久化到 PlayerPrefs。
    /// 资源目录：Assets/Bundles/BGM、Assets/Bundles/SFX
    /// 默认：BGM=Dewfall_at_Daybreak，点击SFX=GetHeart
    /// </summary>
    public interface IAudioManager
    {
        bool BgmEnabled { get; }
        bool SfxEnabled { get; }

        void SetBgmEnabled(bool enabled);
        void SetSfxEnabled(bool enabled);

        /// <summary>播放 BGM。name 为 Bundles/BGM 下文件名（可带或不带扩展名），null 用默认 Dewfall_at_Daybreak。</summary>
        void PlayBgm(string name = null, bool loop = true);

        void StopBgm();

        /// <summary>UI 点击音效（默认 GetHeart）。</summary>
        void PlayUiClick();

        /// <summary>播放 SFX（Bundles/SFX 文件名，可带或不带扩展名）。</summary>
        void PlaySfx(string name);
    }
}
